"""Rebuild the reference-driven head, torso and clothing from immutable inputs.

blender --background --python Tools/Blender/refine_explorer_anatomy.py -- --fast
Add --export only after preview review. This writes Sources/*.blend and
AnatomyExports/*.fbx, never directly into the Unity Assets tree.
"""
import bpy,json,sys,math,importlib.util
from pathlib import Path
from mathutils import Vector,Quaternion

ROOT=Path(__file__).resolve().parents[2];TOOLS=ROOT/'Tools/Blender'
BASE=TOOLS/'Sources/AnatomyBaseSources';RESULTS=ROOT/'TestResults'
TEXTURES=ROOT/'Assets/Orbis/Game/Island/Textures/Explorers';EXPORT='--export' in sys.argv
bpy.context.preferences.filepaths.save_version=0
bpy.context.preferences.filepaths.file_preview_type='NONE'

def module(name):
    spec=importlib.util.spec_from_file_location(name,str(TOOLS/(name+'.py')));value=importlib.util.module_from_spec(spec);spec.loader.exec_module(value);return value

body=module('tailor_explorer_body');head=module('refine_explorer_head')

def aim(obj,point):obj.rotation_euler=(Vector(point)-obj.location).to_track_quat('-Z','Y').to_euler()

def render(name,rig):
    for side,sign in (('Left',1),('Right',-1)):
        bone=rig.pose.bones[side+'UpperArm'];bone.rotation_mode='QUATERNION';rest=bone.bone.matrix_local.to_quaternion()
        bone.rotation_quaternion=rest.inverted()@Quaternion(Vector((0,1,0)),sign*math.radians(75))@rest
    bpy.context.view_layer.update();scene=bpy.context.scene;camera=scene.camera
    scene.render.engine='CYCLES';scene.cycles.samples=8 if '--fast' in sys.argv else 24;scene.cycles.use_denoising=True
    scene.render.threads_mode='FIXED';scene.render.threads=12;scene.render.resolution_percentage=100
    scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'
    bones=rig.data.bones;head_pos=rig.matrix_world@bones['Head'].head_local
    face_look=head_pos+Vector((0,-.02,.08))
    views=[('Front',(0,-4,.89),(0,0,.86),1.92),('ThreeQuarter',(2,-4,1.17),(0,0,.86),1.98),
           ('Profile',(4,0,.89),(0,0,.86),1.98),('Back',(0,4,.89),(0,0,.86),1.98),
           ('Face',(0,-3,face_look.z),tuple(face_look),.36),('FaceProfile',(3,0,face_look.z),tuple(face_look),.39)]
    if '--preview' in sys.argv:views=[views[0],views[1],views[4]]
    for suffix,location,look,size in views:
        camera.location=location;aim(camera,look);camera.data.type='ORTHO';camera.data.ortho_scale=size
        scene.render.resolution_x=650 if '--fast' in sys.argv else 1100
        scene.render.resolution_y=round(scene.render.resolution_x*(1.3 if not suffix.startswith('Face') else 1))
        scene.render.filepath=str(RESULTS/('AnatomyStudio_'+name+'_'+suffix+'.png'));bpy.ops.render.render(write_still=True)

def run(name,female):
    bpy.ops.wm.open_mainfile(filepath=str(BASE/(name+'.blend')))
    rig=bpy.data.objects[name+'_Rig'];rig.data.pose_position='POSE'
    for b in rig.pose.bones:b.rotation_mode='QUATERNION';b.rotation_quaternion=Quaternion()
    before={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
    head_report=head.apply(name,female,rig);body_report=body.apply(name,female,rig,TEXTURES)
    after={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
    assert before==after and len(after)==52,'Humanoid rest skeleton must be preserved.'
    meshes=[o for o in bpy.data.objects if o.type=='MESH' and o.parent==rig]
    for obj in meshes:
        assert obj.data.uv_layers.active is not None or all(m.name not in ('EX_Face','EX_Hair','EX_TailoredIvory','EX_TailoredNavy') for m in obj.data.materials)
        for vertex in obj.data.vertices:assert vertex.groups and abs(sum(g.weight for g in vertex.groups)-1)<.0001
    points=[obj.matrix_world@v.co for obj in meshes for v in obj.data.vertices]
    low=min(p.z for p in points);high=max(p.z for p in points)
    report={'name':name,'boneCount':52,'boneRestTransformsUnchanged':True,'sourceHeight':high-low,
            'meshCount':len(meshes),'triangles':sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons),
            'head':head_report,'body':body_report,'source':'Tools/Blender/Sources/AnatomyBaseSources/'+name+'.blend','exported':EXPORT}
    if EXPORT:
        for image in bpy.data.images:
            if image.source=='FILE' and 'BaseColor' in image.name:
                path=TEXTURES/Path(image.filepath.replace('\\','/')).name
                if path.exists():image.pack();image.filepath='//../../../Assets/Orbis/Game/Island/Textures/Explorers/'+path.name
        bpy.ops.wm.save_as_mainfile(filepath=str(TOOLS/'Sources'/(name+'.blend')),relative_remap=False)
        out=TOOLS/'AnatomyExports';out.mkdir(exist_ok=True)
        bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
        for obj in meshes:obj.select_set(True)
        bpy.context.view_layer.objects.active=rig
        bpy.ops.export_scene.fbx(filepath=str(out/(name+'.fbx')),use_selection=True,object_types={'MESH','ARMATURE'},
            axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_mesh_modifiers=True,
            mesh_smooth_type='FACE',add_leaf_bones=False,primary_bone_axis='Y',secondary_bone_axis='X',bake_anim=False,path_mode='AUTO')
    (RESULTS/('Anatomy_'+name+'_Model.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('ORBIS_ANATOMY='+json.dumps(report),flush=True);render(name,rig)

for name,female in (('Stella',True),('Polaris',False)):
    if '--only-stella' in sys.argv and not female:continue
    if '--only-polaris' in sys.argv and female:continue
    run(name,female)
