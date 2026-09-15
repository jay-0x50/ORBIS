"""Read-only hero/Wayfarer source review; separate temporary grip-pose scene.

The existing weapon geometry, hero rest mesh/UV/weights and bones are preserved.
The proposed socket is a Blender-space candidate, not an approved Unity Euler.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from rig_heroes import reset_pose, rotate_world, integrity
from audit_raw_meshes import add_area

STAGE=Path(__file__).resolve().parents[2]
REPO=Path('D:/Project/ORBIS')
WEAPON=REPO/'Tools/LookDev/Sources/WayfarerBlade.blend'
SOURCES={
    'Stella':STAGE/'TestResults/CharacterPipeline/RigReview/Stella400kV3/Stella_rigged.blend',
    'Polaris':STAGE/'TestResults/CharacterPipeline/RigReview/PolarisCoatV6/Polaris_rigged.blend'}

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def rows(m):return [list(row) for row in m]

def posed_world(obj):
    evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh()
    coords=np.empty((len(mesh.vertices),3),np.float64);mesh.vertices.foreach_get('co',coords.ravel())
    matrix=np.asarray(obj.matrix_world);coords=coords@matrix[:3,:3].T+matrix[:3,3]
    evaluated.to_mesh_clear();return coords

def hand_mask(obj):
    indices={g.index for g in obj.vertex_groups if g.name=='RightHand' or g.name.startswith(('RightThumb','RightIndex','RightMiddle','RightRing','RightLittle'))}
    return np.array([sum(g.weight for g in v.groups if g.group in indices)>.5 for v in obj.data.vertices])

def contact_metric(obj,weapon,mask):
    points=posed_world(obj)[mask];inverse=np.asarray(weapon.matrix_world.inverted())
    local=points@inverse[:3,:3].T+inverse[:3,3]
    axial=(local[:,2]>-.068)&(local[:,2]<.063)
    q=local[axial]
    # Analytic comparison to the actual authored tapered elliptical grip rings.
    # This vertex-only clearance diagnostic cannot certify collision-free faces.
    z=[-.079,-.068,-.024,.026,.063]
    rx=np.interp(q[:,2],z,[.014,.013,.011,.012,.014])
    ry=np.interp(q[:,2],z,[.011,.010,.009,.0095,.011])
    radial=np.sqrt((q[:,0]/rx)**2+(q[:,1]/ry)**2)
    approximate_gap=(radial-1)*np.minimum(rx,ry)
    return {'hand_vertices':int(mask.sum()),'vertices_within_grip_axial_interval':len(q),
        'inside_grip_elliptical_envelope':int(np.count_nonzero(radial<1)),
        'deeper_than_2mm_source_units':int(np.count_nonzero(approximate_gap<-.002)),
        'minimum_approximate_surface_clearance_source_units':float(approximate_gap.min()) if len(q) else None,
        'method':'Vertex-only analytic tapered-ellipse envelope, source weapon units; conservative review aid, not collision certification.'}

def load_weapon():
    with bpy.data.libraries.load(str(WEAPON),link=False) as (source,target):
        target.objects=source.objects
    objects=[o for o in target.objects if o is not None and o.type=='MESH']
    if len(objects)!=1:raise ValueError('Expected the existing one-mesh Wayfarer source')
    weapon=objects[0];bpy.context.scene.collection.objects.link(weapon)
    weapon.name='Wayfarer_EXISTING_MESH_grip_review'
    return weapon

def set_candidate_pose(rig):
    reset_pose(rig)
    # Initial diagnostic power grip in the actual right hand, with separate
    # thumb adduction. These angles are review defaults, never final motion.
    for finger in ['Index','Middle','Ring','Little']:
        for suffix,angle in [('Proximal',55),('Intermediate',70),('Distal',40)]:
            rotate_world(rig,'Right'+finger+suffix,(0,1,0),-angle)
    for suffix,angle in [('Proximal',0),('Intermediate',10),('Distal',5)]:
        rotate_world(rig,'RightThumb'+suffix,(0,1,0),-angle)
    bpy.context.view_layer.update()

def make_studio(target,height):
    scene=bpy.context.scene
    for obj in list(scene.objects):
        if obj.type in {'CAMERA','LIGHT'}:bpy.data.objects.remove(obj,do_unlink=True)
    scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1100;scene.render.resolution_y=1100
    scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
    scene.world=bpy.data.worlds.new('Grip neutral studio');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.12,.12,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.25
    scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
    for name,direction,power in [('key',(-1,-1,2),180),('fill',(1,-1,.5),100),('rim',(0,2,1.3),180)]:
        add_area(scene,'Grip '+name,target+Vector(direction)*height,target,power*height*height,height)
    data=bpy.data.cameras.new('Grip camera');camera=bpy.data.objects.new('Grip camera',data)
    scene.collection.objects.link(camera);scene.camera=camera;data.type='ORTHO'
    data.ortho_scale=height*.20;data.clip_start=.0001;data.clip_end=20
    return camera

def render_views(output,label,variant,target,height,camera):
    paths=[]
    for view,direction in [('Front',(0,-1,.13)),('Top',(-.1,-.15,1)),('Palm',(-.12,-.3,-1))]:
        camera.location=target+Vector(direction).normalized()*height
        camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
        path=output/(label+'_'+variant+'_'+view+'.png')
        bpy.context.scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)
        paths.append(str(path))
    return paths

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--label',choices=list(SOURCES),required=True)
    parser.add_argument('--output',required=True);parser.add_argument('--render',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]);label=args.label
    source=SOURCES[label];output=Path(args.output).resolve()
    assert 'RigReview' in output.parts and output!=source.parent
    output.mkdir(parents=True,exist_ok=True)
    before={str(p):sha(p) for p in [source,WEAPON]}
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    body=next(o for o in bpy.context.scene.objects if o.type=='MESH')
    body_integrity=integrity(body);mask=hand_mask(body)
    original=posed_world(body);height=float(original[:,2].max()-original[:,2].min())
    audit=json.loads((STAGE/('TestResults/CharacterPipeline/SourceAudit/'+label+'.json')).read_text(encoding='utf-8'))
    lo,hi=Vector(audit['render_bounds']['min']),Vector(audit['render_bounds']['max'])
    origin=(lo+hi)*.5;origin.z=lo.z;source_height=hi.z-lo.z
    weapon=load_weapon();weapon_integrity=integrity(weapon)
    reset_pose(rig);bpy.context.view_layer.update()
    hand=rig.pose.bones['RightHand'];hand_world=rig.matrix_world@hand.matrix
    # Existing prefab local +Y points along the blade; Blender source +Z tip
    # needs this axis conversion before reproducing an all-zero bone socket.
    source_to_unity=Matrix(((1,0,0,0),(0,0,1,0),(0,-1,0,0),(0,0,0,1)))
    weapon.matrix_world=hand_world@source_to_unity
    old_pose={'weapon_world':rows(weapon.matrix_world),'contact':contact_metric(body,weapon,mask)}
    center_normalized=(-.351,-.081,.7255) if label=='Stella' else (-.348,-.038,.744)
    target=origin+Vector(center_normalized)*source_height
    # Actual source hand's dorsal normal is +Z. The sword spans the palm toward
    # the thumb (-Y), so the decorated front faces the dorsal side (+Z).
    proposed=Matrix(((-1,0,0,target.x),(0,0,-1,target.y),(0,-1,0,target.z),(0,0,0,1)))
    camera=make_studio(target,height) if args.render else None
    if args.render:old_pose['renders']=render_views(output,label,'ExistingZeroSocket',target,height,camera)
    set_candidate_pose(rig);weapon.matrix_world=proposed;bpy.context.view_layer.update()
    pose_rotations={b.name:list(b.rotation_quaternion) for b in rig.pose.bones if b.name.startswith(('RightThumb','RightIndex','RightMiddle','RightRing','RightLittle'))}
    candidate={'weapon_world':rows(proposed),'weapon_source_local_to_right_hand_matrix':rows(hand_world.inverted()@proposed),
        'source_normalized_grip_center':center_normalized,'finger_rotation_quaternions_wxyz':pose_rotations,
        'contact':contact_metric(body,weapon,mask)}
    if args.render:candidate['renders']=render_views(output,label,'ProposedGrip',target,height,camera)
    # Save only a fresh staging review scene; keep the original rest rig frozen.
    bpy.context.preferences.filepaths.save_version=0;bpy.context.preferences.filepaths.file_preview_type='NONE'
    scene_path=output/(label+'_grip_review.blend')
    bpy.ops.wm.save_as_mainfile(filepath=str(scene_path),relative_remap=True)
    after={str(p):sha(p) for p in [source,WEAPON]};assert before==after
    assert integrity(body)==body_integrity
    # World placement is expected to differ; the weapon's mesh/UV/materials do not.
    weapon_after=integrity(weapon)
    for key in weapon_integrity:
        if 'matrix' not in key and 'transform' not in key:assert weapon_integrity[key]==weapon_after[key],key
    report={'status':'Temporary grip review only; no live socket or motion edits', 'label':label,
        'source_hashes_unchanged':before,'source_blend':str(source),'weapon_source':str(WEAPON),
        'temporary_scene':str(scene_path),'source_height':source_height,
        'right_hand_rest_world':rows(hand_world),'existing_zero_socket':old_pose,'candidate':candidate,
        'limitations':['Candidate matrix is in Blender source bone space, not Unity Euler values. Validate imported basis before applying.',
            'Old per-avatar weapon scale is not inherited. This review uses the unchanged metre-authored blade.',
            'Finger contact needs direct closeup review; vertex envelope numbers alone do not certify a finished grip.',
            'No attack/locomotion clip, production prefab, catalog or runtime socket was changed.']}
    (output/(label+'_grip_report.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('GRIP_REVIEW_READY '+str(scene_path),flush=True)

if __name__=='__main__':main()
