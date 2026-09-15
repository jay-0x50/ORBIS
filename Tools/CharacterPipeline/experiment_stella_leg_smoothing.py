"""CPU-only bounded leg surface experiments; never rewrites the accepted rig.

This conservative first experiment keeps every index/UV/weight/bone and moves
only strongly leg-weighted vertices inside the central leg band. It may fail
to remove manifold microhandles; metrics decide whether retopology is needed.
"""
from pathlib import Path
import hashlib,json,sys
import bpy
import numpy as np

STAGE=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(Path(__file__).resolve().parent))
from rig_heroes import integrity
SOURCE=STAGE/'TestResults/CharacterPipeline/RigReview/Stella400kV3/Stella_rigged.blend'
OUTPUT=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegSmooth01'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def smoothstep(a,b,x):
    t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)

before=sha(SOURCE);bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');mesh=obj.data
raw=np.empty((len(mesh.vertices),3),np.float64);mesh.vertices.foreach_get('co',raw.ravel())
edges=np.empty((len(mesh.edges),2),np.int32);mesh.edges.foreach_get('vertices',edges.ravel())
mesh.calc_loop_triangles();tri=np.empty((len(mesh.loop_triangles),3),np.int32);mesh.loop_triangles.foreach_get('vertices',tri.ravel())
lo=raw.min(0);hi=raw.max(0);height=hi[2]-lo[2];z=(raw[:,2]-lo[2])/height
legids={g.index for g in obj.vertex_groups if g.name in ['LeftUpperLeg','RightUpperLeg','LeftLowerLeg','RightLowerLeg']}
weight=np.array([sum(g.weight for g in v.groups if g.group in legids) for v in mesh.vertices])
# Exclude ankle ornaments, boot tops and thigh straps at both transition ends.
factor=smoothstep(.235,.250,z)*(1-smoothstep(.397,.412,z))*(weight>.985)
mask=factor>0
protected=~mask;protectsha=hashlib.sha256(raw[protected].tobytes()).hexdigest()
orig=integrity(obj)
directed=np.vstack([edges,edges[:,::-1]])
count=np.bincount(directed[:,0],minlength=len(raw)).astype(float);count=np.maximum(count,1)

def laplace(co):
    total=np.column_stack([np.bincount(directed[:,0],weights=co[directed[:,1],i],minlength=len(raw)) for i in range(3)])
    return total/count[:,None]-co

edge_tri=np.sort(np.vstack([tri[:,[0,1]],tri[:,[1,2]],tri[:,[2,0]]]),axis=1)
ids=np.tile(np.arange(len(tri)),3);order=np.lexsort((edge_tri[:,1],edge_tri[:,0]));edge_tri=edge_tri[order];ids=ids[order]
same=np.all(edge_tri[1:]==edge_tri[:-1],axis=1);pairs=np.stack([ids[:-1][same],ids[1:][same]],axis=1)
patchtri=mask[tri].all(1);pairmask=patchtri[pairs].all(1)
def metrics(co):
    normal=np.cross(co[tri[:,1]]-co[tri[:,0]],co[tri[:,2]]-co[tri[:,0]])
    area=np.linalg.norm(normal,axis=1)*.5;normal/=np.maximum(2*area[:,None],1e-20)
    dot=np.einsum('ij,ij->i',normal[pairs[:,0]],normal[pairs[:,1]])
    shift=np.linalg.norm(co-raw,axis=1)
    return {'affected_vertices':int(mask.sum()),'patch_triangles':int(patchtri.sum()),
        'patch_adjacent_pairs':int(pairmask.sum()),'patch_over_90deg':int(np.count_nonzero(pairmask&(dot<0))),
        'patch_over_150deg':int(np.count_nonzero(pairmask&(dot<-.8660254))),
        'patch_triangles_area_under_1e10':int(np.count_nonzero(patchtri&(area<1e-10))),
        'shift_p99_source_units':float(np.quantile(shift[mask],.99)),
        'shift_max_source_units':float(shift.max()),'protected_vertices_exact':np.array_equal(raw[protected],co[protected])}

OUTPUT.mkdir(parents=True,exist_ok=True);reports={'Baseline':metrics(raw)}
bpy.context.preferences.filepaths.save_version=0;bpy.context.preferences.filepaths.file_preview_type='NONE'
for iterations in [3,10,30]:
    co=raw.copy()
    for i in range(iterations):
        co+=.48*factor[:,None]*laplace(co)
        co-=.50*factor[:,None]*laplace(co)
    reports[str(iterations)]=metrics(co)
    assert reports[str(iterations)]['protected_vertices_exact']
    mesh.vertices.foreach_set('co',co.astype(np.float32).ravel());mesh.update()
    result=integrity(obj)
    for key in orig:
        if key!='vertices_sha256':assert result[key]==orig[key],key
    path=OUTPUT/('Stella_leg_smooth_'+str(iterations)+'.blend')
    bpy.ops.wm.save_as_mainfile(filepath=str(path),relative_remap=True)
    reports[str(iterations)]['candidate']=str(path);reports[str(iterations)]['sha256']=sha(path)
    print('LEG_SMOOTH '+str(iterations)+' '+json.dumps(reports[str(iterations)]),flush=True)
assert sha(SOURCE)==before
(OUTPUT/'Experiment.json').write_text(json.dumps({'source':str(SOURCE),'source_sha256_unchanged':before,
    'method':'Bounded Taubin smoothing on existing coordinates only; unchanged topology/UV/weights/bones. This does not remove topological handles.',
    'protected_vertices_sha256':protectsha,'normalized_z_band':[.235,.250,.397,.412],
    'iterations':reports,'status':'CPU experiment only; no visual acceptance/export/production changes'},indent=2),encoding='utf-8')
