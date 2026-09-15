"""Separate animation-only FBX plus CPU take/binding/pose round trip.

Requires explicit actual material-pose review for this exact motion blend.
Neither immutable rest FBX nor source rig/blend is overwritten.
"""
import argparse,json,sys
from pathlib import Path
import bpy,numpy as np
from mathutils import Matrix,Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import sha256,write_json,array
from rig_bosses import evaluated_positions

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--manifest',required=True)
    parser.add_argument('--visual-review',required=True);parser.add_argument('--output',required=True)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    manifest_path=Path(args.manifest);m=json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    review=json.loads(Path(args.visual_review).read_text(encoding='utf-8-sig'))
    source=Path(m['reviewBlend']);output=Path(args.output).resolve()
    if review.get('motionBlendSha256')!=m['reviewBlendSha256'] or not review.get('acceptedForMotionFbxReview',False):
        raise RuntimeError('Explicit visual review must accept this exact candidate.')
    if sha256(source)!=m['reviewBlendSha256'] or output.exists() or 'Assets' in output.parts:
        raise RuntimeError('Frozen input/new separate output gate failed.')
    output.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
    offset=Vector(m['originContract']['uniform_offset_blender_xyz'])
    rig.animation_data.action=None
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    coordinates=array(obj.data.vertices,'co',(len(obj.data.vertices),3))
    obj.data.vertices.foreach_set('co',(coordinates+np.array(offset)).ravel())
    bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
    for b in rig.data.edit_bones:b.head+=offset;b.tail+=offset
    bpy.ops.object.mode_set(mode='OBJECT');obj.data.update();bpy.context.view_layer.update()
    indices=np.linspace(0,len(obj.data.vertices)-1,min(1000,len(obj.data.vertices)),dtype=int)
    points=[obj.data.vertices[int(i)].co.copy() for i in indices]
    groups={g.index:g.name for g in obj.vertex_groups}
    weights=[[(groups[g.group],g.weight) for g in obj.data.vertices[int(i)].groups] for i in indices]
    reference=[];bone_names=[b.name for b in rig.data.bones]
    original_connect={b.name:b.use_connect for b in rig.data.bones}
    for clip in m['clips']:
        rig.animation_data.action=bpy.data.actions[clip['action']]
        for frame in sorted(set([0,clip['endFrame']//3,clip['endFrame']])):
            bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
            coords=evaluated_positions(obj)[indices]
            world=np.array([obj.matrix_world@Vector(v) for v in coords])
            reference.append((clip['action'],frame,world))
    # Animation-only FBX has no mesh skin-cluster bind pose. Exporting while
    # an Idle pose is active can turn that posed skeleton into its bind rest.
    # All Actions remain fake-user-backed and will be baked independently.
    rig.animation_data.action=None
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
    bpy.context.view_layer.objects.active=rig
    # Unity folds a single exported armature object into the model root,
    # dropping <Label>_GenericRig from animation paths. An inert sibling keeps
    # the exact rig child path used by the separate mesh/rest FBX.
    anchor=bpy.data.objects.new('MotionImportAnchor',None)
    bpy.context.scene.collection.objects.link(anchor);anchor.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(output),use_selection=True,object_types={'ARMATURE','EMPTY'},
        global_scale=1.0,apply_unit_scale=True,apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',axis_up='Y',bake_space_transform=False,
        add_leaf_bones=False,use_armature_deform_only=False,bake_anim=True,
        bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,bake_anim_step=1.0,bake_anim_simplify_factor=0.0)
    from io_scene_fbx import parse_fbx
    tree,version=parse_fbx.parse(str(output))
    objects=next(e for e in tree.elems if e.id==b'Objects')
    takes=[e.props[1].decode('utf-8').split('\x00')[0] for e in objects.elems if e.id==b'AnimationStack']
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.scene.render.fps=m['fps']
    bpy.ops.import_scene.fbx(filepath=str(output),use_anim=True,anim_offset=0)
    imported=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    # Blender's FBX importer infers use_connect=True whenever a child's head
    # equals its parent's tail (import_fbx.py:child_connect), even with
    # force_connect_children=False. FBX/Unity transform hierarchies do not
    # encode this Blender-only translation lock. Restore the exact original
    # connection flags in this temporary validation scene; no positions,
    # matrices, Actions or exported FBX bytes are rewritten.
    automatic_connections=[b.name for b in imported.data.bones
                           if b.use_connect!=original_connect[b.name]]
    bpy.context.view_layer.objects.active=imported
    bpy.ops.object.mode_set(mode='EDIT')
    for b in imported.data.edit_bones:b.use_connect=original_connect[b.name]
    bpy.ops.object.mode_set(mode='OBJECT')
    inverse={b.name:b.matrix_local.inverted() for b in imported.data.bones}
    checks=[]
    for clip in m['clips']:
        matches=[a for a in bpy.data.actions if a.name.endswith(clip['action'])]
        take_matches=[t for t in takes if t.endswith(clip['action'])]
        if len(matches)!=1 or len(take_matches)!=1:raise RuntimeError('Missing/ambiguous actual FBX take: '+clip['action'])
        action=matches[0];imported.animation_data.action=action;clip['fbxTake']=take_matches[0]
        frame_error=max(abs(float(action.frame_range[0])-clip['startFrame']),abs(float(action.frame_range[1])-clip['endFrame']))
        pose_errors=[]
        for name,frame,expected in reference:
            if name!=clip['action']:continue
            bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
            actual=[]
            for point,skin in zip(points,weights):
                position=Vector((0,0,0))
                for bone,weight in skin:position+=(imported.pose.bones[bone].matrix@inverse[bone]@point)*weight
                actual.append(imported.matrix_world@position)
            errors=np.linalg.norm(np.array(actual)-expected,axis=1)
            pose_errors.append({'frame':frame,'samples':len(points),'maxError':float(errors.max()),'p99Error':float(np.percentile(errors,99))})
        checks.append({'slot':clip['slot'],'take':clip['fbxTake'],'importedAction':action.name,
            'frameRange':list(action.frame_range),'maximumFrameError':frame_error,'poseErrors':pose_errors})
    maximum=max(p['maxError'] for c in checks for p in c['poseErrors'])
    root_transforms=[]
    for clip in m['clips']:
        imported.animation_data.action=next(a for a in bpy.data.actions if a.name.endswith(clip['action']))
        for frame in [0,clip['endFrame']//3,clip['endFrame']]:
            bpy.context.scene.frame_set(frame);root_transforms.append(imported.pose.bones['Root'].location.length)
    passed=(maximum<.0001 and set(bone_names)==set(inverse) and all(c['maximumFrameError']<.001 for c in checks)
        and max(root_transforms)<1e-5 and len(takes)==6 and sha256(source)==m['reviewBlendSha256'])
    verify={'motionFbx':str(output),'motionFbxSha256':sha256(output),'fbxVersion':version,'takes':takes,
        'boneNamesMatch':set(bone_names)==set(inverse),'maximumPoseSurfaceError':maximum,
        'maximumRootLocalTranslation':max(root_transforms),'clips':checks,'roundtripPass':passed,
        'blenderImporterAutoConnectionsRestored':automatic_connections,
        'note':'1000 original weighted surface samples per pose test animation-only FBX binds. Target Unity take bindings/playback still need validation.'}
    write_json(output.with_suffix('.roundtrip.json'),verify)
    m['motionFbxPath']=str(output);m['motionFbxSha256']=sha256(output);m['motionFbxRoundtripPass']=passed
    m['motionFbxRoundtripReport']=str(output.with_suffix('.roundtrip.json'))
    m['unityRigPathPreservation']='Identity Empty sibling MotionImportAnchor prevents single-armature model-root folding; actual Unity clip paths still require checking.'
    m['visualMotionReview']=str(Path(args.visual_review).resolve())
    write_json(manifest_path,m)
    print('BOSS_MOTION_FBX',m['label'],passed,'takes',takes,'maxPoseError',maximum,flush=True)
    if not passed:raise RuntimeError('Motion FBX roundtrip failed: do not import.')

if __name__=='__main__':main()
