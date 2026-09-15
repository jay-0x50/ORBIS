"""Actual identical-camera Death60 shoulder/pelvis comparison, no source saves."""
import argparse,json,sys
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import add_area,sha256,write_json

parser=argparse.ArgumentParser();parser.add_argument('--baseline',required=True)
parser.add_argument('--candidate',required=True);parser.add_argument('--output',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
output=Path(args.output);output.mkdir(parents=True,exist_ok=True)
record={'renders':[],'method':'Actual unchanged material EEVEE samples; same world-space camera/light and authored Dead60 frame.'}
for state,path in [('Before',args.baseline),('After',args.candidate)]:
    m=json.loads(Path(path).read_text(encoding='utf-8-sig'));source=Path(m['reviewBlend'])
    assert sha256(source)==m['reviewBlendSha256']
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
    scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE')
    scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=800;scene.render.resolution_y=800
    scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
    scene.world=bpy.data.worlds.new('Neutral detail review');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.10,.10,.10,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.4
    scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
    for name,pos,energy in [('Key',(-3,-4,3),350),('Fill',(3,-1,2),250),('Low',(0,-2,-3),180),('Rim',(0,3,4),350)]:
        add_area(scene,name,Vector(pos),Vector((0,0,0)),energy,3)
    cam=bpy.data.objects.new('Detail review camera',bpy.data.cameras.new('Detail review camera'))
    scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
    rig.animation_data.action=bpy.data.actions['RockBoss_Dead'];scene.frame_set(60);bpy.context.view_layer.update()
    for part,center,direction,scale in [('Shoulder',(.55,.09,.36),(1,.28,.08),.4),
                                       ('Pelvis',(.205,.04,-.015),(.2,-1,.1),.4)]:
        center=Vector(center);cam.location=center+Vector(direction).normalized()*4
        cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
        target=output/(part+'_'+state+'.png');assert not target.exists()
        scene.render.filepath=str(target);bpy.ops.render.render(write_still=True)
        record['renders'].append({'state':state,'part':part,'frame':60,'path':str(target),
            'sourceSha256':m['reviewBlendSha256'],'sourceUnchanged':sha256(source)==m['reviewBlendSha256']})
        write_json(output/'DetailComparisonEvidence.json',record)
        print('ROCK_DETAIL_RENDER',state,part,flush=True)
