"""Planner safety tests use synthetic projects under Tools, never active Assets."""
import hashlib
import json
from pathlib import Path
import shutil
import unittest
import uuid

from prepare_delivery import make_plan, normalize, PlanError, contained


def digest(data):
    return hashlib.sha256(data.encode()).hexdigest()


class PlannerTests(unittest.TestCase):
    def setUp(self):
        scope = Path(__file__).resolve().parent / 'DeliveryPlannerTests'
        scope.mkdir(exist_ok=True)
        # Python 3.14 TemporaryDirectory creates owner-only Windows ACLs that the
        # restricted worker token cannot reopen. Ordinary mkdir inherits Tools ACLs.
        self.root = (scope/('synthetic_'+uuid.uuid4().hex)).resolve()
        self.root.mkdir()
        # Verify the exact recursive-cleanup target before registering cleanup.
        if not contained(self.root, scope.resolve()) or self.root == scope.resolve():
            raise RuntimeError('Unsafe synthetic fixture cleanup path')
        self.addCleanup(shutil.rmtree,self.root)
        self.stage = self.root/'staging'
        self.target = self.root/'target'
        self.stage.mkdir(); self.target.mkdir()
        self.baseline = {'Docs/base.txt': digest('original')}
        self.put(self.target,'Docs/base.txt','original')
        self.put(self.stage,'Docs/base.txt','original')
        self.rows = []
        self.extra = []

    def put(self, project, path, text):
        f = project/path; f.parent.mkdir(parents=True,exist_ok=True); f.write_text(text,encoding='utf-8')

    def entry(self, path, text, target_text=None, witness=True):
        self.put(self.stage,path,text)
        row = {'path':path,'sha256':digest(text)}
        if witness: row['targetSha256'] = digest(target_text) if target_text is not None else None
        self.rows.append(row)

    def plan(self, meta='none'):
        bp=self.root/'baseline.json'; bp.write_text(json.dumps(self.baseline),encoding='utf-8')
        mp=self.root/'manifest.json'; mp.write_text(json.dumps({'files':self.rows}),encoding='utf-8')
        sp=self.root/'supplemental.json'; sp.write_text(json.dumps(self.extra),encoding='utf-8')
        return make_plan(self.stage,self.target,bp,mp,sp,meta,expected_count=len(self.baseline),
                         expected_sha=hashlib.sha256(bp.read_bytes()).hexdigest())

    def codes(self, report):
        return {c['code'] for c in report['conflicts']}

    def test_exact_new_replace_skip_and_no_mutation(self):
        self.entry('Docs/base.txt','replacement','original')
        self.entry('Docs/new.txt','new')
        self.put(self.target,'Docs/same.txt','same')
        self.entry('Docs/same.txt','same','same')
        result=self.plan()
        self.assertEqual(result['status'],'ready_for_copy_review')
        self.assertEqual((result['summary']['newFiles'],result['summary']['replaceFiles'],result['summary']['sameBytesSkipped']),(1,1,1))
        self.assertEqual((self.target/'Docs/base.txt').read_text(),'original')
        self.assertFalse((self.target/'Docs/new.txt').exists())

    def test_unbaselined_existing_target_never_authorized_by_matching_witness(self):
        self.put(self.target,'Docs/untracked.txt','user data')
        self.entry('Docs/untracked.txt','replacement','user data')
        self.assertIn('existing_target_without_baseline',self.codes(self.plan()))

    def test_changed_baseline_target_conflicts(self):
        self.put(self.target,'Docs/base.txt','user edit')
        self.entry('Docs/base.txt','replacement','user edit')
        self.assertIn('target_changed_since_baseline',self.codes(self.plan()))

    def test_deleted_baselined_target_is_not_restored(self):
        (self.target/'Docs/base.txt').unlink()
        self.entry('Docs/base.txt','original')
        self.assertIn('baselined_target_deleted_do_not_restore',self.codes(self.plan()))

    def test_stale_manifest_source_and_target_refuse(self):
        self.entry('Docs/base.txt','replacement','wrong old hash')
        self.assertIn('target_changed_since_dependency_manifest',self.codes(self.plan()))
        self.put(self.stage,'Docs/base.txt','changed after manifest')
        self.assertIn('staging_changed_since_dependency_manifest',self.codes(self.plan()))

    def test_protected_raw_tree_and_folder_meta_refuse(self):
        self.rows=[{'path':path,'sha256':digest('not read'),'targetSha256':None} for path in
                   ['Assets/blend/anything.blend','Assets/BLEND/something.png','Assets/blend.meta']]
        result=self.plan()
        self.assertEqual(result['summary']['planFiles'],0)
        self.assertEqual([c['code'] for c in result['conflicts']],['protected_original_blend_tree']*3)

    def test_path_traversal_drive_ads_glob_reserved_refuse(self):
        for value in ['../outside','C:/outside','Assets/a:stream','Assets/*.fbx','Assets/NUL.txt','Assets/trailing.','/root/file']:
            with self.subTest(value=value),self.assertRaises(PlanError):normalize(value)

    def test_supplemental_overlap_cannot_discard_manifest_hash(self):
        self.entry('Docs/base.txt','replacement','original')
        self.extra=['Docs/base.txt'];self.put(self.stage,'Docs/base.txt','post export change')
        self.assertIn('staging_changed_since_dependency_manifest',self.codes(self.plan()))

    def test_unselected_baseline_change_is_reported_not_overwritten(self):
        self.put(self.target,'Docs/base.txt','user edit')
        self.entry('Docs/new.txt','new')
        result=self.plan()
        self.assertEqual(result['status'],'ready_for_copy_review')
        self.assertEqual(result['baselineTargetAudit'][0]['selected'],False)
        self.assertNotIn('Docs/base.txt',[x['path'] for x in result['plannedFiles']])

    def test_same_bytes_after_prior_delivery_needs_no_overwrite(self):
        self.put(self.target,'Docs/base.txt','already delivered')
        self.entry('Docs/base.txt','already delivered','original')
        result=self.plan()
        self.assertEqual(result['summary']['planFiles'],0)
        self.assertEqual(result['summary']['sameBytesSkipped'],1)

    def test_missing_meta_and_undeclared_folder_meta_refuse(self):
        self.entry('Assets/Foo/A.asset','asset')
        self.put(self.stage,'Assets/Foo.meta','guid: '+'a'*32+'\n')
        result=self.plan('selected')
        self.assertIn('missing_source_meta',self.codes(result))
        self.assertIn('required_meta_not_explicitly_selected',self.codes(result))

    def test_selected_and_target_guid_collisions_refuse(self):
        self.entry('Assets/Foo/A.asset','asset')
        self.entry('Assets/Foo/A.asset.meta','guid: '+'b'*32+'\n')
        self.entry('Assets/Foo.meta','guid: '+'a'*32+'\n')
        self.put(self.target,'Assets/Other.meta','guid: '+'b'*32+'\n')
        self.assertIn('selected_guid_collides_with_untouched_target',self.codes(self.plan('target-assets')))
        self.entry('Assets/Foo/B.asset','asset b')
        self.entry('Assets/Foo/B.asset.meta','guid: '+'b'*32+'\n')
        self.assertIn('duplicate_selected_guid',self.codes(self.plan('selected')))

    def test_parent_file_cannot_become_new_directory(self):
        self.put(self.target,'Docs/blocked','user file')
        self.entry('Docs/blocked/file.txt','new')
        with self.assertRaises(PlanError):self.plan()


if __name__ == '__main__':
    unittest.main(verbosity=2)
