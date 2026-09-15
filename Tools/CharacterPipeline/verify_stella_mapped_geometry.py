"""Read-only exact geometry/skin/bone check across the material-only12 bake."""
from pathlib import Path
import hashlib,json
import bpy
import numpy as np
STAGE=Path(__file__).resolve().parents[2]
ROOT=STAGE/'TestResults/CharacterPipeline/RigReview'
sources=[ROOT/('StellaLegIntegrated'+v+'/Stella_leg_integrated_trial.blend') for v in ['11','12']]
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def digest(value):return hashlib.sha256(value).hexdigest()
def field(seq,prop,shape,dtype):
    a=np.empty(shape,dtype);seq.foreach_get(prop,a.ravel());return digest(a.tobytes())
before=[sha(p) for p in sources];fingerprints=[]
for path in sources:
    bpy.ops.wm.open_mainfile(filepath=str(path),load_ui=False,use_scripts=False)
    obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');mesh=obj.data;rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    bones=[(b.name,b.parent.name if b.parent else '',[float(v) for row in b.matrix_local for v in row]) for b in rig.data.bones]
    weights=[[(g.group,g.weight) for g in v.groups] for v in mesh.vertices]
    fingerprints.append({'coordinates':field(mesh.vertices,'co',(len(mesh.vertices),3),np.float32),
        'loop_vertex_indices':field(mesh.loops,'vertex_index',(len(mesh.loops),),np.int32),
        'uv':field(mesh.uv_layers.active.data,'uv',(len(mesh.loops),2),np.float32),
        'corner_normals':field(mesh.corner_normals,'vector',(len(mesh.loops),3),np.float32),
        'material_indices':field(mesh.polygons,'material_index',(len(mesh.polygons),),np.int32),
        'skin':digest(json.dumps(weights).encode()),'bone_local_matrices':digest(json.dumps(bones).encode()),
        'vertex_group_names':digest(json.dumps([g.name for g in obj.vertex_groups]).encode())})
assert fingerprints[0]==fingerprints[1], 'Material transfer modified geometry/skin/bones'
assert before==[sha(p) for p in sources]
(sources[1].parent/'GeometryUnchanged.json').write_text(json.dumps({'source_paths':[str(p) for p in sources],'source_sha256_unchanged':before,'exact_all_fingerprints_equal':True,'fingerprints':fingerprints[0]},indent=2),encoding='utf-8')
print('STELLA_MAPPED_GEOMETRY_EXACTLY_PRESERVED',flush=True)
