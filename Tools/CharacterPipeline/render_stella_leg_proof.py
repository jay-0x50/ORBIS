"""Same-angle local patch feasibility renders. Never saves the input blend."""
import bpy,sys,json,hashlib
from pathlib import Path
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import add_area
STAGE=Path(__file__).resolve().parents[2]
SOURCE=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegEnvelope04/Stella_leg_envelope_proof.blend'
OUTPUT=SOURCE.parent/'RenderReview'
before=hashlib.sha256(SOURCE.read_bytes()).hexdigest();OUTPUT.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
body=next(o for o in bpy.context.scene.objects if o.type=='MESH' and not o.name.endswith('PROOF_ONLY'))
patches=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.endswith('PROOF_ONLY')]
scene=bpy.context.scene
for o in list(scene.objects):
    if o.type in {'LIGHT','CAMERA'}:bpy.data.objects.remove(o,do_unlink=True)
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1100;scene.render.resolution_y=1100
scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
scene.world=bpy.data.worlds.new('Leg proof neutral world');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.12,.12,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.25
scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
target=Vector((0,-.15,-.33));height=1.903
for name,direction,power in [('key',(-1,-1,2),180),('fill',(1,-1,.5),100),('rim',(0,2,1.3),180)]:
    add_area(scene,'Leg '+name,target+Vector(direction)*height,target,power*height*height,height)
data=bpy.data.cameras.new('Leg proof camera');camera=bpy.data.objects.new('Leg proof camera',data)
scene.collection.objects.link(camera);scene.camera=camera;data.type='ORTHO';data.ortho_scale=.31;data.clip_start=.001;data.clip_end=10
records=[]
for view,direction in [('Front',(0,-1,.01)),('ThreeQuarter',(.65,-1,.04))]:
    camera.location=target+Vector(direction).normalized()*1.6
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    for variant,proof in [('OriginalRig',False),('LocalEnvelope',True)]:
        body.hide_render=proof
        for p in patches:p.hide_set(False);p.hide_render=not proof
        path=OUTPUT/(variant+'_'+view+'.png');scene.render.filepath=str(path)
        bpy.ops.render.render(write_still=True)
        records.append({'variant':variant,'view':view,'path':str(path),'camera':list(camera.location),'target':list(target),'ortho_scale':data.ortho_scale})
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==before
(OUTPUT/'Comparison.json').write_text(json.dumps({'status':'Local surface proof; frame deliberately inspects patch interior, not a boundary-stitch acceptance',
    'source':str(SOURCE),'source_sha256_unchanged':before,'renders':records},indent=2),encoding='utf-8')
print('LEG_PROOF_RENDERED',flush=True)
