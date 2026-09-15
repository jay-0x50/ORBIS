"""Read-only source/clean/rig/FBX split-normal and micro-triangle diagnostics."""
from pathlib import Path
import gc, hashlib, json
import bpy
import numpy as np

STAGE=Path(__file__).resolve().parents[2]
OUTPUT=STAGE/'TestResults/CharacterPipeline/SurfaceDiagnostics/Stella01'
SOURCES={
    'Original':Path('D:/Project/ORBIS/Assets/blend/여주인공.blend'),
    'Clean400k':STAGE/'TestResults/CharacterPipeline/Processed/Stella400kDirect/Stella_repaired.blend',
    'RigV3':STAGE/'TestResults/CharacterPipeline/RigReview/Stella400kV3/Stella_rigged.blend',
    'GroundedFBX':STAGE/'TestResults/CharacterPipeline/RigReview/StellaV3GroundedExport/Stella_grounded_object_offset.fbx'}

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def unique_corners(mesh):
    count=len(mesh.loops)
    vertex=np.empty(count,np.int32);mesh.loops.foreach_get('vertex_index',vertex)
    uv=np.empty((count,2),np.float32);mesh.uv_layers.active.data.foreach_get('uv',uv.ravel())
    normals=np.empty((count,3),np.float32);mesh.corner_normals.foreach_get('vector',normals.ravel())
    def unique(fields):
        values=np.column_stack(fields).astype(np.float32)
        return int(len(np.unique(values.view(np.dtype((np.void,values.dtype.itemsize*values.shape[1]))))))
    return {'loops':count,'position_index_plus_uv_rounded_1e6':unique([vertex,np.round(uv,6)]),
        'position_index_plus_normal_rounded_1e5':unique([vertex,np.round(normals,5)]),
        'position_index_uv_normal_rounded':unique([vertex,np.round(uv,6),np.round(normals,5)]),
        'nonfinite_uv':int(np.count_nonzero(~np.isfinite(uv))),
        'nonfinite_corner_normal':int(np.count_nonzero(~np.isfinite(normals)))}

def audit(label,path):
    before=sha(path)
    if path.suffix=='.fbx':
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(path),use_custom_normals=True)
    else:bpy.ops.wm.open_mainfile(filepath=str(path),load_ui=False,use_scripts=False)
    obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');mesh=obj.data
    smooth=np.empty(len(mesh.polygons),bool);mesh.polygons.foreach_get('use_smooth',smooth)
    sharp=np.empty(len(mesh.edges),bool);mesh.edges.foreach_get('use_edge_sharp',sharp)
    co=np.empty((len(mesh.vertices),3),np.float32);mesh.vertices.foreach_get('co',co.ravel())
    m=np.asarray(obj.matrix_world);co=co@m[:3,:3].T+m[:3,3]
    mesh.calc_loop_triangles();tri=np.empty((len(mesh.loop_triangles),3),np.int32)
    mesh.loop_triangles.foreach_get('vertices',tri.ravel())
    a=co[tri[:,1]]-co[tri[:,0]];b=co[tri[:,2]]-co[tri[:,0]]
    cross=np.cross(a,b);area=np.linalg.norm(cross,axis=1)*.5
    lengths=np.stack([np.linalg.norm(a,axis=1),np.linalg.norm(b,axis=1),np.linalg.norm(a-b,axis=1)],axis=1)
    minimum_altitude=2*area/np.maximum(lengths.max(axis=1),1e-15)
    lo,hi=co.min(axis=0),co.max(axis=0);height=hi[2]-lo[2];origin=(hi+lo)*.5;origin[2]=lo[2]
    centers=co[tri].mean(axis=1);normalized=(centers-origin)/height
    leg=(normalized[:,2]>.12)&(normalized[:,2]<.44)&(np.abs(normalized[:,0])<.10)
    edge_tri=np.sort(np.vstack([tri[:,[0,1]],tri[:,[1,2]],tri[:,[2,0]]]),axis=1)
    tid=np.tile(np.arange(len(tri),dtype=np.int32),3)
    # Exact shared-edge pairs, no vertex welding or geometry edits.
    order=np.lexsort((edge_tri[:,1],edge_tri[:,0]));edge_tri=edge_tri[order];tid=tid[order]
    same=np.all(edge_tri[1:]==edge_tri[:-1],axis=1)
    pairs=np.stack([tid[:-1][same],tid[1:][same]],axis=1)
    normals=cross/np.maximum(np.linalg.norm(cross,axis=1)[:,None],1e-18)
    dot=np.einsum('ij,ij->i',normals[pairs[:,0]],normals[pairs[:,1]])
    near_leg=leg[pairs[:,0]]|leg[pairs[:,1]]
    result={'source':str(path),'sha256':before,'vertices':len(co),'triangles':len(tri),
        'polygons_smooth':int(smooth.sum()),'polygons_flat':int((~smooth).sum()),
        'smooth_fraction':float(smooth.mean()),'sharp_edges':int(sharp.sum()),'edges':len(sharp),
        'has_custom_normals':mesh.has_custom_normals,'attributes':[{'name':x.name,'domain':x.domain,'type':x.data_type} for x in mesh.attributes],
        'world_bounds':[lo.tolist(),hi.tolist()],
        'microtriangles':{'area_min':float(area.min()),'area_p01':float(np.quantile(area,.01)),
            'area_under_1e8':int(np.count_nonzero(area<1e-8)),
            'area_under_1e10':int(np.count_nonzero(area<1e-10)),
            'minimum_altitude_under_0001':int(np.count_nonzero(minimum_altitude<.0001)),
            'legs_triangles':int(leg.sum()),'legs_area_under_1e8':int(np.count_nonzero(leg&(area<1e-8)))},
        'adjacent_face_normals':{'edge_pairs':len(pairs),'over_90deg':int(np.count_nonzero(dot<0)),
            'over_150deg':int(np.count_nonzero(dot<-.8660254)),
            'leg_pairs':int(near_leg.sum()),'leg_over_90deg':int(np.count_nonzero(near_leg&(dot<0))),
            'leg_over_150deg':int(np.count_nonzero(near_leg&(dot<-.8660254)))},
        'note':'Actual geometric face angles may expose generated microfolds; these counts do not authorize remodelling or prove which URP pass causes artifacts.'}
    if label!='Original':result['unique_vertex_corner_combinations']=unique_corners(mesh)
    result['input_unchanged']=sha(path)==before;assert result['input_unchanged']
    (OUTPUT/(label+'.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('SURFACE_AUDIT '+label+' '+json.dumps({k:result[k] for k in ['vertices','triangles','smooth_fraction','sharp_edges','has_custom_normals']}),flush=True)
    return result

OUTPUT.mkdir(parents=True,exist_ok=True)
records={}
for label,path in SOURCES.items():
    records[label]=audit(label,path)
    gc.collect()
(OUTPUT/'Comparison.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
print('SURFACE_NORMAL_AUDIT_COMPLETE',flush=True)
