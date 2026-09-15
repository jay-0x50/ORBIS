"""Read-only Stella Grip02 matrices; independently measured source, no Polaris data."""
from pathlib import Path
import argparse,hashlib,json,sys
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from rig_heroes import reset_pose
from review_hero_grip import rows
ROOT=Path(__file__).resolve().parents[2]/'TestResults/CharacterPipeline/RigReview'
parser=argparse.ArgumentParser();parser.add_argument('--run',choices=['StellaGrip02','StellaGrip04'],default='StellaGrip02');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
folder=ROOT/args.run;source=folder/'Stella_grip_refined.blend';before=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');weapon=bpy.data.objects['Wayfarer_EXISTING_MESH_grip_review']
pose={b.name:(rig.matrix_world@b.matrix).copy() for b in rig.pose.bones};blade=weapon.matrix_world.copy()
reset_pose(rig);bpy.context.view_layer.update();bones=[]
for b in rig.pose.bones:
    rest=rig.matrix_world@b.matrix
    bones.append({'name':b.name,'parent':b.parent.name if b.parent else '', 'restWorld':rows(rest),'poseWorld':rows(pose[b.name]),
        'restFlat':[float(v) for row in rest for v in row],'poseFlat':[float(v) for row in pose[b.name] for v in row],
        'applyFinger':b.name.startswith(('RightThumb','RightIndex','RightMiddle','RightRing','RightLittle'))})
assert len(bones)==66 and sum(b['applyFinger'] for b in bones)==15
assert before==hashlib.sha256(source.read_bytes()).hexdigest()
(folder/'PoseTransferSource.json').write_text(json.dumps({'character':'Stella','source':str(source),'sourceSha256Unchanged':before,'bones':bones,
    'weaponWorld':rows(blade),'weaponFlat':[float(v) for row in blade for v in row],
    'weaponPoints':[{'x':float(v.co.x),'y':float(v.co.y),'z':float(v.co.z)} for v in weapon.data.vertices],
    'status':'Temporary '+args.run+' pose/socket candidate. Consult its actual three-angle review and residual contact diagnostics. Fit actual imported rest parity/scale and each bone world delta; no raw Euler/quaternion/socket copy to Unity and no final grip acceptance.'},indent=2),encoding='utf-8')
print('STELLA_GRIP_POSE_SOURCE_READY',flush=True)
