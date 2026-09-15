"""Transfer supplied channels to the actual welded target UV; no source edits.

The old narrow-patch bake cannot be stretched over the expanded join. This
version measures the integrated triangle, tangent frame and world point for
each texel, then samples its corresponding anatomical raw surface.
"""
from pathlib import Path
import hashlib,json,sys
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
STAGE=Path(__file__).resolve().parents[2]
RAW=Path('D:/Project/ORBIS/Assets/blend/여주인공.blend')
RIG=STAGE/'TestResults/CharacterPipeline/RigReview/Stella400kV3/Stella_rigged.blend'
SOURCE=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegIntegrated11/Stella_leg_integrated_trial.blend'
OUTPUT=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegIntegrated12'
TW,TH,PAD=384,768,8
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def arr(seq,key,shape,dtype=np.float64):
    out=np.empty(shape,dtype);seq.foreach_get(key,out.ravel());return out
def unit(v):return v/max(np.linalg.norm(v),1e-12)
def bary(p,abc):
    a,b,c=abc;v0=b-a;v1=c-a;v2=p-a;den=(v0@v0)*(v1@v1)-(v0@v1)**2
    if abs(den)<1e-20:return np.array([1.,0,0])
    v=((v1@v1)*(v2@v0)-(v0@v1)*(v2@v1))/den;w=((v0@v0)*(v2@v1)-(v0@v1)*(v2@v0))/den
    return np.array([1-v-w,v,w])
def sample(image,tex):
    h,w=image.shape[:2];xy=np.clip(tex*np.array([w,h])-.5,[0,0],[w-1,h-1]);i=xy.astype(int);j=np.minimum(i+1,[w-1,h-1]);t=xy-i
    return (image[i[1],i[0]]*(1-t[0])+image[i[1],j[0]]*t[0])*(1-t[1])+(image[j[1],i[0]]*(1-t[0])+image[j[1],j[0]]*t[0])*t[1]
def arrays(mesh):
    mesh.calc_loop_triangles();co=arr(mesh.vertices,'co',(len(mesh.vertices),3));tri=arr(mesh.loop_triangles,'vertices',(len(mesh.loop_triangles),3),np.int32)
    loops=arr(mesh.loop_triangles,'loops',tri.shape,np.int32);uv=arr(mesh.uv_layers.active.data,'uv',(len(mesh.loops),2));cn=arr(mesh.corner_normals,'vector',(len(mesh.loops),3))
    return co,tri,loops,uv,cn
def basis(tc,tu):
    e1=tc[:,1]-tc[:,0];e2=tc[:,2]-tc[:,0];d1=tu[:,1]-tu[:,0];d2=tu[:,2]-tu[:,0];det=d1[:,0]*d2[:,1]-d1[:,1]*d2[:,0];safe=np.where(abs(det)>1e-18,det,1)
    return (e1*d2[:,1,None]-e2*d1[:,1,None])/safe[:,None],(-e1*d2[:,0,None]+e2*d1[:,0,None])/safe[:,None]
before={str(p):sha(p) for p in [RAW,RIG,SOURCE]};OUTPUT.mkdir(exist_ok=True)
if (OUTPUT/'Stella_leg_integrated_trial.blend').exists():raise RuntimeError('Preserve existing mapped candidate')
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');mesh=obj.data
co,tri,loops,uv,cn=arrays(mesh);poly=arr(mesh.loop_triangles,'polygon_index',(len(tri),),np.int32);mi=arr(mesh.polygons,'material_index',(len(mesh.polygons),),np.int32)
targets={}
for side in ['Left','Right']:
    ids=np.flatnonzero((mi[poly]==1)&((uv[loops].mean(1)[:,0]<.5) if side=='Left' else (uv[loops].mean(1)[:,0]>.5)))
    tc=co[tri[ids]];tu=uv[loops[ids]];tn=cn[loops[ids]];flat=np.c_[tu.reshape(-1,2),np.zeros(len(ids)*3)]
    tree=BVHTree.FromPolygons(flat.tolist(),np.arange(len(flat)).reshape(-1,3).tolist(),all_triangles=True);tt,tb=basis(tc,tu)
    targets[side]=(tree,tc,tu,tn,tt,tb)
bpy.ops.wm.open_mainfile(filepath=str(RAW),load_ui=False,use_scripts=False)
mesh=next(o.data for o in bpy.context.scene.objects if o.type=='MESH');co,tri,loops,uv,cn=arrays(mesh)
maps={}
for name in ['Image_0','Image_1','Image_2']:
    im=bpy.data.images[name];w,h=im.size;maps[name]=arr(im.pixels,'',()) if False else np.empty(w*h*4,np.float32)
    im.pixels.foreach_get(maps[name]);maps[name]=maps[name].reshape(h,w,4)
ref=np.load(RIG.parent/'Stella_geometry.npz');skin=np.load(RIG.parent/'Stella_weights.npz');rt=ref['triangles'];rb=BVHTree.FromPolygons(ref['world'].tolist(),rt.tolist(),all_triangles=True)
lw=skin['weights'][:,np.isin(skin['names'],['LeftUpperLeg','LeftLowerLeg','LeftFoot','LeftToes'])].sum(1)
rw=skin['weights'][:,np.isin(skin['names'],['RightUpperLeg','RightLowerLeg','RightFoot','RightToes'])].sum(1)
leg=skin['regions']=='leg';rf=np.zeros(len(rt),np.int8);rf[(leg&(lw>rw))[rt].sum(1)>=2]=1;rf[(leg&(rw>lw))[rt].sum(1)>=2]=2
lo,hi=co.min(0),co.max(0);nz=(co[:,2]-lo[2])/(hi[2]-lo[2]);candidates=np.flatnonzero((nz[tri].min(1)<.467)&(nz[tri].max(1)>.203));raw_side=np.zeros(len(tri),np.int8)
cache=OUTPUT/'RawFaceSide.npz'
if cache.exists():
    stored=np.load(cache);assert str(stored['source_hashes'])==json.dumps(before,sort_keys=True);raw_side=stored['side']
else:
    for i in candidates:
        _,_,ri,distance=rb.find_nearest(Vector(co[tri[i]].mean(0)))
        if distance<.012:raw_side[i]=rf[ri]
    np.savez_compressed(cache,side=raw_side,source_hashes=json.dumps(before,sort_keys=True))
print('EXTENDED_MAP_SOURCE_CLASSIFIED',flush=True)
reports={};outputs={kind:[] for kind in ['BaseColor','Mask','Normal']}
for side in ['Left','Right']:
    ids=np.flatnonzero(raw_side==(1 if side=='Left' else 2));tc=co[tri[ids]];tu=uv[loops[ids]];tn=cn[loops[ids]];st,sb=basis(tc,tu)
    source_tree=BVHTree.FromPolygons(co.tolist(),tri[ids].tolist(),all_triangles=True)
    target_tree,pc,pu,pn,pt,pb=targets[side];out={k:np.ones((TH,TW,4),np.float32) for k in outputs};misses=0;reject=0;outside=0;max_projection=0;max_uv_padding_pixels=0
    for row in range(TH):
        for col in range(TW):
            u=(PAD+col+.5+(0 if side=='Left' else TW+2*PAD))/(2*(TW+2*PAD));v=(PAD+row+.5)/(TH+2*PAD)
            q,_,fi,d=target_tree.find_nearest(Vector((u,v,0)));outside+=d>1e-5
            max_uv_padding_pixels=max(max_uv_padding_pixels,float(np.linalg.norm((np.asarray(q)[:2]-[u,v])*[2*(TW+2*PAD),TH+2*PAD])))
            b=bary(np.asarray(q)[:2],pu[fi]);p=(pc[fi]*b[:,None]).sum(0);n=unit((pn[fi]*b[:,None]).sum(0));t=unit(pt[fi]-n*(pt[fi]@n));bit=np.cross(n,t)
            if bit@pb[fi]<0:bit=-bit
            hit=source_tree.ray_cast(Vector(p+n*.035),Vector(-n),.07)
            if hit[0] is None:misses+=1;hit=source_tree.find_nearest(Vector(p))
            h,_,si,_=hit;max_projection=max(max_projection,float(np.linalg.norm(np.asarray(h)-p)));bw=bary(np.asarray(h),tc[si]);tex=(tu[si]*bw[:,None]).sum(0)
            out['BaseColor'][row,col]=sample(maps['Image_0'],tex);out['Mask'][row,col]=sample(maps['Image_1'],tex)
            nts=sample(maps['Image_2'],tex)[:3]*2-1;sn=unit((tn[si]*bw[:,None]).sum(0));tang=unit(st[si]-sn*(st[si]@sn));bi=np.cross(sn,tang)
            if bi@sb[si]<0:bi=-bi
            wn=unit(tang*nts[0]+bi*nts[1]+sn*nts[2])
            if wn@n<.25:reject+=1;wn=n
            out['Normal'][row,col,:3]=np.array([wn@t,wn@bit,wn@n])*.5+.5
        if row%192==0:print('EXTENDED_MAP_ROW',side,row,flush=True)
    for kind,pix in out.items():
        pix=np.concatenate([pix[:,-PAD:],pix,pix[:,:PAD]],axis=1);outputs[kind].append(np.pad(pix,((PAD,PAD),(0,0),(0,0)),mode='edge'))
    reports[side]={'source_triangles':len(ids),'texels':TW*TH,'ray_fallbacks':misses,'unused_edge_texels_padded_from_target_uv':outside,'max_unused_uv_padding_pixels':max_uv_padding_pixels,'inward_source_normals_replaced':reject,'max_projection_source_units':max_projection}
    # The arc-length bridge has a nonrectangular UV boundary. A few texels lie
    # outside every actual UV triangle: copy its nearest boundary, like a
    # gutter. They are not sampled by the mesh, and no target UV is changed.
    print('EXTENDED_MAP_REPORT',side,json.dumps(reports[side]),flush=True)
    # Unused UV texels do not intersect any target triangle; their padding
    # distance is diagnostic, not a surface error. Only a source ray failure
    # can make a used target texel sample the wrong anatomical surface here.
    if misses>100:raise RuntimeError('Projection quality gate failed '+str(reports[side]))
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');mat=obj.data.materials[1];bsdf=mat.node_tree.nodes.get('Principled BSDF')
for old in list(mat.node_tree.nodes):
    if old.type not in ['BSDF_PRINCIPLED','OUTPUT_MATERIAL']:mat.node_tree.nodes.remove(old)
for kind,pieces in outputs.items():
    combined=np.concatenate(pieces,axis=1);im=bpy.data.images.new('Stella extended '+kind,width=combined.shape[1],height=combined.shape[0],alpha=True);im.colorspace_settings.name='sRGB' if kind=='BaseColor' else 'Non-Color'
    im.pixels.foreach_set(combined.ravel());im.filepath_raw=str(OUTPUT/('LegPatch_'+kind+'.png'));im.file_format='PNG';im.save();im.pack();node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=im
    if kind=='BaseColor':mat.node_tree.links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
    elif kind=='Mask':
        sep=mat.node_tree.nodes.new('ShaderNodeSeparateColor');sep.mode='RGB';mat.node_tree.links.new(node.outputs['Color'],sep.inputs['Color']);mat.node_tree.links.new(sep.outputs['Green'],bsdf.inputs['Roughness']);mat.node_tree.links.new(sep.outputs['Blue'],bsdf.inputs['Metallic'])
    else:
        normal=mat.node_tree.nodes.new('ShaderNodeNormalMap');normal.space='TANGENT';mat.node_tree.links.new(node.outputs['Color'],normal.inputs['Color']);mat.node_tree.links.new(normal.outputs['Normal'],bsdf.inputs['Normal'])
bpy.context.preferences.filepaths.save_version=0;bpy.context.preferences.filepaths.file_preview_type='NONE';output=OUTPUT/'Stella_leg_integrated_trial.blend';bpy.ops.wm.save_as_mainfile(filepath=str(output),relative_remap=True)
assert before=={str(p):sha(p) for p in [RAW,RIG,SOURCE]}
(OUTPUT/'MaterialTransfer.json').write_text(json.dumps({'source_hashes_unchanged':before,'output_sha256':sha(output),'reports':reports,'status':'Actual target triangle UV and tangent transfer. Geometry/skin/UV/bones unchanged from11. Unity acceptance pending.','channels':'Source Image0=BaseColor, Image1 G=roughness B=metallic, Image2=tangent normal. No global toon changes.'},indent=2),encoding='utf-8')
print('EXTENDED_MAPS_COMPLETE',flush=True)
