"""Render the already-recorded grip candidate without changing its source scene."""
from pathlib import Path
import hashlib
import json
import sys
import bpy
from mathutils import Matrix,Vector,Quaternion

sys.path.insert(0,str(Path(__file__).resolve().parent))
from review_hero_grip import make_studio,render_views,reset_pose
root=Path(__file__).resolve().parents[2]
folder=root/'TestResults/CharacterPipeline/RigReview/PolarisGrip01'
report=json.loads((folder/'Polaris_grip_report.json').read_text())
source=Path(report['temporary_scene']); before=hashlib.sha256(source.read_bytes()).hexdigest()
output=folder/'RenderReview'
assert not output.exists(), 'Preserve prior actual render evidence'
output.mkdir()
bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
weapon=bpy.data.objects['Wayfarer_EXISTING_MESH_grip_review']
matrix=Matrix(report['candidate']['weapon_world'])
target=matrix.translation
camera=make_studio(target,report['source_height'])
reset_pose(rig)
weapon.matrix_world=Matrix(report['existing_zero_socket']['weapon_world'])
bpy.context.view_layer.update()
paths=render_views(output,'Polaris','ExistingZeroSocket',target,report['source_height'],camera)
for name,rotation in report['candidate']['finger_rotation_quaternions_wxyz'].items():
    rig.pose.bones[name].rotation_mode='QUATERNION'
    rig.pose.bones[name].rotation_quaternion=Quaternion(rotation)
weapon.matrix_world=matrix
bpy.context.view_layer.update()
paths+=render_views(output,'Polaris','ProposedGrip',target,report['source_height'],camera)
assert hashlib.sha256(source.read_bytes()).hexdigest()==before
(output/'RenderProvenance.json').write_text(json.dumps({'sourceScene':str(source),'sourceSha256':before,
    'poses':'Exact existing/candidate matrices and finger rotations from Polaris_grip_report.json',
    'actualBlenderRenders':paths,'status':'Visual review pending; no runtime socket or animation changed.'},indent=2),encoding='utf-8')
