"""Local surface-envelope feasibility trial, not a production replacement.

Only the central leg band is sampled. Raw face/UV/atlas are used directly;
face/hair/garments are never remeshed or rebaked. Open patch-end boundaries
are explicitly reported and must be resolved before integration.
"""
from pathlib import Path
import hashlib,json,math,sys,zlib,struct
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

STAGE=Path(__file__).resolve().parents[2]
RAW=Path('D:/Project/ORBIS/Assets/blend/여주인공.blend')
RIG=STAGE/'TestResults/CharacterPipeline/RigReview/Stella400kV3/Stella_rigged.blend'
OUTPUT=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegEnvelope04'
Z0,Z1=.235,.412
RINGS,SEGMENTS=97,96
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def arrays(mesh):
    co=np.empty((len(mesh.vertices),3),np.float32);mesh.vertices.foreach_get('co',co.ravel())
    mesh.calc_loop_triangles();tri=np.empty((len(mesh.loop_triangles),3),np.int32);mesh.loop_triangles.foreach_get('vertices',tri.ravel())
    loops=np.empty((len(mesh.loop_triangles),3),np.int32);mesh.loop_triangles.foreach_get('loops',loops.ravel())
    uv=np.empty((len(mesh.loops),2),np.float32);mesh.uv_layers.active.data.foreach_get('uv',uv.ravel())
    return co,tri,uv[loops]

def barycentric(point,a,b,c):
    v0=b-a;v1=c-a;v2=point-a
    d00=np.dot(v0,v0);d01=np.dot(v0,v1);d11=np.dot(v1,v1);d20=np.dot(v2,v0);d21=np.dot(v2,v1)
    den=d00*d11-d01*d01
    if abs(den)<1e-20:return np.array([1,0,0])
    v=(d11*d20-d01*d21)/den;w=(d00*d21-d01*d20)/den
    return np.array([1-v-w,v,w])

before={str(p):sha(p) for p in [RAW,RIG]};OUTPUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(RAW),load_ui=False,use_scripts=False)
rawobj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
co,tri,uv=arrays(rawobj.data)
lo,hi=co.min(0),co.max(0);height=float(hi[2]-lo[2]);origin=(lo+hi)*.5;origin[2]=lo[2]
normalized=(co-origin)/height
atlas=bpy.data.images.get('Image_0');atlas_size=list(atlas.size)
pixels=np.empty(atlas_size[0]*atlas_size[1]*4,np.float32);atlas.pixels.foreach_get(pixels)
pixels=pixels.reshape((atlas_size[1],atlas_size[0],4))
results={};surfaces=[]
reference_geometry=np.load(RIG.parent/'Stella_geometry.npz')
reference_skin=np.load(RIG.parent/'Stella_weights.npz')

for side,sign in [('Left',1),('Right',-1)]:
    # Outside garments are behind this isolated band. Per-side filtering also
    # prevents a radial ray from reaching the opposite leg or a cape panel.
    vm=(normalized[:,2]>Z0-.012)&(normalized[:,2]<Z1+.012)&(normalized[:,0]*sign>.002)&(normalized[:,0]*sign<.105)&(normalized[:,1]<-.02)&(normalized[:,1]>-.135)
    # V1 captured a nearby cape strip. V2/3 used the joint centreline, which
    # differs from the anatomical surface centre by up to 24mm and clipped a
    # valid wall. Measure the actual cleaned leg component at each height.
    reference_mask=reference_skin['weights'][:,np.isin(reference_skin['names'],[side+'UpperLeg',side+'LowerLeg'])].sum(1)>.985
    measured_z=np.linspace(Z0-.013,Z1+.013,81);measured_centers=[]
    for zz in measured_z:
        pts=reference_geometry['world'][reference_mask&(np.abs(reference_geometry['normalized'][:,2]-zz)<.004)]
        measured_centers.append((np.quantile(pts,.015,axis=0)+np.quantile(pts,.985,axis=0))*.5)
    measured_centers=np.asarray(measured_centers)
    axis_x=np.interp(normalized[:,2],measured_z,measured_centers[:,0])
    axis_y=np.interp(normalized[:,2],measured_z,measured_centers[:,1])
    axis_radius=np.sqrt((co[:,0]-axis_x)**2+(co[:,1]-axis_y)**2)
    vm&=axis_radius<.075
    ti=np.flatnonzero(vm[tri].all(1));part=tri[ti]
    bvh=BVHTree.FromPolygons(co.tolist(),part.tolist(),all_triangles=True)
    zs=np.linspace(Z0,Z1,RINGS);centers=[];radii=np.full((RINGS,SEGMENTS),np.nan);miss=[]
    for row,z in enumerate(zs):
        center=np.array([np.interp(z,measured_z,measured_centers[:,0]),np.interp(z,measured_z,measured_centers[:,1]),origin[2]+z*height])
        centers.append(center)
        for col in range(SEGMENTS):
            angle=math.tau*col/SEGMENTS;direction=np.array([math.cos(angle),math.sin(angle),0])
            result=bvh.ray_cast(Vector(center+direction*.16),Vector(-direction),.32)
            if result[0] is None:miss.append((row,col));continue
            radius=np.dot(np.asarray(result[0])-center,direction)
            # A ray that passes the leg centre hit the opposite wall through
            # a source microhole. It is a missing outward sample, not a dent.
            if radius<.005:miss.append((row,col));continue
            radii[row,col]=radius
    finite=np.isfinite(radii);valid_radii=radii[finite]
    if len(valid_radii)==0:raise RuntimeError('No leg surface hit')
    # Missing rays expose raw holes. Nearest angular interpolation is explicit,
    # and no candidate can pass if the miss rate/surface error is excessive.
    for row,col in miss:
        available=np.flatnonzero(finite[row]);distance=np.minimum((available-col)%SEGMENTS,(col-available)%SEGMENTS)
        radii[row,col]=radii[row,available[np.argmin(distance)]]
    initial=radii.copy()
    for _ in range(2):
        stack=np.stack([np.roll(radii,i,axis=1) for i in [-2,-1,0,1,2]])
        radii=np.median(stack,axis=0)
        vertical=np.pad(radii,((1,1),(0,0)),mode='edge')
        radii=(vertical[:-2]+2*vertical[1:-1]+vertical[2:])*.25
    centers=np.asarray(centers);verts=[];faces=[];texuv=[]
    for row in range(RINGS):
        for col in range(SEGMENTS):
            angle=math.tau*col/SEGMENTS;verts.append(centers[row]+np.array([math.cos(angle),math.sin(angle),0])*radii[row,col])
    for row in range(RINGS-1):
        for col in range(SEGMENTS):
            a=row*SEGMENTS+col;b=row*SEGMENTS+(col+1)%SEGMENTS;c=(row+1)*SEGMENTS+(col+1)%SEGMENTS;d=(row+1)*SEGMENTS+col
            faces.extend([(a,b,c),(a,c,d)])
            u0,u1=col/SEGMENTS,(col+1)/SEGMENTS;v0,v1=row/(RINGS-1),(row+1)/(RINGS-1)
            texuv.extend([[(u0,v0),(u1,v0),(u1,v1)],[(u0,v0),(u1,v1),(u0,v1)]])
    verts=np.asarray(verts);faces=np.asarray(faces)
    proxybvh=BVHTree.FromPolygons(verts.tolist(),faces.tolist(),all_triangles=True)
    source_ids=np.flatnonzero(vm&(normalized[:,2]>=Z0)&(normalized[:,2]<=Z1))
    sampled=source_ids[np.linspace(0,len(source_ids)-1,min(100000,len(source_ids))).astype(int)]
    source_errors=np.array([proxybvh.find_nearest(Vector(co[i]))[3] for i in sampled])
    proxy_errors=np.array([bvh.find_nearest(Vector(v))[3] for v in verts])
    # Copy original source atlas color at the outward-facing raw surface hit.
    # This new patch-only image has its own cylinder UV; unrelated atlases remain untouched.
    tw,th=384,768;patch=np.zeros((th,tw,4),np.float32);missing_colors=0
    for row in range(th):
        rf=row/(th-1)*(RINGS-1);r0=min(int(rf),RINGS-2);t=rf-r0
        center=centers[r0]*(1-t)+centers[r0+1]*t
        for col in range(tw):
            angle=math.tau*(col+.5)/tw;direction=np.array([math.cos(angle),math.sin(angle),0])
            hit=bvh.ray_cast(Vector(center+direction*.16),Vector(-direction),.32)
            if hit[0] is None or np.dot(np.asarray(hit[0])-center,direction)<.005:
                missing_colors+=1
                radius=np.interp(angle/math.tau*SEGMENTS,np.arange(SEGMENTS+1),np.append(radii[r0],radii[r0,0]))
                hit=bvh.find_nearest(Vector(center+direction*radius))
            point,_,face,_=hit
            face_global=ti[face];abc=co[tri[face_global]]
            bary=barycentric(np.asarray(point),*abc);tex=(uv[face_global]*bary[:,None]).sum(0)
            x=np.clip(tex[0]*atlas_size[0]-.5,0,atlas_size[0]-1);y=np.clip(tex[1]*atlas_size[1]-.5,0,atlas_size[1]-1)
            ix,iy=int(x),int(y);jx,jy=min(ix+1,atlas_size[0]-1),min(iy+1,atlas_size[1]-1);tx,ty=x-ix,y-iy
            patch[row,col]=((pixels[iy,ix]*(1-tx)+pixels[iy,jx]*tx)*(1-ty)+(pixels[jy,ix]*(1-tx)+pixels[jy,jx]*tx)*ty)
    image=bpy.data.images.new(side+'_LegPatch_SourceColor',width=tw,height=th,alpha=True)
    image.colorspace_settings.name='sRGB';image.pixels.foreach_set(patch.ravel());image.filepath_raw=str(OUTPUT/(side+'_LegPatch_SourceColor.png'));image.file_format='PNG';image.save();image.pack()
    # Store the image bytes; reopening the rig below would otherwise discard it.
    def error(values):return {'samples':len(values),'mean':float(values.mean()),'p95':float(np.quantile(values,.95)),'p99':float(np.quantile(values,.99)),'max':float(values.max())}
    results[side]={'vertices':len(verts),'triangles':len(faces),'raw_surface_triangles':len(part),'ray_misses':len(miss),'ray_count':radii.size,
        'radii_range':[float(initial.min()),float(initial.max())],'radial_filter_delta_max':float(np.max(np.abs(initial-radii))),
        'source_to_patch':error(source_errors),'patch_to_source':error(proxy_errors),'color_ray_misses_used_nearest':missing_colors,'color_texels':tw*th,
        'patch_color_png':str(OUTPUT/(side+'_LegPatch_SourceColor.png'))}
    surfaces.append((side,verts.tolist(),faces.tolist(),texuv))
    print('LEG_ENVELOPE '+side+' '+json.dumps(results[side]),flush=True)

bpy.ops.wm.open_mainfile(filepath=str(RIG),load_ui=False,use_scripts=False)
for side,verts,faces,texuv in surfaces:
    mesh=bpy.data.meshes.new(side+'_LegEnvelope_Mesh');mesh.from_pydata(verts,[],faces);mesh.update()
    for p in mesh.polygons:p.use_smooth=True
    layer=mesh.uv_layers.new(name='LocalPatchUV')
    for poly,coords in zip(mesh.polygons,texuv):
        for li,coord in zip(poly.loop_indices,coords):layer.data[li].uv=coord
    obj=bpy.data.objects.new(side+'_LegEnvelope_PROOF_ONLY',mesh);bpy.context.scene.collection.objects.link(obj)
    image=bpy.data.images.load(str(OUTPUT/(side+'_LegPatch_SourceColor.png')));image.pack()
    mat=bpy.data.materials.new(side+'_LegEnvelope_SourceColor');mat.use_nodes=True
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;mat.node_tree.links.new(node.outputs['Color'],mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.65;mesh.materials.append(mat)
    # Proof is isolated: original model remains saved and can be toggled for
    # comparison. Unstitched patch ends prohibit gameplay use.
    obj.hide_render=True;obj.hide_set(True)
bpy.context.preferences.filepaths.save_version=0;bpy.context.preferences.filepaths.file_preview_type='NONE'
path=OUTPUT/'Stella_leg_envelope_proof.blend';bpy.ops.wm.save_as_mainfile(filepath=str(path),relative_remap=True)
assert before=={str(p):sha(p) for p in [RAW,RIG]}
(OUTPUT/'Experiment.json').write_text(json.dumps({'status':'Proof only; patch ends open, no accepted replacement/skin/Unity export',
    'source_hashes_unchanged':before,'normalized_z_band':[Z0,Z1],'original_unmodified_model_retained':True,
    'surface_method':'Per-side outer radial rays from raw source, limited median smoothing, cylindrical patch; current raw UV untouched and sampled into a patch-only texture',
    'results':results,'output':str(path)},indent=2),encoding='utf-8')
print('LEG_ENVELOPE_PROOF_COMPLETE',flush=True)
