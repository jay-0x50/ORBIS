"""Guarded exact-file ORBIS delivery. Default dry-run; --apply is explicit.

The CLI destination is fixed to D:/Project/ORBIS. No rollback or deletion is
performed. This is per-file atomic replacement, not a project-wide transaction.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import sys
import time
import uuid

import prepare_delivery as planner

PROJECT_ROOT = Path(__file__).resolve().parents[2]
ALLOWED_TARGET = Path('D:/Project/ORBIS')
PINNED_BASELINE_COUNT = planner.BASELINE_COUNT
PINNED_BASELINE_SHA = planner.BASELINE_SHA256


class DeliveryError(RuntimeError):
    pass


def fixed_roots():
    # Resolving a junction first would silently redefine the fixed destination.
    # Reject aliases at the project root as well as within individual file paths.
    raw_staging, raw_target = PROJECT_ROOT.absolute(), ALLOWED_TARGET.absolute()
    staging, target = raw_staging.resolve(), raw_target.resolve()
    if str(raw_staging).casefold() != str(staging).casefold() or str(raw_target).casefold() != str(target).casefold():
        raise DeliveryError('Fixed project roots may not use symlinks, junctions or path aliases.')
    return staging,target


def sha_rows(rows):
    return hashlib.sha256(json.dumps(rows,sort_keys=True,separators=(',', ':')).encode()).hexdigest()


def require_stamp(path, expected, label):
    actual = planner.stamp(path)
    got = actual['sha256'] if actual else None
    if got != expected:
        raise DeliveryError(f'{label} changed or missing: {path}; expected {expected}, actual {got}')
    return actual


def project_file(path, staging):
    resolved = Path(path).resolve()
    if not planner.contained(resolved,staging) or resolved == staging:
        raise DeliveryError('Input must be a file inside the fixed staging project: '+str(path))
    relative = planner.normalize(resolved.relative_to(staging).as_posix())
    if planner.protected(relative):
        raise DeliveryError('Protected input path: '+relative)
    return planner.scoped_file(staging,relative)


def validate_plan(plan_path):
    staging, target = fixed_roots()
    if not staging.is_dir() or not target.is_dir() or staging == target:
        raise DeliveryError('Fixed staging and fixed target must be distinct existing projects.')
    if planner.contained(staging,target) or planner.contained(target,staging):
        raise DeliveryError('Staging and target may not contain one another.')
    plan_path = project_file(plan_path,staging)
    plan_stamp = planner.stamp(plan_path)
    plan = planner.load_json(plan_path)
    if not isinstance(plan,dict) or not isinstance(plan.get('summary'),dict) or not isinstance(plan.get('metadata'),dict):
        raise DeliveryError('Plan, summary and metadata must be JSON objects.')
    if plan.get('schemaVersion') != 1 or plan.get('mode') != 'plan-only' or plan.get('status') != 'ready_for_copy_review':
        raise DeliveryError('Expected a ready schema-1 plan-only delivery report.')
    if plan.get('conflicts') != [] or plan.get('summary',{}).get('conflicts') != 0:
        raise DeliveryError('Any planned conflict prohibits the complete apply operation.')
    if Path(plan.get('stagingRoot','')).resolve() != staging or Path(plan.get('targetRoot','')).resolve() != target:
        raise DeliveryError('Plan project roots do not match this script and the fixed D:/Project/ORBIS target.')
    inputs = plan.get('inputs',{})
    if not isinstance(inputs,dict):
        raise DeliveryError('Plan inputs must be a JSON object.')
    if inputs.get('baselineCount') != PINNED_BASELINE_COUNT or inputs.get('baselineSha256') != PINNED_BASELINE_SHA:
        raise DeliveryError('Pinned baseline count/SHA mismatch.')
    paths = {}
    for key,hash_key in [('baseline','baselineSha256'),('dependencyManifest','dependencySha256'),('supplemental','supplementalSha256')]:
        paths[key] = project_file(inputs.get(key,''),staging)
        require_stamp(paths[key],planner.valid_hash(inputs.get(hash_key)),key)
    planned = plan.get('plannedFiles')
    if not isinstance(planned,list) or sha_rows(planned) != plan.get('planSha256'):
        raise DeliveryError('Canonical plannedFiles SHA mismatch.')
    for row in planned:
        if not isinstance(row,dict):
            raise DeliveryError('Every planned file must be a JSON object.')
        path = planner.normalize(row.get('path'))
        if path != row['path'] or planner.protected(path):
            raise DeliveryError('Noncanonical/protected delivery file: '+path)
        if row.get('disposition') not in ('plan_new_file','plan_replace_baseline_unchanged'):
            raise DeliveryError('Unsupported delivery disposition: '+path)
        planner.valid_hash(row.get('stagingSha256'))
        planner.scoped_file(staging,path)
        planner.scoped_file(target,path)
    meta_scope = plan.get('metadata',{}).get('scope')
    if meta_scope not in ('none','selected','target-assets'):
        raise DeliveryError('Missing or unknown metadata verification scope.')
    if meta_scope == 'none' and any(r['path'].casefold().startswith('assets/') for r in planned):
        raise DeliveryError('Applying Assets requires at least selected metadata verification.')

    # Reconstruct the entire plan from pinned input bytes and current source/target
    # state. A recomputed digest alone cannot authorize attacker-added planned rows.
    fresh = planner.make_plan(staging,target,paths['baseline'],paths['dependencyManifest'],paths['supplemental'],
                              meta_scope,expected_count=PINNED_BASELINE_COUNT,expected_sha=PINNED_BASELINE_SHA)
    if fresh['status'] != 'ready_for_copy_review' or fresh['conflicts']:
        raise DeliveryError('Current full preflight has conflicts: '+json.dumps(fresh['conflicts'],ensure_ascii=False))
    for key in ('plannedFiles','files','summary'):
        # Outside-scope baseline audits may change without changing the exact-file
        # delivery. All explicit source/target file rows still must match exactly.
        before,after = plan.get(key),fresh.get(key)
        if key == 'summary':
            before = {k:v for k,v in before.items() if k!='baselineTargetChanges'}
            after = {k:v for k,v in after.items() if k!='baselineTargetChanges'}
        if before != after:
            raise DeliveryError('Current preflight differs from reviewed plan: '+key)
    if fresh['metadata']['witnesses'] != plan['metadata'].get('witnesses'):
        raise DeliveryError('Metadata source/target/GUID witnesses changed.')
    if fresh['planSha256'] != plan['planSha256']:
        raise DeliveryError('Current canonical plan hash differs.')
    # Recheck every explicit source/target and required metadata before any target
    # mutation, including skipped files and metadata not copied because it is equal.
    for row in fresh['files']:
        require_stamp(planner.scoped_file(staging,row['path']),row['stagingSha256'],'source preflight')
        require_stamp(planner.scoped_file(target,row['path']),row['targetSha256'],'target preflight')
    for row in fresh['metadata']['witnesses']:
        require_stamp(planner.scoped_file(staging,row['path']),row['stagingSha256'],'source meta preflight')
        require_stamp(planner.scoped_file(target,row['path']),row['targetSha256'],'target meta preflight')
    for key,hash_key in [('baseline','baselineSha256'),('dependencyManifest','dependencySha256'),('supplemental','supplementalSha256')]:
        require_stamp(paths[key],inputs[hash_key],key+' final preflight')
    require_stamp(plan_path,plan_stamp['sha256'],'plan final preflight')
    return plan,fresh,plan_stamp


def stream_verified_copy(source, destination, expected_sha):
    """Exclusive create only; callers choose a fresh staging backup or target temp."""
    before = source.stat()
    digest = hashlib.sha256()
    count = 0
    with source.open('rb') as incoming, destination.open('xb') as outgoing:
        while chunk := incoming.read(1024*1024):
            outgoing.write(chunk)
            digest.update(chunk)
            count += len(chunk)
        outgoing.flush()
        os.fsync(outgoing.fileno())
    after = source.stat()
    if (before.st_size,before.st_mtime_ns) != (after.st_size,after.st_mtime_ns) or digest.hexdigest() != expected_sha:
        raise DeliveryError('Source changed during streaming copy: '+str(source))
    require_stamp(destination,expected_sha,'written copy')
    return {'sha256':digest.hexdigest(),'bytes':count,'sourceMtimeNs':before.st_mtime_ns}


def _replace_target(temporary, target):
    os.replace(temporary,target)


def write_journal(run_dir, report):
    report['updatedUtc'] = dt.datetime.now(dt.timezone.utc).isoformat()
    temporary = run_dir/('journal-'+uuid.uuid4().hex+'.tmp')
    with temporary.open('x',encoding='utf-8',newline='\n') as stream:
        json.dump(report,stream,ensure_ascii=False,indent=2)
        stream.write('\n'); stream.flush(); os.fsync(stream.fileno())
    # Only our staging journal may retry transient Windows access/sharing errors.
    # Keep the same verified temp on failure; never retry a target-file replace.
    for attempt in range(5):
        try:
            os.replace(temporary,run_dir/'Run.json')
            break
        except PermissionError as error:
            if getattr(error,'winerror',None) not in (5,32,33) or attempt == 4:
                raise
            time.sleep(0.1*(attempt+1))


def deliver(plan_path, run_dir, apply=False):
    """Production policy is fixed by module constants; CLI exposes no target override."""
    staging, target = fixed_roots()
    run_dir = Path(run_dir).resolve()
    # Validate the original report-base path before resolving it. Otherwise a
    # DeliveryRuns junction into Assets could redirect even a dry-run report.
    planner.scoped_file(staging,'Tools/CharacterPipeline/DeliveryRuns')
    allowed_output = (staging/'Tools/CharacterPipeline/DeliveryRuns').resolve()
    if not planner.contained(run_dir,allowed_output) or run_dir == allowed_output or run_dir.exists():
        raise DeliveryError('Use a fresh run directory below staging Tools/CharacterPipeline/DeliveryRuns.')
    planner.scoped_file(staging,run_dir.relative_to(staging).as_posix())
    run_dir.mkdir(parents=True,exist_ok=False)
    start = time.monotonic()
    report = {'schemaVersion':1,'status':'validating','applyRequested':bool(apply),'targetRoot':str(target),
              'stagingRoot':str(staging),'planPath':str(Path(plan_path).resolve()),'runDirectory':str(run_dir),
              'createdUtc':dt.datetime.now(dt.timezone.utc).isoformat(),'targetMutationStarted':False,
              'copied':[],'backups':[],'temporaryFiles':[],'currentFile':None,'error':None,
              'notes':['No automatic rollback: user changes are never overwritten to restore an earlier state.',
                       'Only per-file replacement is atomic. An interrupted run can be partially applied.',
                       'Failed owned temporary files/backups are retained at recorded paths; no deletion is attempted.']}
    write_journal(run_dir,report)
    try:
        plan,fresh,plan_stamp = validate_plan(plan_path)
        report.update({'planSha256':plan['planSha256'],'planFileSha256':plan_stamp['sha256'],
                       'plannedFileCount':len(plan['plannedFiles']),'plannedBytes':sum(r['bytes'] for r in plan['plannedFiles']),
                       'metadataScope':plan['metadata']['scope'],'preflightComplete':True,
                       'outsideScopeBaselineChanges':[r for r in fresh['baselineTargetAudit'] if not r['selected']]})
        if not apply:
            report['status']='dry_run_verified'
            return report
        report['status']='applying'
        write_journal(run_dir,report)
        for row in plan['plannedFiles']:
            relative=planner.normalize(row['path'])
            if planner.protected(relative):
                raise DeliveryError('Protected path reached mutation loop: '+relative)
            source=planner.scoped_file(staging,relative)
            destination=planner.scoped_file(target,relative)
            report['currentFile']={'path':relative,'phase':'checking','targetBeforeSha256':row['targetSha256'],
                                   'expectedNewSha256':row['stagingSha256']}
            write_journal(run_dir,report)
            require_stamp(source,row['stagingSha256'],'source before file')
            require_stamp(destination,row['targetSha256'],'target before file')
            if row['targetSha256'] is not None:
                backup=run_dir/'backups'/relative
                if not planner.contained(backup.resolve(),run_dir):
                    raise DeliveryError('Backup path escaped fresh staging run.')
                backup.parent.mkdir(parents=True,exist_ok=True)
                record={'path':relative,'backupPath':str(backup),'expectedSha256':row['targetSha256'],'status':'writing'}
                report['backups'].append(record)
                write_journal(run_dir,report)
                record.update(stream_verified_copy(destination,backup,row['targetSha256']))
                record['status']='verified'
                require_stamp(destination,row['targetSha256'],'target after backup')
                write_journal(run_dir,report)
            # Only now can target directories/temp bytes be created. Never change
            # read-only attributes to force a replace, and never follow junctions.
            planner.scoped_file(target,relative)
            report['targetMutationStarted']=True
            report['currentFile']['phase']='creating_target_temp'
            temporary=destination.parent/('.'+destination.name+'.orbis-delivery-'+uuid.uuid4().hex+'.tmp')
            report['temporaryFiles'].append({'path':str(temporary),'target':relative,'status':'pending'})
            write_journal(run_dir,report)
            destination.parent.mkdir(parents=True,exist_ok=True)
            planner.scoped_file(target,relative)
            stream_verified_copy(source,temporary,row['stagingSha256'])
            report['temporaryFiles'][-1]['status']='verified'
            # Recheck immediately before replacement. A target changed after
            # preflight or backup remains the user's version; do not overwrite it.
            report['currentFile']['phase']='ready_to_replace'
            write_journal(run_dir,report)
            planner.scoped_file(target,relative)
            require_stamp(source,row['stagingSha256'],'source immediately before replace')
            require_stamp(temporary,row['stagingSha256'],'temp immediately before replace')
            require_stamp(destination,row['targetSha256'],'target immediately before replace')
            _replace_target(temporary,destination)
            record={'path':relative,'sha256':row['stagingSha256'],'bytes':row['bytes'],
                    'targetBeforeSha256':row['targetSha256'],'verified':False}
            report['copied'].append(record)
            report['temporaryFiles'][-1]['status']='consumed_by_replace'
            write_journal(run_dir,report)
            require_stamp(destination,row['stagingSha256'],'target immediately after replace')
            record['verified']=True
            report['currentFile']['phase']='verified'
            write_journal(run_dir,report)
        report['status']='verifying_complete_delivery'
        write_journal(run_dir,report)
        for row in plan['plannedFiles']:
            require_stamp(planner.scoped_file(target,row['path']),row['stagingSha256'],'final copied target')
        # Also detect concurrent changes to skipped/required metadata dependencies.
        for row in fresh['files']:
            require_stamp(planner.scoped_file(target,row['path']),row['stagingSha256'],'final dependency target')
        for row in fresh['metadata']['witnesses']:
            require_stamp(planner.scoped_file(target,row['path']),row['stagingSha256'],'final metadata target')
        report['status']='applied_verified'
        report['currentFile']=None
        report['copiedFileCount']=len(report['copied'])
        report['copiedBytes']=sum(r['bytes'] for r in report['copied'])
        return report
    except (DeliveryError,planner.PlanError,OSError,ValueError,TypeError,KeyError) as error:
        report['status']='failed_partial_no_rollback' if report['targetMutationStarted'] else 'failed_preflight_or_backup'
        report['error']={'type':type(error).__name__,'message':str(error)}
        return report
    finally:
        report['elapsedSeconds']=time.monotonic()-start
        write_journal(run_dir,report)


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--plan',type=Path,required=True)
    parser.add_argument('--run-dir',type=Path,required=True,help='Fresh staging Tools/CharacterPipeline/DeliveryRuns/<name> directory.')
    parser.add_argument('--apply',action='store_true',help='Explicitly mutate only the fixed D:/Project/ORBIS target after full verification.')
    args=parser.parse_args(argv)
    try:
        result=deliver(args.plan,args.run_dir,args.apply)
    except (DeliveryError,planner.PlanError,OSError,ValueError) as error:
        print('DELIVERY REFUSED: '+str(error),file=sys.stderr)
        return 2
    print(json.dumps({k:result.get(k) for k in ['status','applyRequested','runDirectory','plannedFileCount','copiedFileCount','copiedBytes','error']},ensure_ascii=False))
    return 0 if result['status'] in ('dry_run_verified','applied_verified') else 2


if __name__=='__main__':
    raise SystemExit(main())
