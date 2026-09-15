"""Constrained pose/socket review candidate; preserves all geometry, UV, bones and weights."""
from pathlib import Path
import argparse,hashlib,json,sys
import numpy as np
import bpy
from mathutils import Matrix,Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from review_hero_grip import posed_world,hand_mask,contact_metric,reset_pose,rotate_world,rows

root=Path(__file__).resolve().parents[2]
previous=root/'TestResults/CharacterPipeline/RigReview/PolarisGrip01'
record=json.loads((previous/'Polaris_grip_report.json').read_text())
source=Path(record['temporary_scene'])
before=hashlib.sha256(source.read_bytes()).hexdigest()
parser=argparse.ArgumentParser()
parser.add_argument('--run',default='PolarisGrip02')
parser.add_argument('--natural',action='store_true')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
assert args.run.isalnum() and args.run.startswith('PolarisGrip')
output=root/'TestResults/CharacterPipeline/RigReview'/args.run
assert not output.exists(), 'Preserve prior candidate'
output.mkdir()
bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
body=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o.name!='Wayfarer_EXISTING_MESH_grip_review')
weapon=bpy.data.objects['Wayfarer_EXISTING_MESH_grip_review']
mask=hand_mask(body)
names={g.index:g.name for g in body.vertex_groups}
categories=[]
for vertex in body.data.vertices:
    if not mask[vertex.index]:continue
    group=names[max(vertex.groups,key=lambda g:g.weight).group]
    categories.append(next((i for i,f in enumerate(['Thumb','Index','Middle','Ring','Little']) if group.startswith('Right'+f)),5))
categories=np.array(categories)
matrix=Matrix(record['candidate']['weapon_world'])
base=np.array(matrix)
inverse=np.array(matrix.inverted())
base_angles=np.array([[55.,70.,40.]]*4+[[0.,10.,5.]])

def apply(angles):
    reset_pose(rig)
    for finger,values in zip(['Index','Middle','Ring','Little','Thumb'],angles):
        for suffix,angle in zip(['Proximal','Intermediate','Distal'],values):
            rotate_world(rig,'Right'+finger+suffix,(0,1,0),-float(angle))
    bpy.context.view_layer.update()
    points=posed_world(body)[mask]
    return points@inverse[:3,:3].T+inverse[:3,3]

def score(points,offset):
    # Offset is in immutable sword local coordinates. Local Y is normal to the palm;
    # local X crosses the grip and local Z runs along the handle.
    local=points-offset
    z=local[:,2]
    rx=np.interp(z,[-.079,-.068,-.024,.026,.063],[.014,.013,.011,.012,.014])
    ry=np.interp(z,[-.079,-.068,-.024,.026,.063],[.011,.010,.009,.0095,.011])
    radial=np.sqrt((local[:,0]/rx)**2+(local[:,1]/ry)**2)
    gap=(radial-1)*np.minimum(rx,ry)
    axial=np.maximum(np.maximum(-.068-z,z-.063),0)
    distance=np.sqrt(gap*gap+axial*axial)*1000
    inside=np.where(axial<1e-7,np.maximum(-gap-.0006,0)*1000,0)
    contact=0
    for category in range(6):
        values=distance[categories==category]
        if len(values):contact+=float(np.mean(np.sort(values)[:min(4,len(values))]**2))
    return 45*float(np.max(inside)**2)+10*float(np.sum(inside**2))+contact+.04*float(np.sum((offset*1000)**2))

# Small bounded coordinate search retains a normal power-grip pose and the recorded
# palm region. Numeric improvement still requires actual closeups and triangle QA.
angles=base_angles.copy(); offset=np.zeros(3)
points=apply(angles); initial=score(points,offset); best=initial
trace=[]
for iteration in range(12 if args.natural else 6):
    angle_step=10. if iteration<3 else 5. if iteration<7 else 2.
    for finger in range(5):
        for joint in range(3):
            chosen=angles.copy()
            for sign in (-1,1):
                trial=angles.copy()
                trial[finger,joint]+=sign*angle_step
                limits=(0,45) if finger==4 else ((25,85) if joint==0 else ((30,100) if joint==1 else (5,70)))
                if args.natural and finger<4:
                    limits=((30,85),(55,95),(15,55))[joint]
                if not limits[0]<=trial[finger,joint]<=limits[1]:continue
                sample=apply(trial); cost=score(sample,offset)
                if cost<best:best=cost;chosen=trial
            angles=chosen
    points=apply(angles)
    for axis in range(3):
        chosen=offset.copy()
        for sign in (-1,1):
            trial=offset.copy();trial[axis]+=sign*(.002 if iteration<3 else .001)
            if abs(trial[axis])>(.015 if args.natural and axis==2 else .010):continue
            cost=score(points,trial)
            if cost<best:best=cost;chosen=trial
        offset=chosen
    trace.append({'iteration':iteration,'score':best,'angles':angles.tolist(),'swordLocalOffset':offset.tolist()})
    print('GRIP_OPTIMIZATION',iteration,best,flush=True)

apply(angles)
weapon.matrix_world=matrix@Matrix.Translation(Vector(offset))
bpy.context.view_layer.update()
hand_world=rig.matrix_world@rig.pose.bones['RightHand'].matrix
bpy.context.preferences.filepaths.save_version=0
bpy.context.preferences.filepaths.file_preview_type='NONE'
scene_path=output/'Polaris_grip_refined.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(scene_path),relative_remap=True)
assert hashlib.sha256(source.read_bytes()).hexdigest()==before
(output/'GripRefinement.json').write_text(json.dumps({
    'sourceScene':str(source),'sourceSha256Unchanged':before,'naturalJointLimits':args.natural,'initialScore':initial,'finalScore':best,'trace':trace,
    'weapon_world':rows(weapon.matrix_world),'weapon_source_local_to_right_hand_matrix':rows(hand_world.inverted()@weapon.matrix_world),
    'finger_rotation_quaternions_wxyz':{b.name:list(b.rotation_quaternion) for b in rig.pose.bones if b.name.startswith(('RightThumb','RightIndex','RightMiddle','RightRing','RightLittle'))},
    'contact':contact_metric(body,weapon,mask),
    'status':'Numerical pose/socket candidate only. Actual closeups, triangle collision and Unity muscle/socket transfer not yet verified. No live model or animation changed.'
},indent=2),encoding='utf-8')
