"""Exercise copying only in synthetic Tools fixtures, never D:/Project/ORBIS."""
import hashlib
import json
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

import apply_delivery as copier
import prepare_delivery as planner


def digest(text):return hashlib.sha256(text.encode()).hexdigest()


class CopierTests(unittest.TestCase):
    def setUp(self):
        scope=Path(__file__).resolve().parent/'DeliveryCopierTests'
        scope.mkdir(exist_ok=True)
        self.root=(scope/('synthetic_'+uuid.uuid4().hex)).resolve()
        self.root.mkdir()
        if not planner.contained(self.root,scope.resolve()) or self.root==scope.resolve():
            raise RuntimeError('Unsafe fixture cleanup target')
        self.addCleanup(shutil.rmtree,self.root)
        self.stage=self.root/'staging';self.target=self.root/'target'
        self.stage.mkdir();self.target.mkdir()
        self.put(self.stage,'Docs/base.txt','replacement')
        self.put(self.target,'Docs/base.txt','original')
        self.put(self.stage,'Docs/new.txt','new file')
        self.baseline=self.stage/'Tools/CharacterPipeline/ProjectBaseline.json'
        self.baseline.parent.mkdir(parents=True)
        self.baseline.write_text(json.dumps({'Docs/base.txt':digest('original')}))
        self.baseline_sha=hashlib.sha256(self.baseline.read_bytes()).hexdigest()
        self.manifest=self.stage/'Tools/manifest.json'
        self.manifest.write_text(json.dumps({'stagingRoot':str(self.stage),'targetRoot':str(self.target),
             'files':[{'path':'Docs/base.txt','sha256':digest('replacement'),'targetSha256':digest('original')},
                      {'path':'Docs/new.txt','sha256':digest('new file'),'targetSha256':None}]}))
        self.supp=self.stage/'Tools/supplemental.json';self.supp.write_text('[]')
        self.plan=self.stage/'Tools/plan.json'
        self.data=planner.make_plan(self.stage,self.target,self.baseline,self.manifest,self.supp,'none',1,self.baseline_sha)
        self.save_plan()
        # Production destination and baseline pins have no CLI override. Tests
        # patch module policy only inside this verified synthetic fixture scope.
        for name,value in [('PROJECT_ROOT',self.stage),('ALLOWED_TARGET',self.target),
                           ('PINNED_BASELINE_COUNT',1),('PINNED_BASELINE_SHA',self.baseline_sha)]:
            handle=patch.object(copier,name,value);handle.start();self.addCleanup(handle.stop)

    def put(self,root,path,text):
        f=root/path;f.parent.mkdir(parents=True,exist_ok=True);f.write_text(text,encoding='utf-8')

    def save_plan(self):self.plan.write_text(json.dumps(self.data),encoding='utf-8')

    def run_delivery(self,apply=False):
        out=self.stage/'Tools/CharacterPipeline/DeliveryRuns'/('run_'+uuid.uuid4().hex)
        return copier.deliver(self.plan,out,apply)

    def assert_untouched(self):
        self.assertEqual((self.target/'Docs/base.txt').read_text(),'original')
        self.assertFalse((self.target/'Docs/new.txt').exists())

    def test_default_dry_run_only_writes_staging_report(self):
        report=self.run_delivery()
        self.assertEqual(report['status'],'dry_run_verified')
        self.assertFalse(report['targetMutationStarted'])
        self.assertEqual(report['backups'],[])
        self.assert_untouched()

    def test_synthetic_apply_preserves_backup_and_verifies_all_files(self):
        report=self.run_delivery(True)
        self.assertEqual(report['status'],'applied_verified')
        self.assertEqual(report['copiedFileCount'],2)
        self.assertEqual((self.target/'Docs/base.txt').read_text(),'replacement')
        self.assertEqual((self.target/'Docs/new.txt').read_text(),'new file')
        self.assertEqual(Path(report['backups'][0]['backupPath']).read_text(),'original')
        self.assertTrue(all(row['verified'] for row in report['copied']))
        self.assertTrue(all(not Path(row['path']).exists() for row in report['temporaryFiles']))

    def test_target_change_before_preflight_blocks_every_write(self):
        self.put(self.target,'Docs/base.txt','user edit')
        report=self.run_delivery(True)
        self.assertEqual(report['status'],'failed_preflight_or_backup')
        self.assertFalse(report['targetMutationStarted'])
        self.assertEqual((self.target/'Docs/base.txt').read_text(),'user edit')
        self.assertFalse((self.target/'Docs/new.txt').exists())

    def test_source_change_before_preflight_blocks(self):
        self.put(self.stage,'Docs/new.txt','changed source')
        report=self.run_delivery(True)
        self.assertEqual(report['status'],'failed_preflight_or_backup')
        self.assert_untouched()

    def test_tampered_plan_canonical_hash_is_rejected(self):
        self.data['planSha256']='0'*64;self.save_plan()
        report=self.run_delivery(True)
        self.assertIn('SHA mismatch',report['error']['message'])
        self.assert_untouched()

    def test_rehashed_unauthorized_plan_row_still_rejected(self):
        self.data['plannedFiles'].append({'path':'Docs/extra.txt','stagingSha256':digest('extra'),'targetSha256':None,
                                         'baselineSha256':None,'bytes':5,'disposition':'plan_new_file'})
        self.data['planSha256']=copier.sha_rows(self.data['plannedFiles']);self.save_plan()
        report=self.run_delivery(True)
        self.assertEqual(report['status'],'failed_preflight_or_backup')
        self.assertIn('differs from reviewed plan',report['error']['message'])
        self.assert_untouched()

    def test_changed_input_manifest_hash_is_rejected(self):
        self.supp.write_text('["Docs/new.txt"]')
        report=self.run_delivery(True)
        self.assertEqual(report['status'],'failed_preflight_or_backup')
        self.assertIn('supplemental changed',report['error']['message'])
        self.assert_untouched()

    def test_wrong_target_and_baseline_pins_rejected(self):
        self.data['targetRoot']=str(self.root/'other');self.save_plan()
        report=self.run_delivery(True)
        self.assertEqual(report['status'],'failed_preflight_or_backup')
        self.data['targetRoot']=str(self.target);self.data['inputs']['baselineCount']=1793;self.save_plan()
        report=self.run_delivery(True)
        self.assertIn('Pinned baseline',report['error']['message'])
        self.assert_untouched()

    def test_change_after_backup_is_not_overwritten_or_rolled_back(self):
        original=copier.stream_verified_copy
        def race(source,destination,expected):
            result=original(source,destination,expected)
            if '.orbis-delivery-' in destination.name:
                self.put(self.target,'Docs/base.txt','concurrent user edit')
            return result
        with patch.object(copier,'stream_verified_copy',side_effect=race):report=self.run_delivery(True)
        self.assertEqual(report['status'],'failed_partial_no_rollback')
        self.assertEqual((self.target/'Docs/base.txt').read_text(),'concurrent user edit')
        self.assertFalse((self.target/'Docs/new.txt').exists())
        self.assertEqual(Path(report['backups'][0]['backupPath']).read_text(),'original')

    def test_failure_after_one_replace_keeps_completed_file_and_no_rollback(self):
        original=copier._replace_target
        def fail_second(temporary,target):
            if target.name=='new.txt':raise OSError('injected second-file failure')
            original(temporary,target)
        with patch.object(copier,'_replace_target',side_effect=fail_second):report=self.run_delivery(True)
        self.assertEqual(report['status'],'failed_partial_no_rollback')
        self.assertEqual(len(report['copied']),1)
        self.assertEqual((self.target/'Docs/base.txt').read_text(),'replacement')
        self.assertFalse((self.target/'Docs/new.txt').exists())
        self.assertTrue(Path(report['temporaryFiles'][-1]['path']).exists())
        self.assertEqual(Path(report['backups'][0]['backupPath']).read_text(),'original')

    def test_fresh_report_directory_required(self):
        out=self.stage/'Tools/CharacterPipeline/DeliveryRuns/existing';out.mkdir(parents=True)
        with self.assertRaises(copier.DeliveryError):copier.deliver(self.plan,out,True)
        self.assert_untouched()

    def test_missing_metadata_witness_cannot_be_silently_ignored(self):
        self.data['metadata']['witnesses']=[{'path':'Assets/Fake.meta','guid':'a'*32,
                'stagingSha256':'0'*64,'targetSha256':None}];self.save_plan()
        report=self.run_delivery(True)
        self.assertEqual(report['status'],'failed_preflight_or_backup')
        self.assertIn('Metadata',report['error']['message'])
        self.assert_untouched()

    def test_journal_retries_only_selected_windows_errors_then_succeeds(self):
        original=copier.os.replace
        for code in (5,32,33):
            with self.subTest(winerror=code):
                folder=self.stage/'Tools/CharacterPipeline/DeliveryRuns'/('journal_'+str(code))
                folder.mkdir(parents=True)
                (folder/'Run.json').write_text('{"status":"previous"}')
                error=PermissionError(13,'injected Windows journal lock');error.winerror=code
                calls=[]
                def transient(source,target):
                    calls.append((source,target))
                    if len(calls)<3:raise error
                    original(source,target)
                with patch.object(copier.os,'replace',side_effect=transient),patch.object(copier.time,'sleep') as sleep:
                    copier.write_journal(folder,{'status':'updated'})
                self.assertEqual(len(calls),3)
                self.assertEqual(len({source for source,target in calls}),1)
                self.assertTrue(all(target==folder/'Run.json' for source,target in calls))
                self.assertEqual([row.args[0] for row in sleep.call_args_list],[.1,.2])
                self.assertEqual(json.loads((folder/'Run.json').read_text())['status'],'updated')
                self.assertEqual(list(folder.glob('journal-*.tmp')),[])
        self.assert_untouched()

    def test_journal_retry_exhaustion_retains_old_report_and_owned_temp(self):
        folder=self.stage/'Tools/CharacterPipeline/DeliveryRuns/journal_exhaustion'
        folder.mkdir(parents=True)
        before='{"status":"previous"}';(folder/'Run.json').write_text(before)
        error=PermissionError(13,'injected Windows journal lock');error.winerror=5
        with patch.object(copier.os,'replace',side_effect=error) as replace,patch.object(copier.time,'sleep') as sleep:
            with self.assertRaises(PermissionError):copier.write_journal(folder,{'status':'updated'})
        self.assertEqual(replace.call_count,5)
        self.assertEqual(sleep.call_count,4)
        self.assertAlmostEqual(sum(row.args[0] for row in sleep.call_args_list),1.0)
        self.assertEqual((folder/'Run.json').read_text(),before)
        temps=list(folder.glob('journal-*.tmp'));self.assertEqual(len(temps),1)
        self.assertEqual(json.loads(temps[0].read_text())['status'],'updated')
        self.assert_untouched()

    def test_journal_unrelated_errors_are_never_retried(self):
        error=PermissionError(13,'unlisted Windows error');error.winerror=87
        for index,failure in enumerate((error,OSError('unrelated write error'))):
            with self.subTest(error=repr(failure)):
                folder=self.stage/'Tools/CharacterPipeline/DeliveryRuns'/('journal_other_'+str(index))
                folder.mkdir(parents=True)
                with patch.object(copier.os,'replace',side_effect=failure) as replace,patch.object(copier.time,'sleep') as sleep:
                    with self.assertRaises(type(failure)):copier.write_journal(folder,{'status':'updated'})
                self.assertEqual(replace.call_count,1);sleep.assert_not_called()
                self.assertEqual(len(list(folder.glob('journal-*.tmp'))),1)
        self.assert_untouched()


if __name__=='__main__':unittest.main(verbosity=2)
