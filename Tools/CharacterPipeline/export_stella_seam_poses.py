"""Read-only exact source poses for a calibrated Unity seam/skin comparison."""
from pathlib import Path
import argparse,hashlib,json,sys
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from rig_heroes import test_pose,reset_pose
STAGE=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser();parser.add_argument('--version',choices=['06','12'],default='06');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
SOURCE=STAGE/('TestResults/CharacterPipeline/RigReview/StellaLegIntegrated'+args.version+'/Stella_leg_integrated_trial.blend')
OUTPUT=STAGE/('Tools/CharacterPipeline/PendingImport/StellaLegIntegrated'+args.version+'/ReviewPoses.json')
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
before=sha(SOURCE);bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');reset_pose(rig)
def flat(m):return [float(v) for row in m for v in row]
rest=[{'name':b.name,'parent':b.parent.name if b.parent else '', 'matrix':flat(rig.matrix_world@b.matrix_local)} for b in rig.data.bones]
poses=[]
for name in ['Relaxed','WalkContact','JointStress']:
    test_pose(rig,name)
    poses.append({'name':name,'bones':[{'name':p.name,'parent':p.parent.name if p.parent else '', 'matrix':flat(rig.matrix_world@p.matrix)} for p in rig.pose.bones]})
assert sha(SOURCE)==before
OUTPUT.write_text(json.dumps({'source':str(SOURCE),'sourceSha256Unchanged':before,'rest':rest,'poses':poses},indent=2),encoding='utf-8')
print('STELLA_SOURCE_REVIEW_POSES_EXPORTED',flush=True)
