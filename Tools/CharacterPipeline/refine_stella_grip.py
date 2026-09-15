"""Minimal pose/socket candidate measured on Stella Grip01; immutable source data.

Search uses the existing Stella pose as zero, not Polaris angles or socket.
Small bounded deltas are allowed only on the fifteen right finger rotations and
the sword translation. Actual triangle samples supplement vertex penetration.
"""
from pathlib import Path
import argparse,hashlib,json,sys
import bpy
import numpy as np
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from review_hero_grip import hand_mask,posed_world,contact_metric,rows
from rig_heroes import rotate_world,integrity
STAGE=Path(__file__).resolve().parents[2]
SOURCE=STAGE/'TestResults/CharacterPipeline/RigReview/StellaGrip01/Stella_grip_review.blend'
RECORD=SOURCE.parent/'Stella_grip_report.json'
parser=argparse.ArgumentParser();parser.add_argument('--run',default='StellaGrip02');parser.add_argument('--opposition',action='store_true');parser.add_argument('--clearance',action='store_true');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
assert args.run.isalnum() and args.run.startswith('StellaGrip')
OUTPUT=SOURCE.parent.parent/args.run
if OUTPUT.exists():raise RuntimeError('Preserve prior grip candidate')
OUTPUT.mkdir()
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();before=sha(SOURCE)
record=json.loads(RECORD.read_text(encoding='utf-8'))
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
weapon=bpy.data.objects['Wayfarer_EXISTING_MESH_grip_review'];body=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o!=weapon)
body_before=integrity(body);weapon_before=integrity(weapon)
bone_before={b.name:(b.parent.name if b.parent else '',rows(b.matrix_local)) for b in rig.data.bones}
group_names=[g.name for g in body.vertex_groups]
skin_before=[[(g.group,g.weight) for g in v.groups] for v in body.data.vertices]
original_pose={b.name:b.rotation_quaternion.copy() for b in rig.pose.bones}
base_socket=weapon.matrix_world.copy();inverse=np.asarray(base_socket.inverted())
fingers=['Thumb','Index','Middle','Ring','Little'];finger_names=[f'Right{f}{s}' for f in fingers for s in ['Proximal','Intermediate','Distal']]
mask=hand_mask(body);used=np.flatnonzero(mask);remap=np.full(len(mask),-1,np.int32);remap[used]=np.arange(len(used))
mesh=body.data;mesh.calc_loop_triangles();tri=np.empty((len(mesh.loop_triangles),3),np.int32);mesh.loop_triangles.foreach_get('vertices',tri.ravel());handtri=remap[tri[mask[tri].all(1)]]
co=np.asarray([mesh.vertices[i].co[:] for i in used],float);co4=np.c_[co,np.ones(len(co))]
weights=np.zeros((len(co),len(group_names)))
for j,i in enumerate(used):
    for g in mesh.vertices[i].groups:weights[j,g.group]=g.weight
active=np.flatnonzero(weights.sum(0)>0);wn=weights[:,active];names=[group_names[i] for i in active]
categories=np.array([next((i for i,f in enumerate(fingers) if group_names[int(np.argmax(w))].startswith('Right'+f)),5) for w in weights])
bind={n:rig.data.bones[n].matrix_local.inverted()@rig.matrix_world.inverted()@body.matrix_world for n in names}
modifiers=[(m,m.show_viewport) for m in body.modifiers if m.type=='ARMATURE']
assert all(not m.use_deform_preserve_volume for m,_ in modifiers),'Fast LBS comparison does not replace dual-quaternion skin'
for m,_ in modifiers:m.show_viewport=False
def apply(delta):
    for n,q in original_pose.items():rig.pose.bones[n].rotation_quaternion=q
    for n,d in zip(finger_names,delta[:15]):rotate_world(rig,n,(0,1,0),float(d))
    if len(delta)>15:
        # Measured hand's dorsal normal is +Z. Opposition in this plane and
        # a small thumb tuck are distinct from curling every finger about Y.
        rotate_world(rig,'RightThumbProximal',(0,0,1),float(delta[15]))
        rotate_world(rig,'RightThumbProximal',(1,0,0),float(delta[16]))
    bpy.context.view_layer.update()
    transforms=np.asarray([rig.matrix_world@rig.pose.bones[n].matrix@bind[n] for n in names])
    p=np.einsum('nk,kij,nj->ni',wn,transforms,co4)[:,:3]
    return p@inverse[:3,:3].T+inverse[:3,3]
def sample_triangles(p):
    face=p[handtri]
    return np.concatenate([p,face.mean(1),(face[:,0]+face[:,1])*.5,(face[:,1]+face[:,2])*.5,(face[:,2]+face[:,0])*.5])
def distances(p,offset):
    p=p-offset;z=p[:,2];rx=np.interp(z,[-.079,-.068,-.024,.026,.063],[.014,.013,.011,.012,.014]);ry=np.interp(z,[-.079,-.068,-.024,.026,.063],[.011,.010,.009,.0095,.011])
    gap=(np.sqrt((p[:,0]/rx)**2+(p[:,1]/ry)**2)-1)*np.minimum(rx,ry)
    axial=np.maximum(np.maximum(-.068-z,z-.063),0)
    return gap,axial
def score(p,offset,delta):
    dense=sample_triangles(p);gap,axial=distances(dense,offset)
    inside=np.where(axial<1e-7,np.maximum((.0003 if args.clearance else -.0005)-gap,0)*1000,0)
    vg,va=distances(p,offset);d=np.sqrt(vg**2+va**2)*1000;contact=0
    for c in range(6):
        values=d[categories==c]
        if len(values):contact+=float(np.mean(np.sort(values)[:min(3,len(values))]**2))
    # Millimetre penetration takes priority. Regularizers prefer the least
    # possible change to this existing Stella pose and palm-local socket.
    return (150 if args.clearance else 60)*float(inside.max()**2)+(20 if args.clearance else 8)*float(np.sum(inside**2))+contact+.10*float(np.sum(delta**2))+.30*float(np.sum((offset*1000)**2))
def diagnostic(p,offset):
    dense=sample_triangles(p);g,a=distances(dense,offset);vg,va=distances(p,offset)
    return {'handVertices':len(p),'actualHandTriangles':len(handtri),'denseTriangleAndVertexSamples':len(dense),
        'verticesPenetratingOver2mm':int(((vg<-.002)&(va<1e-7)).sum()),'denseSamplesPenetratingOver2mm':int(((g<-.002)&(a<1e-7)).sum()),
        'deepestAnalyticSamplePenetration':float(max(0,-g[a<1e-7].min())),
        'penetratingVertexCategories':{fingers[c] if c<5 else 'Palm':int(((categories==c)&(vg<-.002)&(va<1e-7)).sum()) for c in range(6)}}
delta=np.zeros(17 if args.opposition else 15);offset=np.zeros(3)
if args.opposition or args.clearance:
    prior=json.loads((SOURCE.parent.parent/'StellaGrip02/GripRefinement.json').read_text())['trace'][-1]
    delta[:15]=prior['fingerWorldYCurlDeltasDegrees'];offset=np.asarray(prior['swordLocalOffset'])
p=apply(delta);initial_diagnostic=diagnostic(p,offset);initial=score(p,offset,delta);best=initial;trace=[]
# First move the grip only, to avoid unnecessary finger changes. Then small
# anatomical curl deltas can close contact around the measured Stella hand.
for iteration in range(14):
    if iteration>=4 or args.opposition or args.clearance:
        step=5 if iteration<8 else 2 if iteration<11 else 1
        for joint in range(len(delta)):
            chosen=delta.copy()
            for sign in [-1,1]:
                trial=delta.copy();trial[joint]+=sign*step
                limit=25 if joint>=15 else 20 if joint%3==1 else 15
                if abs(trial[joint])>limit:continue
                pts=apply(trial);cost=score(pts,offset,trial)
                if cost<best:best=cost;chosen=trial
            delta=chosen
    p=apply(delta)
    for axis in range(3):
        chosen=offset.copy()
        for sign in [-1,1]:
            trial=offset.copy();trial[axis]+=sign*(.002 if iteration<4 else .001 if iteration<10 else .0005)
            if abs(trial[axis])>.010:continue
            cost=score(p,trial,delta)
            if cost<best:best=cost;chosen=trial
        offset=chosen
    trace.append({'iteration':iteration,'score':best,'fingerWorldYCurlDeltasDegrees':delta.tolist(),'swordLocalOffset':offset.tolist()})
    print('STELLA_GRIP_SEARCH',iteration,best,flush=True)
p=apply(delta);weapon.matrix_world=base_socket@Matrix.Translation(Vector(offset))
for m,show in modifiers:m.show_viewport=show
bpy.context.view_layer.update()
actual=posed_world(body)[mask];fast_world=(p-inverse[:3,3])@np.linalg.inv(inverse[:3,:3]).T
fast_error=float(np.linalg.norm(actual-fast_world,axis=1).max());assert fast_error<.00001,'Hand LBS differs from actual Blender evaluated skin'
assert integrity(body)==body_before
assert {b.name:(b.parent.name if b.parent else '',rows(b.matrix_local)) for b in rig.data.bones}==bone_before
assert skin_before==[[(g.group,g.weight) for g in v.groups] for v in body.data.vertices]
after_weapon=integrity(weapon)
assert all(after_weapon[k]==weapon_before[k] for k in weapon_before if k!='world_matrix')
hand_world=rig.matrix_world@rig.pose.bones['RightHand'].matrix
bpy.context.preferences.filepaths.save_version=0;bpy.context.preferences.filepaths.file_preview_type='NONE'
output=OUTPUT/'Stella_grip_refined.blend';bpy.ops.wm.save_as_mainfile(filepath=str(output),relative_remap=True)
assert sha(SOURCE)==before
report={'sourceScene':str(SOURCE),'sourceSha256Unchanged':before,'output':str(output),'outputSha256':sha(output),
    'initialScore':initial,'finalScore':best,'trace':trace,'initialDiagnostic':initial_diagnostic,'finalDiagnostic':diagnostic(p,offset),
    'actualBlenderSkinVsFastLbsMaximum':fast_error,'geometryUvMaterialsWeightsRestBonesUnchanged':True,
    'weapon_world':rows(weapon.matrix_world),'weapon_source_local_to_right_hand_matrix':rows(hand_world.inverted()@weapon.matrix_world),
    'finger_rotation_quaternions_wxyz':{n:list(rig.pose.bones[n].rotation_quaternion) for n in finger_names},'contact':contact_metric(body,weapon,mask),
    'status':'Temporary Stella-only pose/socket candidate. Actual closeups, exact weapon-surface diagnostics and Unity roundtrip required; no finished grip approval.'}
(OUTPUT/'GripRefinement.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('STELLA_GRIP_CANDIDATE_COMPLETE',json.dumps(report['finalDiagnostic']),flush=True)
