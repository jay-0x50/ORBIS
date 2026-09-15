"""Read-only exterior seam versus intentionally open ornament sheet audit."""
from pathlib import Path
import json,hashlib
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
STAGE=Path(__file__).resolve().parents[2]
SOURCE=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegIntegrated03/Stella_leg_integrated_trial.blend'
before=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');mesh=obj.data;mesh.calc_loop_triangles()
co=np.empty((len(mesh.vertices),3),np.float64);mesh.vertices.foreach_get('co',co.ravel())
tri=np.empty((len(mesh.loop_triangles),3),np.int32);mesh.loop_triangles.foreach_get('vertices',tri.ravel())
mat=np.empty(len(tri),np.int32);mesh.loop_triangles.foreach_get('material_index',mat)
keys=np.sort(np.vstack([tri[:,[0,1]],tri[:,[1,2]],tri[:,[2,0]]]),axis=1)
unique,counts=np.unique(keys,axis=0,return_counts=True);boundary=unique[counts==1]
patch=tri[mat==1];bvh=BVHTree.FromPolygons(co.tolist(),patch.tolist(),all_triangles=True)
mid=co[boundary].mean(1);dist=[];signed=[]
for p in mid:
    near,n,index,d=bvh.find_nearest(Vector(p));dist.append(d);signed.append(float((Vector(p)-near).dot(n)))
areas=.5*np.linalg.norm(np.cross(co[tri[:,1]]-co[tri[:,0]],co[tri[:,2]]-co[tri[:,0]]),axis=1)
degrees=np.bincount(boundary.ravel(),minlength=len(co))
report={'source':str(SOURCE),'source_sha256_unchanged':before,'boundary_edge_count':len(boundary),
    'boundary_bounds':[mid.min(0).tolist(),mid.max(0).tolist()],
    'boundary_nearest_patch_distance':{k:float(f(dist)) for k,f in [('min',np.min),('mean',np.mean),('p95',lambda x:np.quantile(x,.95)),('p99',lambda x:np.quantile(x,.99)),('max',np.max)]},
    'boundary_signed_outside_patch_distance':{k:float(f(signed)) for k,f in [('min',np.min),('p01',lambda x:np.quantile(x,.01)),('median',np.median),('p99',lambda x:np.quantile(x,.99)),('max',np.max)]},
    'boundary_vertex_degree_counts':{str(k):int(np.sum(degrees==k)) for k in np.unique(degrees) if k>0},
    'degenerate_faces':[{'face':int(i),'area':float(areas[i]),'vertices':tri[i].tolist(),'positions':co[tri[i]].tolist(),'material':int(mat[i])} for i in np.flatnonzero(areas<1e-12)]}
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==before
(SOURCE.parent/'SeamSupport.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report),flush=True)
