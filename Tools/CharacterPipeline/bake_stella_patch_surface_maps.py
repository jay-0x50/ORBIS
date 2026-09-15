"""Transfer supplied mask/normal into the local retopo UV, preserving all raw bytes.

No colour repaint or whole-character rebake. This only calibrates the two local
leg charts so their physical shading channels match the adjacent source atlas.
"""
from pathlib import Path
import json,hashlib,math,sys
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
STAGE=Path(__file__).resolve().parents[2]
RAW=Path('D:/Project/ORBIS/Assets/blend/여주인공.blend')
RIG=STAGE/'TestResults/CharacterPipeline/RigReview/Stella400kV3/Stella_rigged.blend'
PROOF=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegEnvelope04/Stella_leg_envelope_proof.blend'
OUTPUT=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegSurfaceMaps01'
OUTPUT.mkdir(exist_ok=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
hashes={str(p):sha(p) for p in [RAW,RIG,PROOF]}
Z0,Z1=.235,.412;TW,TH=384,768
ref=np.load(RIG.parent/'Stella_geometry.npz');skin=np.load(RIG.parent/'Stella_weights.npz')
bpy.ops.wm.open_mainfile(filepath=str(PROOF),load_ui=False,use_scripts=False)
surfaces={}
for side in ['Left','Right']:
    obj=next(o for o in bpy.context.scene.objects if o.name.startswith(side) and o.name.endswith('PROOF_ONLY'))
    c=np.empty((len(obj.data.vertices),3),np.float64);obj.data.vertices.foreach_get('co',c.ravel())
    n=np.empty_like(c);obj.data.vertices.foreach_get('normal',n.ravel())
    surfaces[side]=(c.reshape(97,96,3),n.reshape(97,96,3))
bpy.ops.wm.open_mainfile(filepath=str(RAW),load_ui=False,use_scripts=False)
mesh=next(o.data for o in bpy.context.scene.objects if o.type=='MESH');mesh.calc_loop_triangles()
co=np.empty((len(mesh.vertices),3),np.float64);mesh.vertices.foreach_get('co',co.ravel())
tri=np.empty((len(mesh.loop_triangles),3),np.int32);mesh.loop_triangles.foreach_get('vertices',tri.ravel())
loops=np.empty_like(tri);mesh.loop_triangles.foreach_get('loops',loops.ravel())
uv=np.empty((len(mesh.loops),2),np.float32);mesh.uv_layers.active.data.foreach_get('uv',uv.ravel())
cn=np.empty((len(mesh.loops),3),np.float32);mesh.corner_normals.foreach_get('vector',cn.ravel())
lo,hi=co.min(0),co.max(0);height=hi[2]-lo[2];origin=(lo+hi)*.5;origin[2]=lo[2];normalized=(co-origin)/height
pixels={}
for name in ['Image_1','Image_2']:
    image=bpy.data.images[name];w,h=image.size;p=np.empty(w*h*4,np.float32);image.pixels.foreach_get(p);pixels[name]=p.reshape(h,w,4)
def sample(image,tex):
    h,w=image.shape[:2];xy=np.clip(tex*np.array([w,h])-.5,[0,0],[w-1,h-1]);i=xy.astype(int);j=np.minimum(i+1,[w-1,h-1]);t=xy-i
    return (image[i[1],i[0]]*(1-t[0])+image[i[1],j[0]]*t[0])*(1-t[1])+(image[j[1],i[0]]*(1-t[0])+image[j[1],j[0]]*t[0])*t[1]
def unit(v):return v/max(np.linalg.norm(v),1e-12)
def barycentric(p,abc):
    a,b,c=abc;v0=b-a;v1=c-a;v2=p-a;d00=v0@v0;d01=v0@v1;d11=v1@v1;d20=v2@v0;d21=v2@v1;den=d00*d11-d01*d01
    if abs(den)<1e-20:return np.array([1.,0,0])
    v=(d11*d20-d01*d21)/den;ww=(d00*d21-d01*d20)/den;return np.array([1-v-ww,v,ww])
reports={}
for side,sign in [('Left',1),('Right',-1)]:
    vm=(normalized[:,2]>Z0-.012)&(normalized[:,2]<Z1+.012)&(normalized[:,0]*sign>.002)&(normalized[:,0]*sign<.105)&(normalized[:,1]<-.02)&(normalized[:,1]>-.135)
    reference_mask=skin['weights'][:,np.isin(skin['names'],[side+'UpperLeg',side+'LowerLeg'])].sum(1)>.985
    measured_z=np.linspace(Z0-.013,Z1+.013,81);measured=[]
    for zz in measured_z:
        pts=ref['world'][reference_mask&(np.abs(ref['normalized'][:,2]-zz)<.004)]
        measured.append((np.quantile(pts,.015,axis=0)+np.quantile(pts,.985,axis=0))*.5)
    measured=np.asarray(measured)
    ax=np.interp(normalized[:,2],measured_z,measured[:,0]);ay=np.interp(normalized[:,2],measured_z,measured[:,1])
    vm&=np.sqrt((co[:,0]-ax)**2+(co[:,1]-ay)**2)<.075
    ti=np.flatnonzero(vm[tri].all(1));part=tri[ti];used=np.unique(part);remap=np.full(len(co),-1,np.int32);remap[used]=np.arange(len(used))
    bvh=BVHTree.FromPolygons(co[used].tolist(),remap[part].tolist(),all_triangles=True)
    tco=co[part];tuv=uv[loops[ti]];tcn=cn[loops[ti]]
    e1=tco[:,1]-tco[:,0];e2=tco[:,2]-tco[:,0];d1=tuv[:,1]-tuv[:,0];d2=tuv[:,2]-tuv[:,0]
    determinant=d1[:,0]*d2[:,1]-d1[:,1]*d2[:,0];safe=np.where(np.abs(determinant)>1e-16,determinant,1)
    tt=(e1*d2[:,1,None]-e2*d1[:,1,None])/safe[:,None]
    bb=(-e1*d2[:,0,None]+e2*d1[:,0,None])/safe[:,None]
    grid,gn=surfaces[side];centers=grid.mean(1);mask_out=np.ones((TH,TW,4),np.float32);normal_out=np.ones_like(mask_out)
    misses=0;rejected_normals=0
    for row in range(TH):
        rf=row/(TH-1)*96;r0=min(int(rf),95);t=rf-r0;center=centers[r0]*(1-t)+centers[r0+1]*t
        for col in range(TW):
            angle=math.tau*(col+.5)/TW;direction=np.array([math.cos(angle),math.sin(angle),0])
            hit=bvh.ray_cast(Vector(center+direction*.16),Vector(-direction),.32)
            if hit[0] is None or (np.asarray(hit[0])-center)@direction<.005:
                misses+=1;hit=bvh.find_nearest(Vector(center+direction*.05))
            point,_,fi,_=hit;bary=barycentric(np.asarray(point),tco[fi]);tex=(tuv[fi]*bary[:,None]).sum(0)
            mask_out[row,col]=sample(pixels['Image_1'],tex)
            tangent_normal=sample(pixels['Image_2'],tex)[:3]*2-1
            source_n=unit((tcn[fi]*bary[:,None]).sum(0));source_t=unit(tt[fi]-source_n*(tt[fi]@source_n));source_b=np.cross(source_n,source_t)
            if source_b@bb[fi]<0:source_b=-source_b
            world_n=unit(source_t*tangent_normal[0]+source_b*tangent_normal[1]+source_n*tangent_normal[2])
            cf=(col+.5)/TW*96;c0=int(cf)%96;c1=(c0+1)%96;s=cf-int(cf)
            target_n=unit((gn[r0,c0]*(1-s)+gn[r0,c1]*s)*(1-t)+(gn[r0+1,c0]*(1-s)+gn[r0+1,c1]*s)*t)
            target_t=unit(grid[r0,c1]-grid[r0,c0]);target_t=unit(target_t-target_n*(target_t@target_n));target_b=np.cross(target_n,target_t)
            # Reject raw inward/near-tangent microhandle shading normals, not
            # meaningful outward detail. Count this repair explicitly.
            if world_n@target_n<.25:rejected_normals+=1;world_n=target_n
            normal_out[row,col,:3]=np.array([world_n@target_t,world_n@target_b,world_n@target_n])*.5+.5
        if row%192==0:print('PATCH_MAP_ROW',side,row,flush=True)
    for name,array in [('Mask',mask_out),('Normal',normal_out)]:
        image=bpy.data.images.new(side+'_LegPatch_'+name,width=TW,height=TH,alpha=True);image.colorspace_settings.name='Non-Color';image.pixels.foreach_set(array.ravel())
        image.filepath_raw=str(OUTPUT/(side+'_LegPatch_'+name+'.png'));image.file_format='PNG';image.save()
    reports[side]={'ray_misses':misses,'pixels':TW*TH,'inward_raw_shading_normals_rejected':rejected_normals,
        'mask_green_roughness_p50':float(np.median(mask_out[:,:,1])),'mask_blue_metallic_p50':float(np.median(mask_out[:,:,2]))}
assert hashes=={str(p):sha(p) for p in [RAW,RIG,PROOF]}
(OUTPUT/'Transfer.json').write_text(json.dumps({'source_hashes_unchanged':hashes,'method':'Raw outward ray -> barycentric source UV -> supplied mask/normal. Source tangent normal converted through world normal into target cylinder tangent frame. Original map bytes untouched.','results':reports},indent=2),encoding='utf-8')
print('PATCH_SURFACE_MAPS_COMPLETE '+json.dumps(reports),flush=True)
