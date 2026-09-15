"""Plan an exact, guarded ORBIS file delivery. Never copies files or edits Assets.

Only the requested report is written. A plan is not permission to apply later:
an eventual copier must recheck every source/destination/metadata hash again.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys

BASELINE_COUNT = 1793
BASELINE_SHA256 = '3a3190b61b936c74b8c6e1c53a4e6ffbebf924527b012b52a017eefd42868b7d'
HEX64 = re.compile(r'^[0-9a-fA-F]{64}$')
GUID = re.compile(r'^guid:\s*([0-9a-fA-F]{32})\s*$', re.MULTILINE)
RESERVED = re.compile(r'^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)', re.I)


class PlanError(ValueError):
    pass


def normalize(value):
    if not isinstance(value, str) or not value:
        raise PlanError('A nonempty project-relative file path is required.')
    value = value.replace('\\', '/')
    parts = value.split('/')
    if value.startswith('/') or any(p in ('', '.', '..') for p in parts):
        raise PlanError('Absolute, empty, dot and traversal path components are forbidden: ' + value)
    if any(any(c in p for c in ':*?"<>|') or any(ord(c) < 32 for c in p)
           or p.endswith((' ', '.')) or RESERVED.match(p) for p in parts):
        raise PlanError('Nonportable/ambiguous Windows file path: ' + value)
    return '/'.join(parts)


def contained(path, root):
    return path == root or root in path.parents


def scoped_file(root, relative):
    """Resolve existing ancestors too, so a junction cannot escape the chosen root."""
    path = root.joinpath(*relative.split('/'))
    resolved = path.resolve()
    if not contained(resolved, root):
        raise PlanError('Resolved path escapes project root: ' + str(path))
    cursor = path
    while cursor != root:
        if cursor.is_symlink() or (hasattr(cursor, 'is_junction') and cursor.is_junction()):
            raise PlanError('Symlink/junction paths are not delivery inputs: ' + str(cursor))
        if cursor != path and cursor.exists() and not cursor.is_dir():
            raise PlanError('A file blocks a required parent directory: ' + str(cursor))
        cursor = cursor.parent
    return path


def protected(relative):
    low = relative.casefold()
    if low in ('assets/blend', 'assets/blend.meta') or low.startswith('assets/blend/'):
        return 'protected_original_blend_tree'
    if low.split('/')[0] in {'.git', '.codex', '.agents', 'library', 'temp', 'obj', 'logs', 'usersettings'}:
        return 'project_local_or_repository_internal'
    return None


def stamp(path):
    if not path.exists():
        return None
    if not path.is_file():
        raise PlanError('Explicit input/destination is not a regular file: ' + str(path))
    before = path.stat()
    with path.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    after = path.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise PlanError('File changed during hash read: ' + str(path))
    return {'sha256': digest, 'bytes': after.st_size}


def load_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def valid_hash(value, nullable=False):
    if nullable and value in (None, ''):
        return None
    if not isinstance(value, str) or not HEX64.fullmatch(value):
        raise PlanError('Expected SHA256, got ' + repr(value))
    return value.lower()


def make_plan(staging, target, baseline_path, manifest_path, supplemental_path,
              meta_check='selected', expected_count=BASELINE_COUNT, expected_sha=BASELINE_SHA256):
    staging, target = Path(staging).resolve(), Path(target).resolve()
    if not staging.is_dir() or not target.is_dir() or staging == target:
        raise PlanError('Distinct existing staging and target project directories are required.')
    if contained(staging, target) or contained(target, staging):
        raise PlanError('Staging and target must not contain one another.')
    baseline_path, manifest_path, supplemental_path = map(Path, (baseline_path, manifest_path, supplemental_path))
    baseline_stamp = stamp(baseline_path)
    if baseline_stamp is None or baseline_stamp['sha256'] != expected_sha:
        raise PlanError('Pinned baseline SHA256 mismatch; no plan produced.')
    raw_baseline = load_json(baseline_path)
    if not isinstance(raw_baseline, dict) or len(raw_baseline) != expected_count:
        raise PlanError(f'Expected exactly {expected_count} baseline file entries.')
    baseline = {}
    for key, value in raw_baseline.items():
        path = normalize(key)
        if path.casefold() in baseline:
            raise PlanError('Case-insensitive duplicate baseline path: ' + path)
        baseline[path.casefold()] = {'path': path, 'sha256': valid_hash(value)}
    manifest_stamp, supplemental_stamp = stamp(manifest_path), stamp(supplemental_path)
    manifest, supplemental = load_json(manifest_path), load_json(supplemental_path)
    if not isinstance(manifest, dict) or not isinstance(manifest.get('files'), list):
        raise PlanError('Dependency manifest must contain a files[] array.')
    if not isinstance(supplemental, list) or not all(isinstance(p, str) for p in supplemental):
        raise PlanError('Supplemental JSON must be an explicit array of file path strings.')
    for key, expected in [('stagingRoot', staging), ('targetRoot', target)]:
        if manifest.get(key) and Path(manifest[key]).resolve() != expected:
            raise PlanError('Manifest project root mismatch: ' + key)
    rows, conflicts = {}, []

    def conflict(code, path=None, **detail):
        conflicts.append({'code': code, 'path': path, **detail})

    def add(value, origin, expected=None, target_witness=None, has_target_witness=False):
        path = normalize(value)
        key = path.casefold()
        if key in rows:
            row = rows[key]
            if row['path'] != path:
                conflict('case_alias_input', path, first=row['path'])
            if expected is not None and row['expectedSha256'] not in (None, expected):
                conflict('contradictory_manifest_hash', path)
            if has_target_witness and row['hasManifestTargetWitness'] and row['manifestTargetSha256'] != target_witness:
                conflict('contradictory_target_witness', path)
            if row['expectedSha256'] is None and expected is not None:
                row['expectedSha256'] = expected
            if has_target_witness and not row['hasManifestTargetWitness']:
                row['hasManifestTargetWitness'] = True
                row['manifestTargetSha256'] = target_witness
            row['origins'].append(origin)
            return
        rows[key] = {'path': path, 'expectedSha256': expected, 'origins': [origin],
                     'hasManifestTargetWitness': has_target_witness, 'manifestTargetSha256': target_witness}

    for index, record in enumerate(manifest['files']):
        if not isinstance(record, dict) or 'path' not in record or 'sha256' not in record:
            raise PlanError('Each dependency record requires path and sha256.')
        add(record['path'], 'dependency', valid_hash(record['sha256']),
            valid_hash(record.get('targetSha256'), nullable=True), 'targetSha256' in record)
    for path in supplemental:
        add(path, 'supplemental')

    target_cache = {}

    def target_stamp(path):
        key = path.casefold()
        if key not in target_cache:
            target_cache[key] = stamp(scoped_file(target, path))
        return target_cache[key]

    baseline_audit = []
    for entry in sorted(baseline.values(), key=lambda x: x['path'].casefold()):
        actual = target_stamp(entry['path'])
        if actual is None or actual['sha256'] != entry['sha256']:
            baseline_audit.append({'path': entry['path'], 'baselineSha256': entry['sha256'],
                'targetSha256': actual['sha256'] if actual else None, 'selected': entry['path'].casefold() in rows})

    files = []
    for row in sorted(rows.values(), key=lambda x: x['path'].casefold()):
        path = row['path']
        if protected(path):
            conflict(protected(path), path)
            row['disposition'] = 'conflict'
            files.append(row)
            continue
        source = stamp(scoped_file(staging, path))
        dest = target_stamp(path)
        old = baseline.get(path.casefold())
        row.update({'stagingSha256': source['sha256'] if source else None, 'bytes': source['bytes'] if source else 0,
                    'targetSha256': dest['sha256'] if dest else None, 'baselineSha256': old['sha256'] if old else None})
        reason = None
        if source is None:
            reason = 'staging_file_missing'
        elif row['expectedSha256'] is not None and source['sha256'] != row['expectedSha256']:
            reason = 'staging_changed_since_dependency_manifest'
        elif dest is not None and dest['sha256'] == source['sha256']:
            # Already identical files need no overwrite, even after a prior completed delivery.
            row['disposition'] = 'skip_same_bytes'
        elif row['hasManifestTargetWitness'] and row['manifestTargetSha256'] != (dest['sha256'] if dest else None):
            reason = 'target_changed_since_dependency_manifest'
        elif old is not None and dest is None:
            reason = 'baselined_target_deleted_do_not_restore'
        elif old is not None and dest['sha256'] != old['sha256']:
            reason = 'target_changed_since_baseline'
        elif dest is not None and old is None:
            # A freshly recorded target hash is evidence, never overwrite authorization.
            reason = 'existing_target_without_baseline'
        else:
            row['disposition'] = 'plan_replace_baseline_unchanged' if dest is not None else 'plan_new_file'
        if reason:
            row['disposition'] = 'conflict'
            conflict(reason, path, stagingSha256=row['stagingSha256'], targetSha256=row['targetSha256'],
                     baselineSha256=row['baselineSha256'], manifestTargetSha256=row['manifestTargetSha256'])
        files.append(row)

    metadata = {'scope': meta_check, 'sourceMetasChecked': 0, 'targetMetasScanned': 0,
                'witnesses': [], 'note': 'No metadata/GUID check requested.'}
    if meta_check != 'none':
        metadata['note'] = ('Selected Assets files and ancestor folder metas; GUID uniqueness among these source metas only.'
                            if meta_check == 'selected' else
                            'Selected Assets files/ancestor metas plus GUID collisions against all untouched target Assets metas. Packages excluded.')
        required = set()
        for row in files:
            path = row['path']
            if not path.casefold().startswith('assets/') or protected(path):
                continue
            if path.lower().endswith('.meta'):
                required.add(path)
                asset = scoped_file(staging, path[:-5])
                if not asset.exists():
                    conflict('orphan_source_meta', path)
            else:
                required.add(path + '.meta')
            parent = PurePosixPath(path).parent
            while str(parent).casefold() not in ('assets', '.'):
                required.add(str(parent) + '.meta')
                parent = parent.parent
        guid_paths = {}
        for path in sorted(required, key=str.casefold):
            source_path = scoped_file(staging, path)
            source = stamp(source_path)
            if source is None:
                conflict('missing_source_meta', path)
                continue
            dest = target_stamp(path)
            if path.casefold() not in rows and (dest is None or dest['sha256'] != source['sha256']):
                conflict('required_meta_not_explicitly_selected', path)
            matches = GUID.findall(source_path.read_text(encoding='utf-8-sig'))
            if len(matches) != 1:
                conflict('invalid_or_missing_guid', path)
                continue
            guid = matches[0].lower()
            guid_paths.setdefault(guid, []).append(path)
            metadata['witnesses'].append({'path': path, 'guid': guid, 'stagingSha256': source['sha256'],
                                          'targetSha256': dest['sha256'] if dest else None})
        metadata['sourceMetasChecked'] = len(metadata['witnesses'])
        for guid, paths in guid_paths.items():
            if len(set(p.casefold() for p in paths)) > 1:
                conflict('duplicate_selected_guid', guid=guid, paths=paths)
        if meta_check == 'target-assets':
            # Read exact .meta files. Never add these files to the delivery list.
            required_keys = {p.casefold() for p in required}
            for source_path in (target / 'Assets').rglob('*.meta'):
                path = source_path.relative_to(target).as_posix()
                scoped_file(target, path)
                metadata['targetMetasScanned'] += 1
                if path.casefold() in required_keys:
                    continue  # selected source metadata describes the resulting path
                matches = GUID.findall(source_path.read_text(encoding='utf-8-sig'))
                for guid in matches:
                    if guid.lower() in guid_paths:
                        conflict('selected_guid_collides_with_untouched_target', path, guid=guid.lower(), selected=guid_paths[guid.lower()])

    planned = [r for r in files if r.get('disposition', '').startswith('plan_')]
    same = [r for r in files if r.get('disposition') == 'skip_same_bytes']
    digest_rows = [{k: r[k] for k in ['path','stagingSha256','targetSha256','baselineSha256','bytes','disposition']} for r in planned]
    plan_hash = hashlib.sha256(json.dumps(digest_rows, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    return {'schemaVersion': 1, 'mode': 'plan-only', 'status': 'blocked_conflicts' if conflicts else 'ready_for_copy_review',
        'createdUtc': dt.datetime.now(dt.timezone.utc).isoformat(), 'stagingRoot': str(staging), 'targetRoot': str(target),
        'inputs': {'dependencyManifest': str(manifest_path.resolve()), 'dependencySha256': manifest_stamp['sha256'],
                   'supplemental': str(supplemental_path.resolve()), 'supplementalSha256': supplemental_stamp['sha256'],
                   'baseline': str(baseline_path.resolve()), 'baselineSha256': baseline_stamp['sha256'], 'baselineCount': len(baseline)},
        'summary': {'uniqueExplicitFiles': len(files), 'planFiles': len(planned), 'planBytes': sum(r['bytes'] for r in planned),
                    'newFiles': sum(r['disposition']=='plan_new_file' for r in planned),
                    'replaceFiles': sum(r['disposition']=='plan_replace_baseline_unchanged' for r in planned),
                    'sameBytesSkipped': len(same), 'sameBytesSkippedBytes': sum(r['bytes'] for r in same),
                    'conflicts': len(conflicts), 'baselineTargetChanges': len(baseline_audit)},
        'planSha256': plan_hash, 'plannedFiles': digest_rows, 'files': files, 'conflicts': conflicts,
        'baselineTargetAudit': baseline_audit, 'metadata': metadata,
        'limits': ['Nothing copied, deleted, applied, exported, imported or executed in Unity.',
                  'Supplemental paths are exact files only; directories, glob patterns and recursive copy are unsupported.',
                  'Fresh manifest targetSha256 is a read witness, never authority to overwrite an unbaselined existing file.',
                  'Baseline target changes outside the explicit delivery scope are reported, not overwritten or silently adopted.',
                  'No Assets/blend file or its root folder meta may enter a copy plan.',
                  'A later copier must reject any conflict and rehash all input/source/destination/meta witnesses immediately before mutation.',
                  'No Unity GUID binding, serialized scene semantics, Addressables build or complete dependency closure is proven by this Python planner.']}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    project = Path(__file__).resolve().parents[2]
    parser.add_argument('--staging', type=Path, default=project)
    parser.add_argument('--target', type=Path, default=Path('D:/Project/ORBIS'))
    parser.add_argument('--baseline', type=Path, default=project/'Tools/CharacterPipeline/ProjectBaseline.json')
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--supplemental', type=Path, required=True, help='JSON array of exact additional project-relative file paths; [] is valid.')
    parser.add_argument('--meta-check', choices=['none','selected','target-assets'], default='selected')
    parser.add_argument('--output', type=Path, required=True, help='New report path under staging Tools/CharacterPipeline; existing reports are not overwritten.')
    args = parser.parse_args(argv)
    output = args.output.resolve()
    permitted = (args.staging.resolve()/'Tools/CharacterPipeline').resolve()
    if not contained(output, permitted) or output.exists() or output.suffix.lower() != '.json':
        parser.error('Output must be a new .json file under staging Tools/CharacterPipeline.')
    try:
        report = make_plan(args.staging,args.target,args.baseline,args.manifest,args.supplemental,args.meta_check)
    except (PlanError, OSError, json.JSONDecodeError, UnicodeError) as error:
        print('PLAN REFUSED: ' + str(error), file=sys.stderr)
        return 2
    output.parent.mkdir(parents=True,exist_ok=True)
    # Exclusive create avoids replacing even a report created concurrently after the initial check.
    with output.open('x',encoding='utf-8',newline='\n') as stream:
        json.dump(report,stream,ensure_ascii=False,indent=2)
        stream.write('\n')
    print(json.dumps({'status':report['status'],'output':str(output),**report['summary']},ensure_ascii=False))
    return 2 if report['conflicts'] else 0


if __name__ == '__main__':
    raise SystemExit(main())
