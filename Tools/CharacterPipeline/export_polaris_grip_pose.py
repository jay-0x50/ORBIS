"""Export observed source rest/pose matrices for a measured Unity basis conversion later."""
from pathlib import Path
import hashlib,json,sys
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from rig_heroes import reset_pose
from review_hero_grip import rows
root=Path(__file__).resolve().parents[2]
folder=root/'TestResults/CharacterPipeline/RigReview/PolarisGrip03'
source=folder/'Polaris_grip_refined.blend'
before=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
weapon=bpy.data.objects['Wayfarer_EXISTING_MESH_grip_review']
pose={b.name:(rig.matrix_world@b.matrix).copy() for b in rig.pose.bones}
blade=weapon.matrix_world.copy()
reset_pose(rig);bpy.context.view_layer.update()
bones=[]
for bone in rig.pose.bones:
    rest=rig.matrix_world@bone.matrix
    bones.append({'name':bone.name,'parent':bone.parent.name if bone.parent else '',
                  'restWorld':rows(rest),'poseWorld':rows(pose[bone.name]),
                  'restFlat':[float(rest[r][c]) for r in range(4) for c in range(4)],
                  'poseFlat':[float(pose[bone.name][r][c]) for r in range(4) for c in range(4)],
                  'applyFinger':bone.name.startswith(('RightThumb','RightIndex','RightMiddle','RightRing','RightLittle'))})
assert hashlib.sha256(source.read_bytes()).hexdigest()==before
(folder/'PoseTransferSource.json').write_text(json.dumps({'source':str(source),'sourceSha256Unchanged':before,
    'bones':bones,'weaponWorld':rows(blade),'weaponFlat':[float(blade[r][c]) for r in range(4) for c in range(4)],
    'weaponPoints':[{'x':float(v.co.x),'y':float(v.co.y),'z':float(v.co.z)} for v in weapon.data.vertices],
    'status':'Do not copy raw Blender Euler/quaternion values to Unity. Fit the global basis from matching rest bone points, conjugate each world rotation delta, then apply it to that Unity bone rest basis and verify posed bone points. The physical grip is still a review candidate.'
},indent=2),encoding='utf-8')
print('Grip source rest/pose basis recorded; Unity conversion pending.')
