"""Match the retained source-edge material at the local UV seam only.

The geometry, supplied atlas, retained UVs, bones and skin are untouched. The
replacement atlas receives source material samples from the actual cut edge,
faded over 8mm; this avoids raw-ray/collapsed-source UV mismatch at the seam.
"""
from pathlib import Path
import hashlib,json
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
STAGE=Path(__file__).resolve().parents[2]
SOURCE=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegIntegrated05/Stella_leg_integrated_trial.blend'
OUTPUT=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegIntegrated06';OUTPUT.mkdir(exist_ok=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
before=sha(SOURCE);bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');mesh=obj.data;mesh.calc_loop_triangles()
co=np.empty((len(mesh.vertices),3),np.float64);mesh.vertices.foreach_get('co',co.ravel())
tri=np.empty((len(mesh.loop_triangles),3),np.int32);mesh.loop_triangles.foreach_get('vertices',tri.ravel())
loops=np.empty_like(tri);mesh.loop_triangles.foreach_get('loops',loops.ravel())
uv=np.empty((len(mesh.loops),2),np.float32);mesh.uv_layers.active.data.foreach_get('uv',uv.ravel())
norm=np.empty((len(mesh.loops),3),np.float32);mesh.corner_normals.foreach_get('vector',norm.ravel())
mat=np.empty(len(tri),np.int32);mesh.loop_triangles.foreach_get('material_index',mat)
def pixels(img):
    w,h=img.size;p=np.empty(w*h*4,np.float32);img.pixels.foreach_get(p);return p.reshape(h,w,4)
src={k:pixels(bpy.data.images[n]) for k,n in [('BaseColor','Image_0'),('Mask','Image_1'),('Normal','Image_2')]}
patchmat=mesh.materials[1];texture_nodes=[n for n in patchmat.node_tree.nodes if n.type=='TEX_IMAGE']
images={}
for node in texture_nodes:
    kind='Mask' if 'Mask' in node.image.name else 'Normal' if 'Normal' in node.image.name else 'BaseColor'
    images[kind]=(node,node.image,pixels(node.image))
def unit(v):return v/max(np.linalg.norm(v),1e-12)
def sample(image,tex):
    h,w=image.shape[:2];xy=np.clip(tex*np.array([w,h])-.5,[0,0],[w-1,h-1]);i=xy.astype(int);j=np.minimum(i+1,[w-1,h-1]);t=xy-i
    return (image[i[1],i[0]]*(1-t[0])+image[i[1],j[0]]*t[0])*(1-t[1])+(image[j[1],i[0]]*(1-t[0])+image[j[1],j[0]]*t[0])*t[1]
def frame(fi,bary):
    abc=co[tri[fi]];tex=uv[loops[fi]];n=unit((norm[loops[fi]]*bary[:,None]).sum(0))
    e1,e2=abc[1]-abc[0],abc[2]-abc[0];d1,d2=tex[1]-tex[0],tex[2]-tex[0];den=d1[0]*d2[1]-d1[1]*d2[0]
    if abs(den)<1e-14:return n,unit(np.cross([0,0,1],n)),np.array([0.,0,1])
    tangent=(e1*d2[1]-e2*d1[1])/den;bitangent=(-e1*d2[0]+e2*d1[0])/den
    tangent=unit(tangent-n*(tangent@n));b=np.cross(n,tangent)
    if b@bitangent<0:b=-b
    return n,tangent,b
edge_faces={}
for fi,t in enumerate(tri):
    for a,b in zip(t,np.roll(t,-1)):edge_faces.setdefault((int(min(a,b)),int(max(a,b))),[]).append(fi)
cuts={(s,e):[] for s in ['Left','Right'] for e in [0,1]};pad=8;pw=384;ph=768;W=800;H=784
for (a,b),faces in edge_faces.items():
    if len(faces)!=2 or set(mat[faces])!={0,1}:continue
    sf=next(f for f in faces if mat[f]==0);pf=next(f for f in faces if mat[f]==1)
    midpoint=(co[a]+co[b])*.5;side='Left' if midpoint[0]>0 else 'Right';end=0 if midpoint[2]<-.33 else 1
    pi=[int(np.where(tri[pf]==v)[0][0]) for v in [a,b]];si=[int(np.where(tri[sf]==v)[0][0]) for v in [a,b]]
    values=uv[loops[pf,pi],0];u=(values*W-pad-(0 if side=='Left' else pw+2*pad))/pw
    if abs(u[0]-u[1])>.5:u[u<.5]+=1
    cuts[(side,end)].append({'source_face':sf,'source_corners':si,'u':u})
target_faces=np.flatnonzero(mat==1);targetuv=uv[loops[target_faces]].reshape(-1,2)
uvco=np.column_stack([targetuv,np.zeros(len(targetuv))]);uvtri=np.arange(len(uvco)).reshape(-1,3)
targetbvh=BVHTree.FromPolygons(uvco.tolist(),uvtri.tolist(),all_triangles=True)
def bary2(p,abc):
    a,b,c=abc;v0=b-a;v1=c-a;v2=p-a;den=v0[0]*v1[1]-v1[0]*v0[1]
    v=(v2[0]*v1[1]-v1[0]*v2[1])/den;w=(v0[0]*v2[1]-v2[0]*v0[1])/den;return np.array([1-v-w,v,w])
reports=[];length=(.412-.235)*1.902953028678894;band=0.008
for side in ['Left','Right']:
    xoffset=pad+(0 if side=='Left' else pw+2*pad)
    for end in [0,1]:
        changed=0;miss=0;maxdelta=0
        for row in range(ph):
            distance=(row/(ph-1) if end==0 else 1-row/(ph-1))*length
            if distance>band:continue
            t=distance/band;alpha=1-t*t*(3-2*t)
            for col in range(pw):
                u=(col+.5)/pw;record=None;amount=0
                for edge in cuts[(side,end)]:
                    lo,hi=np.min(edge['u']),np.max(edge['u'])
                    candidate=u+1 if u<lo and u+1<=hi+1e-6 else u
                    if lo-1e-6<=candidate<=hi+1e-6:
                        record=edge;amount=(candidate-edge['u'][0])/(edge['u'][1]-edge['u'][0]);break
                if record is None:miss+=1;continue
                y=pad+row;x=xoffset+col;tex=np.array([(x+.5)/W,(y+.5)/H])
                hit=targetbvh.ray_cast(Vector([tex[0],tex[1],1]),Vector([0,0,-1]),2)
                if hit[0] is None:miss+=1;continue
                target_fi=int(target_faces[hit[2]]);tb=bary2(tex,uv[loops[target_fi]])
                sf=record['source_face'];sb=np.zeros(3);sb[record['source_corners']]=[1-amount,amount]
                source_uv=(uv[loops[sf]]*sb[:,None]).sum(0)
                for kind in ['BaseColor','Mask']:
                    array=images[kind][2];value=sample(src[kind],source_uv)
                    if kind=='BaseColor':maxdelta=max(maxdelta,float(np.max(np.abs(value-array[y,x]))))
                    array[y,x]=array[y,x]*(1-alpha)+value*alpha
                sn,st,sbframe=frame(sf,sb);source_tangent=sample(src['Normal'],source_uv)[:3]*2-1
                world_n=unit(st*source_tangent[0]+sbframe*source_tangent[1]+sn*source_tangent[2])
                tn,tt,tbframe=frame(target_fi,tb);desired=np.array([world_n@tt,world_n@tbframe,world_n@tn])
                current=images['Normal'][2][y,x,:3]*2-1
                images['Normal'][2][y,x,:3]=unit(current*(1-alpha)+desired*alpha)*.5+.5
                changed+=1
        reports.append({'side':side,'end':end,'interface_edges':len(cuts[(side,end)]),'pixels_changed':changed,'missing_boundary_or_target':miss,'max_linear_color_channel_delta':maxdelta})
for kind,(node,old,array) in images.items():
    # Refresh padding from the corrected interior edge, including circular U.
    for xoffset in [0,400]:
        array[pad:pad+ph,xoffset:xoffset+pad]=array[pad:pad+ph,xoffset+pw:xoffset+pw+pad]
        array[pad:pad+ph,xoffset+pad+pw:xoffset+2*pad+pw]=array[pad:pad+ph,xoffset+pad:xoffset+2*pad]
    array[:pad]=array[pad:pad+1];array[pad+ph:]=array[pad+ph-1:pad+ph]
    img=bpy.data.images.new('Stella seam matched '+kind,width=W,height=H,alpha=True)
    img.colorspace_settings.name='sRGB' if kind=='BaseColor' else 'Non-Color';img.pixels.foreach_set(array.ravel())
    img.filepath_raw=str(OUTPUT/('LegPatch_'+kind+'.png'));img.file_format='PNG';img.save();img.pack();node.image=img
bpy.context.preferences.filepaths.save_version=0;bpy.context.preferences.filepaths.file_preview_type='NONE'
output=OUTPUT/'Stella_leg_integrated_trial.blend';bpy.ops.wm.save_as_mainfile(filepath=str(output),relative_remap=True)
assert sha(SOURCE)==before
(OUTPUT/'SeamMaterialMatch.json').write_text(json.dumps({'source_sha256_unchanged':before,'output_sha256':sha(output),'geometry_uv_skin_bones_unchanged':True,'source_atlases_unchanged':True,'new_atlas_blend_band_metres':band,'results':reports,'status':'Actual same-angle seam review required.'},indent=2),encoding='utf-8')
print('STELLA_SEAM_MATERIAL_MATCH '+json.dumps(reports),flush=True)
