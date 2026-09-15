"""Separate skinned leg-patch integration trial; source rigs remain frozen.

Actual metal/stellar ornament faces remain original 3D geometry. The central
damaged base faces are removed, not hidden behind another rendered shell.
Explicit source planar cuts, classified outer loops, and connected bridge seams.
"""
from pathlib import Path
import hashlib,json,sys
import bpy
import numpy as np
from mathutils import Vector

STAGE=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(Path(__file__).resolve().parent))
from rig_heroes import stella_leg_weights,reset_pose,pose_metrics,mesh_arrays
SOURCE=STAGE/'TestResults/CharacterPipeline/RigReview/Stella400kV3/Stella_rigged.blend'
PROOF=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegEnvelope11/Stella_leg_envelope_proof.blend'
OUTPUT=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegIntegrated11'
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def smooth(a,b,x):
    t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)

before={str(p):sha(p) for p in [SOURCE,PROOF]};OUTPUT.mkdir(parents=True,exist_ok=True)
if (OUTPUT/'Stella_leg_integrated_trial.blend').exists():raise RuntimeError('Preserve existing extended integration')
bpy.ops.wm.open_mainfile(filepath=str(PROOF),load_ui=False,use_scripts=False)
body=next(o for o in bpy.context.scene.objects if o.type=='MESH' and not o.name.endswith('PROOF_ONLY'))
patches=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.endswith('PROOF_ONLY')]
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');reset_pose(rig)
mesh=body.data
co,tri,edges=mesh_arrays(body)
norms=np.empty((len(mesh.loops),3),np.float32);mesh.corner_normals.foreach_get('vector',norms.ravel())
uv=np.empty((len(mesh.loops),2),np.float32);mesh.uv_layers.active.data.foreach_get('uv',uv.ravel())
polygon_smooth=np.empty(len(mesh.polygons),bool);mesh.polygons.foreach_get('use_smooth',polygon_smooth)
mesh.calc_loop_triangles();loops=np.empty((len(mesh.loop_triangles),3),np.int32);mesh.loop_triangles.foreach_get('loops',loops.ravel())
triangle_polygons=np.empty(len(tri),np.int32);mesh.loop_triangles.foreach_get('polygon_index',triangle_polygons)
smooth_flags=polygon_smooth[triangle_polygons]
names=[g.name for g in body.vertex_groups]
weights=np.zeros((len(co),len(names)),np.float64)
for vertex in mesh.vertices:
    for group in vertex.groups:weights[vertex.index,group.group]=group.weight
report=json.loads((SOURCE.parent/'Stella_inspection.json').read_text(encoding='utf-8'))
origin=np.asarray(report['normalization_origin']);height=report['height'];normalized=(co-origin)/height
pureleg=np.zeros(len(co),bool);rightleg=np.zeros(len(co),bool)
skin_np=np.load(SOURCE.parent/'Stella_weights.npz');side_masks={}
for side in ['Left','Right']:
    other='Right' if side=='Left' else 'Left'
    own=weights[:,[names.index(side+n) for n in ['UpperLeg','LowerLeg','Foot','Toes']]].sum(1)
    opposite=weights[:,[names.index(other+n) for n in ['UpperLeg','LowerLeg','Foot','Toes']]].sum(1)
    side_mask=(skin_np['regions']=='leg')&(own>opposite);side_masks[side]=side_mask
    pureleg|=side_mask
    if side=='Right':rightleg=side_mask
z=normalized[:,2]
# A source cut plane within a few micrometres of an old vertex creates tiny
# slivers. Choose the nearest well-separated gap in a +/-0.1mm source window.
cut_adjustments=[];cut_values=[]
for target in [.215,.455]:
    target_world=origin[2]+target*height
    values=np.sort(np.unique(co[pureleg,2]));near=values[np.abs(values-target_world)<.0001]
    gaps=np.diff(near);centers=(near[1:]+near[:-1])*.5
    score=gaps-0.01*np.abs(centers-target_world);idx=int(np.argmax(score))
    selected=float(centers[idx]);cut_values.append((selected-origin[2])/height)
    cut_adjustments.append({'target':target_world,'selected':selected,'delta':selected-target_world,'nearest_vertex_clearance':float(gaps[idx]*.5)})
Z0,Z1=cut_values

# Keep crossing/end faces; only wholly internal base faces are removed.
eligible=pureleg[tri].all(1)&(z[tri].min(1)>Z0)&(z[tri].max(1)<Z1)

# Source atlas classification is restricted to the dark right boot. The bare
# left leg is not reclassified as metal just because skin is bright/warm.
atlas=bpy.data.images.get('Image_0');w,h=atlas.size
pixels=np.empty(w*h*4,np.float32);atlas.pixels.foreach_get(pixels);pixels=pixels.reshape(h,w,4)
centeruv=uv[loops].mean(1)
ix=np.clip((centeruv[:,0]*w).astype(int),0,w-1);iy=np.clip((centeruv[:,1]*h).astype(int),0,h-1)
color=pixels[iy,ix,:3]
warm=(color[:,0]>.34)&(color[:,0]>color[:,2]*1.07)&(color[:,1]>color[:,2]*1.015)
left_gold=(color[:,0]>.30)&(color[:,0]>color[:,2]*1.25)&(color[:,1]>color[:,2]*1.07)
metal=eligible&((rightleg[tri].all(1)&warm)|(side_masks['Left'][tri].all(1)&left_gold))
edgekeys=np.sort(np.vstack([tri[:,[0,1]],tri[:,[1,2]],tri[:,[2,0]]]),axis=1)
faceids=np.tile(np.arange(len(tri)),3);order=np.lexsort((edgekeys[:,1],edgekeys[:,0]));edgekeys=edgekeys[order];faceids=faceids[order]
same=np.all(edgekeys[:-1]==edgekeys[1:],axis=1);pairs=np.stack([faceids[:-1][same],faceids[1:][same]],axis=1)
# One adjacent face ring retains the actual beveled sides of thin metal.
adjacent=metal[pairs].any(1)
grown=metal.copy();grown[pairs[adjacent].ravel()]=True;grown&=eligible
parent=np.arange(len(tri))
def find(i):
    while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
    return i
for a,b in pairs[grown[pairs].all(1)]:
    a,b=find(int(a)),find(int(b))
    if a!=b:parent[b]=a
area=.5*np.linalg.norm(np.cross(co[tri[:,1]]-co[tri[:,0]],co[tri[:,2]]-co[tri[:,0]]),axis=1)
kept_ornaments=np.zeros(len(tri),bool);components=[]
members={}
for i in np.flatnonzero(grown):members.setdefault(find(int(i)),[]).append(int(i))
for indices in members.values():
    total=float(area[indices].sum())
    if total<.000007:continue # Reject isolated generated flecks, not coherent ornament strips.
    kept_ornaments[indices]=True
    pts=co[tri[indices]].reshape(-1,3)
    components.append({'faces':len(indices),'area_source_units_squared':total,'bounds':[pts.min(0).tolist(),pts.max(0).tolist()]})

# Some source ornament selections include an inner microfold wall completely
# behind the closed new leg. Inverted-hull extrusion can expose these buried
# backfaces as speckles. Remove only triangles whose three original vertices
# are demonstrably inside by >0.75mm; visible gold faces/vertices stay exact.
from mathutils.bvhtree import BVHTree
buried_ornaments=np.zeros(len(tri),bool);buried_records=[]
transition_records=[]
for side in ['Left','Right']:
    patch=next(o for o in patches if o.name.startswith(side));pco,ptri,_=mesh_arrays(patch)
    bvh=BVHTree.FromPolygons(pco.tolist(),ptri.tolist(),all_triangles=True)
    selected=np.flatnonzero(kept_ornaments&side_masks[side][tri].all(1));verts=np.unique(tri[selected]);signed=np.full(len(co),np.inf)
    for v in verts:
        near,n,_,_=bvh.find_nearest(Vector(co[v]));signed[v]=float((Vector(co[v])-near).dot(n))
    removed=selected[(signed[tri[selected]]<-.00075).all(1)];buried_ornaments[removed]=True
    buried_records.append({'side':side,'selected_ornament_faces':len(selected),'wholly_buried_faces_removed':len(removed),'inside_distance_threshold_source_units':.00075})
kept_ornaments&=~buried_ornaments

# Every original vertex outside the removed base remains exact. New cut
# vertices interpolate only the intersected source edge, including UV/skin.
newco=[];newtri=[];newuv=[];newnormals=[];newweights=[];newmaterial=[];new_smooth=[]
key_to_new={};boundary_sets={(side,end):set() for side in side_masks for end in [0,1]}
original_map={};cut_normals={};retained_faces=[];cut_face_count=0;removed_faces=0
def skin_normalize(row):
    row=np.maximum(row,0);idx=np.argsort(row)[-4:];out=np.zeros_like(row);out[idx]=row[idx]
    return out/max(out.sum(),1e-12)
def vertex(key,p,w):
    if key in key_to_new:return key_to_new[key]
    i=len(newco);key_to_new[key]=i;newco.append(np.asarray(p).tolist());newweights.append(np.asarray(w).tolist())
    if isinstance(key,int):original_map[key]=i
    return i
def face(ids,tex,norm,material=0,smoothflag=True):
    if len(set(ids))!=3:raise RuntimeError('Degenerate index triangle')
    newtri.append(list(ids));newuv.extend(np.asarray(tex).tolist());newnormals.extend(np.asarray(norm).tolist())
    newmaterial.append(material);new_smooth.append(smoothflag)
def original_face(i):
    ids=[vertex(int(v),co[v],weights[v]) for v in tri[i]]
    face(ids,uv[loops[i]],norms[loops[i]],0,bool(smooth_flags[i]));retained_faces.append(i)
def clip_original(i,side,end):
    global cut_face_count
    plane=origin[2]+[Z0,Z1][end]*height
    data=[{'key':int(v),'p':co[v], 'w':weights[v], 'uv':uv[li], 'n':norms[li]} for v,li in zip(tri[i],loops[i])]
    result=[]
    def inside(x):return x['p'][2]<=plane if end==0 else x['p'][2]>=plane
    for a,b in zip(data,data[1:]+data[:1]):
        ina,inb=inside(a),inside(b)
        if ina:result.append(a)
        if ina!=inb:
            t=(plane-a['p'][2])/(b['p'][2]-a['p'][2]);key=(min(a['key'],b['key']),max(a['key'],b['key']),end)
            p=a['p']+(b['p']-a['p'])*t;p[2]=plane
            n=a['n']+(b['n']-a['n'])*t;n/=max(np.linalg.norm(n),1e-12)
            result.append({'key':key,'p':p,'w':skin_normalize(a['w']+(b['w']-a['w'])*t),'uv':a['uv']+(b['uv']-a['uv'])*t,'n':n})
    ids=[]
    for d in result:
        vi=vertex(d['key'],d['p'],d['w']);ids.append(vi)
        if isinstance(d['key'],tuple):
            boundary_sets[(side,end)].add(vi);cut_normals.setdefault(vi,[]).append(d['n'])
    for j in range(1,len(result)-1):
        ii=[0,j,j+1];face([ids[k] for k in ii],[result[k]['uv'] for k in ii],[result[k]['n'] for k in ii],0,bool(smooth_flags[i]))
    cut_face_count+=1

for i,t in enumerate(tri):
    side=next((s for s,mask in side_masks.items() if mask[t].all()),None)
    zz=z[t]
    if side is None or zz.max()<=Z0 or zz.min()>=Z1:
        original_face(i);continue
    if zz.min()<Z0:clip_original(i,side,0)
    if zz.max()>Z1:clip_original(i,side,1)
    # Entire 3D ornament triangles are retained at their exact source position;
    # they are never projected into the replacement cylinder.
    if kept_ornaments[i]:original_face(i)
    else:removed_faces+=1

new_to_key={v:k for k,v in key_to_new.items()}
source_vertex_uv={}
for t,ls in zip(tri,loops):
    for v,li in zip(t,ls):
        if int(v) not in source_vertex_uv:source_vertex_uv[int(v)]=uv[li]
base_count=len(newtri)
base_tri=np.asarray(newtri,int)
edge_counts={};edge_directions={}
for t in base_tri:
    for a,b in zip(t,np.roll(t,-1)):
        key=(int(min(a,b)),int(max(a,b)));edge_counts[key]=edge_counts.get(key,0)+1;edge_directions[key]=(int(a),int(b))
cap_adjustments=[];seam_records=[];outer_loops={};baseco=np.asarray(newco);baseweights=np.asarray(newweights)
for (side,end),members in boundary_sets.items():
    adjacency={}
    for (a,b),count in edge_counts.items():
        if count==1 and a in members and b in members:
            adjacency.setdefault(a,[]).append(b);adjacency.setdefault(b,[]).append(a)
    assert all(len(v)==2 for v in adjacency.values()),f'{side} {end} source cut has open/branching paths'
    unseen=set(adjacency);section_loops=[]
    while unseen:
        start=min(unseen);ordered=[start];prev=None;current=start
        while True:
            unseen.discard(current);nxt=next(v for v in adjacency[current] if v!=prev)
            if nxt==start:break
            ordered.append(nxt);prev,current=current,nxt
        xy=baseco[ordered,:2];signed=.5*np.sum(xy[:,0]*np.roll(xy[:,1],-1)-xy[:,1]*np.roll(xy[:,0],-1))
        section_loops.append((abs(signed),signed,ordered))
    section_loops.sort(reverse=True,key=lambda x:x[0])
    assert section_loops[0][0]>.004,'Largest loop does not span actual leg'
    outer=section_loops[0][2]
    if section_loops[0][1]<0:outer=outer[::-1]
    outer_loops[(side,end)]=outer
    # Original inner wall and microscopic handle sections close inside the
    # outer loop. These caps have no exposed overlapping outside wall.
    cap_count=0
    for _,signed,ordered in section_loops[1:]:
        # Centroid fan closes every cut edge, including microscopic slivers
        # whose coincident float positions defeat polygon tessellation. These
        # caps are inside the measured outside loop; no original vertex weld.
        a,b=ordered[:2]
        if edge_directions[(min(a,b),max(a,b))]==(a,b):ordered=ordered[::-1]
        pts=baseco[ordered];center=pts.mean(0)
        # Keep the new interior cap planar but move only its new center a few
        # micrometres if a boundary edge and centroid are nearly collinear.
        def fan_min(c):return float(np.linalg.norm(np.cross(np.roll(pts,-1,axis=0)-pts,c-pts),axis=1).min()*.5)
        if fan_min(center)<2e-12:
            original_center=center.copy();best=fan_min(center)
            for radius in [0.000002,0.000005,0.00001,0.00002]:
                for angle in np.linspace(0,2*np.pi,24,endpoint=False):
                    c=original_center+np.array([np.cos(angle),np.sin(angle),0])*radius;value=fan_min(c)
                    if value>best:best=value;center=c
                if best>=2e-12:break
            cap_adjustments.append({'delta':float(np.linalg.norm(center-original_center)),'minimum_triangle_area':best})
        ww=skin_normalize(baseweights[ordered].mean(0))
        centerid=vertex(('cap',side,end,len(newco)),center,ww)
        tex=[]
        for v in ordered:
            oldkey=new_to_key[v];sourcevert=oldkey[0] if isinstance(oldkey,tuple) else oldkey
            tex.append(source_vertex_uv[sourcevert])
        centeruv=np.mean(tex,0)
        for k,(a,b) in enumerate(zip(ordered,ordered[1:]+ordered[:1])):
            ids=[a,b,centerid]
            n=Vector(newco[b])-Vector(newco[a]);n=n.cross(Vector(center)-Vector(newco[a])).normalized()
            face(ids,[tex[k],tex[(k+1)%len(tex)],centeruv],[n]*3,0,False);cap_count+=1
    seam_records.append({'side':side,'end':end,'source_loops':len(section_loops),'outer_vertices':len(outer),'outer_area':section_loops[0][0],'interior_cap_triangles':cap_count})

# Keep the proven circular/clamped eight-pixel gutters from Integrated02.
patch_pixels=[];pad=8
for side in ['Left','Right']:
    image=bpy.data.images.load(str(PROOF.parent/(side+'_LegPatch_SourceColor.png')),check_existing=True)
    pw,ph=image.size;a=np.empty(pw*ph*4,np.float32);image.pixels.foreach_get(a);a=a.reshape(ph,pw,4)
    a=np.concatenate([a[:,-pad:],a,a[:,:pad]],axis=1);patch_pixels.append(np.pad(a,((pad,pad),(0,0),(0,0)),mode='edge'))
combined=np.concatenate(patch_pixels,axis=1)
patch_image=bpy.data.images.new('Stella local leg source atlas',width=combined.shape[1],height=combined.shape[0],alpha=True)
patch_image.colorspace_settings.name='sRGB';patch_image.pixels.foreach_set(combined.ravel())
patch_image.filepath_raw=str(OUTPUT/'LegPatch_BaseColor.png');patch_image.file_format='PNG';patch_image.save();patch_image.pack()
patchmat=bpy.data.materials.new('Stella local leg atlas');patchmat.use_nodes=True
node=patchmat.node_tree.nodes.new('ShaderNodeTexImage');node.image=patch_image
patchmat.node_tree.links.new(node.outputs['Color'],patchmat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
patchmat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.65

for side in ['Left','Right']:
    patch=next(o for o in patches if o.name.startswith(side))
    pco,ptri,_=mesh_arrays(patch);ROWS=len(pco)//96;grid=pco.reshape(ROWS,96,3);centers=grid.mean(1)
    pn=np.empty((len(patch.data.vertices),3),np.float32);patch.data.vertices.foreach_get('normal',pn.ravel())
    # Spread the measured source outer-loop/patch radial difference over only
    # the newly replaced end strips. The old approach concentrated this change
    # within one 3.5mm row and caused the visible horizontal geometry step.
    # Original source vertices are never moved, and the central patch stays put.
    endpoint_changes=[]
    for end in [0,1]:
        outer=outer_loops[(side,end)];xy=np.asarray(newco)[outer,:2];center=centers[0 if end==0 else -1,:2]
        angles=np.arctan2(xy[:,1]-center[1],xy[:,0]-center[0])%(2*np.pi);radii=np.linalg.norm(xy-center,axis=1)
        order=np.argsort(angles);angles=angles[order];radii=radii[order]
        cols=np.arange(96)/96*2*np.pi;target=np.interp(cols,np.r_[angles[-1]-2*np.pi,angles,angles[0]+2*np.pi],np.r_[radii[-1],radii,radii[0]])
        source_row=0 if end==0 else ROWS-1
        current=np.linalg.norm(grid[source_row,:,:2]-center,axis=1);delta=target-current
        for row in range(ROWS):
            distance=abs(grid[row,0,2]-grid[source_row,0,2]);factor=1-smooth(0,.025,distance)
            if factor<=0:continue
            radial=grid[row,:,:2]-centers[row,:2];radial/=np.maximum(np.linalg.norm(radial,axis=1)[:,None],1e-12)
            change=radial*delta[:,None]*factor;grid[row,:,:2]+=change
            endpoint_changes.append(float(np.linalg.norm(change,axis=1).max()))
    transition_records.append({'side':side,'maximum_radial_transition_displacement':max(endpoint_changes,default=0)})
    # Recompute geometric normal only on the new patch; source custom normals
    # and source UV/weights remain unchanged.
    du=np.roll(grid,-1,axis=1)-np.roll(grid,1,axis=1)
    dv=np.empty_like(grid);dv[1:-1]=grid[2:]-grid[:-2];dv[0]=grid[1]-grid[0];dv[-1]=grid[-1]-grid[-2]
    newpn=np.cross(du,dv);newpn/=np.maximum(np.linalg.norm(newpn,axis=2)[:,:,None],1e-12);pn=newpn.reshape(-1,3)
    patchids={};pnorm={};param={}
    for row in range(1,ROWS-1):
        for col in range(96):
            p=grid[row,col];n=(p-origin)/height;wrow=np.zeros(len(names))
            for name,value in stella_leg_weights(side,float(n[1]),float(n[2])).items():wrow[names.index(name)]=value
            vi=vertex((side,row,col),p,wrow);patchids[(row,col)]=vi;pnorm[vi]=pn[row*96+col];param[vi]=(col/96,row/(ROWS-1))
    for end in [0,1]:
        outer=outer_loops[(side,end)];xy=np.asarray(newco)[outer,:2];center=centers[0 if end==0 else -1,:2]
        angles=np.arctan2(xy[:,1]-center[1],xy[:,0]-center[0])%(2*np.pi)
        start=int(np.argmin(np.minimum(angles,2*np.pi-angles)));outer=outer[start:]+outer[:start]
        for vi in outer:
            a=np.arctan2(newco[vi][1]-center[1],newco[vi][0]-center[0])%(2*np.pi)
            param[vi]=(a/(2*np.pi),float(end));n=np.mean(cut_normals[vi],0);pnorm[vi]=n/max(np.linalg.norm(n),1e-12)
        outer_loops[(side,end)]=outer
    def patchface(ids):
        values=np.asarray([param[v] for v in ids]);uu=values[:,0]
        if uu.max()-uu.min()>.5:values[uu<.5,0]+=1
        values[:,0]=(pad+values[:,0]*pw+(0 if side=='Left' else pw+2*pad))/combined.shape[1]
        values[:,1]=(pad+values[:,1]*ph)/combined.shape[0]
        face(ids,values,[pnorm[v] for v in ids],1,True)
    for row in range(1,ROWS-2):
        for col in range(96):
            a=patchids[(row,col)];b=patchids[(row,(col+1)%96)];c=patchids[(row+1,(col+1)%96)];d=patchids[(row+1,col)]
            patchface([a,b,c]);patchface([a,c,d])
    def bridge(bottom,top):
        # Arc-length zipper retains each original outer-loop edge exactly once
        # even where a shallow concavity reverses a polar angle.
        def progress(loop):
            pts=np.asarray(newco)[loop];lengths=np.linalg.norm(np.roll(pts,-1,axis=0)-pts,axis=1)
            return np.concatenate([[0],np.cumsum(lengths)])/lengths.sum()
        bp=progress(bottom);tp=progress(top);i=j=0
        while i<len(bottom) or j<len(top):
            a=bottom[i%len(bottom)];b=top[j%len(top)]
            if i<len(bottom) and (j==len(top) or bp[i+1]<=tp[j+1]):
                patchface([a,bottom[(i+1)%len(bottom)],b]);i+=1
            else:
                patchface([a,top[(j+1)%len(top)],b]);j+=1
    bridge(outer_loops[(side,0)],[patchids[(1,c)] for c in range(96)])
    bridge([patchids[(ROWS-2,c)] for c in range(96)],outer_loops[(side,1)])

newmesh=bpy.data.meshes.new('Stella_Leg_Explicit_Seam_Trial_Mesh');newmesh.from_pydata(newco,[],newtri);newmesh.update()
layer=newmesh.uv_layers.new(name='UVMap');layer.data.foreach_set('uv',np.asarray(newuv,np.float32).ravel())
newmesh.polygons.foreach_set('use_smooth',np.asarray(new_smooth,bool));newmesh.normals_split_custom_set(newnormals)
newmesh.materials.append(mesh.materials[0]);newmesh.materials.append(patchmat)
newmesh.polygons.foreach_set('material_index',np.asarray(newmaterial,np.int32));body.data=newmesh
for group in list(body.vertex_groups):body.vertex_groups.remove(group)
for name in names:body.vertex_groups.new(name=name)
nw=np.asarray(newweights);assert np.max(np.abs(nw.sum(1)-1))<1e-5
assert np.max(np.count_nonzero(nw>1e-8,axis=1))<=4
for col,name in enumerate(names):
    group=body.vertex_groups[name]
    for i in np.flatnonzero(nw[:,col]>1e-8):group.add([int(i)],float(nw[i,col]),'REPLACE')
for patch in patches:bpy.data.objects.remove(patch,do_unlink=True)
body.hide_set(False);body.hide_render=False
for old,new in original_map.items():
    assert np.array_equal(np.asarray(newco[new]),co[old])
    assert np.array_equal(nw[new],weights[old])
world,faces,newedges=mesh_arrays(body)
allkeys=np.sort(np.vstack([faces[:,[0,1]],faces[:,[1,2]],faces[:,[2,0]]]),axis=1)
unique,counts=np.unique(allkeys,axis=0,return_counts=True)
cutmembers=set().union(*boundary_sets.values())
seam_edge_bad=[e.tolist() for e,c in zip(unique,counts) if c!=2 and (int(e[0]) in cutmembers or int(e[1]) in cutmembers)]
areas=np.linalg.norm(np.cross(world[faces[:,1]]-world[faces[:,0]],world[faces[:,2]]-world[faces[:,0]]),axis=1)*.5
test_report={'Rest':pose_metrics(body,world,newedges)}
from rig_heroes import test_pose
for pose in ['Relaxed','WalkContact','JointStress']:
    test_pose(rig,pose);test_report[pose]=pose_metrics(body,world,newedges)
reset_pose(rig)
bpy.context.preferences.filepaths.save_version=0;bpy.context.preferences.filepaths.file_preview_type='NONE'
path=OUTPUT/'Stella_leg_integrated_trial.blend';bpy.ops.wm.save_as_mainfile(filepath=str(path),relative_remap=True)
assert before=={str(p):sha(p) for p in [SOURCE,PROOF]}
result={'status':'Explicit welded outer-seam trial, not game ready. Actual full/close/flex review required.',
    'source_hashes_unchanged':before,'output':str(path),'sha256':sha(path),'vertices':len(newco),'triangles':len(newtri),
    'original_faces_removed_or_clipped':removed_faces,'original_faces_retained':len(retained_faces),'source_faces_clipped':cut_face_count,
    'original_ornament_faces_retained':int(kept_ornaments.sum()),'ornament_components':components,
    'buried_original_ornament_backfaces':buried_records,'new_patch_end_transition_width_source_units':.025,'new_patch_transition_records':transition_records,
    'retained_source_positions_weights_exact':True,'retained_source_corner_uv_normals_exact':True,
    'cut_plane_adjustments':cut_adjustments,'new_cap_center_adjustments':cap_adjustments,'materials':2,'bone_count':len(rig.data.bones),'atlas_chart_gutter_pixels':pad,'seams':seam_records,
    'topology':{'boundary_edges':int((counts==1).sum()),'nonmanifold_edges':int((counts>2).sum()),'seam_bad_edges':seam_edge_bad,
        'degenerate_area_lt_1e_12':int((areas<1e-12).sum()),'duplicate_triangles':int(len(faces)-len(np.unique(np.sort(faces,axis=1),axis=0)))},
    'pose_metrics':test_report,'limits':['Preserved ornament strips may have source-to-patch perimeter topology requiring further work.',
        'No source projection or overlapping old base boundary. Inner source wall/micro-loop caps are separate from outside bridge.',
        'Only local leg patch uses newly sampled source color; supplied other atlases/geometry remain unchanged.']}
(OUTPUT/'Integration.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('STELLA_LEG_EXPLICIT_SEAM '+json.dumps({k:result[k] for k in ['vertices','triangles','topology','seams']}),flush=True)
