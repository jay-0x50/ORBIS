"""Separate skinned leg-patch integration trial; source rigs remain frozen.

Actual metal/stellar ornament faces remain original 3D geometry. The central
damaged base faces are removed, not hidden behind another rendered shell.
Patch-end overlaps are a review hypothesis, not claimed welded topology.
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
PROOF=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegEnvelope04/Stella_leg_envelope_proof.blend'
OUTPUT=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegIntegrated02'
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def smooth(a,b,x):
    t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)

before={str(p):sha(p) for p in [SOURCE,PROOF]};OUTPUT.mkdir(parents=True,exist_ok=True)
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
for side in ['Left','Right']:
    side_mask=weights[:,[names.index(side+'UpperLeg'),names.index(side+'LowerLeg')]].sum(1)>.985
    pureleg|=side_mask
    if side=='Right':rightleg=side_mask
z=normalized[:,2]
# Keep crossing/end faces; only wholly internal base faces are removed.
eligible=pureleg[tri].all(1)&(z[tri].min(1)>.235)&(z[tri].max(1)<.412)

# Source atlas classification is restricted to the dark right boot. The bare
# left leg is not reclassified as metal just because skin is bright/warm.
atlas=bpy.data.images.get('Image_0');w,h=atlas.size
pixels=np.empty(w*h*4,np.float32);atlas.pixels.foreach_get(pixels);pixels=pixels.reshape(h,w,4)
centeruv=uv[loops].mean(1)
ix=np.clip((centeruv[:,0]*w).astype(int),0,w-1);iy=np.clip((centeruv[:,1]*h).astype(int),0,h-1)
color=pixels[iy,ix,:3]
warm=(color[:,0]>.34)&(color[:,0]>color[:,2]*1.07)&(color[:,1]>color[:,2]*1.015)
metal=eligible&rightleg[tri].all(1)&warm
edgekeys=np.sort(np.vstack([tri[:,[0,1]],tri[:,[1,2]],tri[:,[2,0]]]),axis=1)
faceids=np.tile(np.arange(len(tri)),3);order=np.lexsort((edgekeys[:,1],edgekeys[:,0]));edgekeys=edgekeys[order];faceids=faceids[order]
same=np.all(edgekeys[:-1]==edgekeys[1:],axis=1);pairs=np.stack([faceids[:-1][same],faceids[1:][same]],axis=1)
# One adjacent face ring retains the actual beveled sides of thin metal.
adjacent=metal[pairs].any(1)
grown=metal.copy();grown[pairs[adjacent].ravel()]=True;grown&=eligible&rightleg[tri].all(1)
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
keep=~eligible|kept_ornaments
assert kept_ornaments.sum()>50,'Ornament selection failed; refuse to flatten all metal.'
ornament_vertices=np.zeros(len(co),bool);ornament_vertices[tri[kept_ornaments].ravel()]=True
adjustedco=co.copy();boundary_factor=np.zeros(len(co));boundary_normals=np.zeros_like(co)
# V1's overlap alone left a jagged lighting/shape line. Match only the actual
# leg transition strips to the measured patch field, including their geometric
# normals. Gold/cloth/head/UV/weights remain protected. This is not a weld.
for side in ['Left','Right']:
    patch=next(o for o in patches if o.name.startswith(side))
    pco,_,_=mesh_arrays(patch);grid=pco.reshape(97,96,3);centers=grid.mean(1)
    pn=np.empty((len(patch.data.vertices),3),np.float32);patch.data.vertices.foreach_get('normal',pn.ravel());pn=pn.reshape(97,96,3)
    side_mask=weights[:,[names.index(side+'UpperLeg'),names.index(side+'LowerLeg')]].sum(1)>.985
    distance=np.minimum(np.abs(z-.235),np.abs(z-.412))*height
    factor=(1-smooth(.003,.014,distance))*side_mask*(~ornament_vertices)
    for i in np.flatnonzero(factor>0):
        rf=(z[i]-.235)/(.412-.235)*96;r0=int(np.clip(np.floor(rf),0,95));t=rf-r0
        center=centers[r0]*(1-t)+centers[r0+1]*t
        angle=np.arctan2(co[i,1]-center[1],co[i,0]-center[0])%(2*np.pi)
        cf=angle/(2*np.pi)*96;c0=int(cf)%96;c1=(c0+1)%96;s=cf-np.floor(cf)
        target=(grid[r0,c0]*(1-s)+grid[r0,c1]*s)*(1-t)+(grid[r0+1,c0]*(1-s)+grid[r0+1,c1]*s)*t
        target[2]=co[i,2];delta=target-co[i]
        if np.linalg.norm(delta)>.004:continue # Do not pull protruding source details into the field.
        adjustedco[i]+=delta*factor[i];boundary_factor[i]=factor[i]
        normal=(pn[r0,c0]*(1-s)+pn[r0,c1]*s)*(1-t)+(pn[r0+1,c0]*(1-s)+pn[r0+1,c1]*s)*t
        boundary_normals[i]=normal/max(np.linalg.norm(normal),1e-12)
loop_vertex=np.empty(len(mesh.loops),np.int32);mesh.loops.foreach_get('vertex_index',loop_vertex)
f=boundary_factor[loop_vertex,None]
adjustednormals=norms*(1-f)+boundary_normals[loop_vertex]*f
adjustednormals/=np.maximum(np.linalg.norm(adjustednormals,axis=1)[:,None],1e-12)
adjustednormals[f[:,0]==0]=norms[f[:,0]==0]
assert np.array_equal(adjustedco[ornament_vertices],co[ornament_vertices])
face_indices=np.flatnonzero(keep);old_tri=tri[face_indices]
used=np.unique(old_tri);remap=np.full(len(co),-1,int);remap[used]=np.arange(len(used))
newco=adjustedco[used].tolist();newtri=remap[old_tri].tolist();newuv=uv[loops[face_indices]].reshape(-1,2).tolist()
newnormals=adjustednormals[loops[face_indices]].reshape(-1,3).tolist()
newweights=weights[used].tolist();newmaterial=[0]*len(newtri);new_smooth=smooth_flags[face_indices].tolist()

# Put both new cylinder charts in one patch atlas, leaving the supplied atlas
# and every surviving source UV byte intact. Two material draws instead of three.
patch_pixels=[]
for side in ['Left','Right']:
    image=bpy.data.images.load(str(PROOF.parent/(side+'_LegPatch_SourceColor.png')),check_existing=True)
    pw,ph=image.size;a=np.empty(pw*ph*4,np.float32);image.pixels.foreach_get(a);a=a.reshape(ph,pw,4)
    # Circular horizontal gutters and clamped vertical gutters prevent atlas
    # filtering from mixing the bare leg with the adjacent dark boot chart.
    pad=8;a=np.concatenate([a[:,-pad:],a,a[:,:pad]],axis=1)
    patch_pixels.append(np.pad(a,((pad,pad),(0,0),(0,0)),mode='edge'))
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
    pco,ptri,_=mesh_arrays(patch);pm=patch.data
    puv=np.empty((len(pm.loops),2),np.float32);pm.uv_layers.active.data.foreach_get('uv',puv.ravel())
    pm.calc_loop_triangles();ploops=np.empty((len(pm.loop_triangles),3),np.int32);pm.loop_triangles.foreach_get('loops',ploops.ravel())
    pnormal=np.empty((len(pm.loops),3),np.float32);pm.corner_normals.foreach_get('vector',pnormal.ravel())
    pn=(pco-origin)/height
    # Small inward overlap only at patch ends prevents the untouched crossing
    # faces from z-fighting; the central damaged base faces have been removed.
    # This is explicitly not a welded seam and must pass whole-body URP review.
    distance=np.minimum(pn[:,2]-.235,.412-pn[:,2])*height
    inset=.00015*(1-smooth(0,.004,distance))
    for row in range(97):
        sl=slice(row*96,(row+1)*96);center=pco[sl].mean(0);rad=pco[sl]-center;rad[:,2]=0
        pco[sl]-=rad/np.maximum(np.linalg.norm(rad,axis=1)[:,None],1e-12)*inset[sl,None]
    base=len(newco);newco.extend(pco.tolist());newtri.extend((ptri+base).tolist())
    patchuv=puv[ploops].reshape(-1,2)
    patchuv[:,0]=(pad+patchuv[:,0]*pw+(0 if side=='Left' else pw+2*pad))/combined.shape[1]
    patchuv[:,1]=(pad+patchuv[:,1]*ph)/combined.shape[0]
    newuv.extend(patchuv.tolist());newnormals.extend(pnormal[ploops].reshape(-1,3).tolist())
    newmaterial.extend([1]*len(ptri));new_smooth.extend([True]*len(ptri))
    for p in pn:
        binding=stella_leg_weights(side,float(p[1]),float(p[2]));row=[0.0]*len(names)
        for name,value in binding.items():row[names.index(name)]=value
        newweights.append(row)

newmesh=bpy.data.meshes.new('Stella_Leg_Integrated_Trial_Mesh');newmesh.from_pydata(newco,[],newtri);newmesh.update()
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
assert np.array_equal(np.asarray(newco)[:len(used)][boundary_factor[used]==0],co[used][boundary_factor[used]==0])
assert np.array_equal(np.asarray(newuv,np.float32)[:len(face_indices)*3],uv[loops[face_indices]].reshape(-1,2))
assert np.array_equal(nw[:len(used)],weights[used])
world,faces,newedges=mesh_arrays(body)
test_report={'Rest':pose_metrics(body,world,newedges)}
from rig_heroes import test_pose
for pose in ['Relaxed','WalkContact','JointStress']:
    test_pose(rig,pose);test_report[pose]=pose_metrics(body,world,newedges)
reset_pose(rig)
bpy.context.preferences.filepaths.save_version=0;bpy.context.preferences.filepaths.file_preview_type='NONE'
path=OUTPUT/'Stella_leg_integrated_trial.blend';bpy.ops.wm.save_as_mainfile(filepath=str(path),relative_remap=True)
assert before=={str(p):sha(p) for p in [SOURCE,PROOF]}
result={'status':'Skinned integration experiment; patch-end seams and preserved 3D metal need actual full-character render/URP review. Not game-ready.',
    'source_hashes_unchanged':before,'output':str(path),'sha256':sha(path),'vertices':len(newco),'triangles':len(newtri),
    'original_faces_removed':int(np.count_nonzero(~keep)),'original_faces_retained':len(face_indices),
    'original_ornament_faces_retained':int(kept_ornaments.sum()),'ornament_components':components,
    'retained_source_uv_weights_exact':True,'protected_source_vertex_positions_exact':True,
    'boundary_vertices_adjusted':int(np.count_nonzero(boundary_factor)),'boundary_position_delta_max_source_units':float(np.linalg.norm(adjustedco-co,axis=1).max()),
    'original_ornament_positions_exact':True,'materials':2,'bone_count':len(rig.data.bones),
    'atlas_chart_gutter_pixels':pad,
    'new_patch_end_inset_source_units':.00015,'new_patch_end_seam':'Boundary field positions/normals blended over 14mm; small inward overlap, not topologically welded. Explicit review gate.',
    'source_to_patch_metrics':str(PROOF.parent/'Experiment.json'),'pose_metrics':test_report,
    'limits':['Only normalized central leg band .235-.412 is changed; unmodified areas may retain source microfolds.',
        '3D ornament faces remain from the source, with their original UV/weights, rather than replacing all metal with flat texture.',
        'Common toon shader must use the new patch atlas for material slot 1; do not apply the original atlas normal map to this new UV.',
        'Two source materials imply two base draws plus existing outline draws. Profile before accepting.']}
(OUTPUT/'Integration.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('STELLA_LEG_INTEGRATED_TRIAL '+json.dumps({k:result[k] for k in ['vertices','triangles','original_faces_removed','original_ornament_faces_retained','materials','bone_count']}),flush=True)
