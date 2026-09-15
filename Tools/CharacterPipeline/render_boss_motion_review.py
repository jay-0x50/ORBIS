"""GPU-window-only actual motion key-pose evidence. No source writes."""
import argparse,json,sys
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import add_area,sha256,write_json

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--manifest',required=True)
    parser.add_argument('--output',required=True)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    manifest=json.loads(Path(args.manifest).read_text(encoding='utf-8-sig'))
    source=Path(manifest['reviewBlend']);initial=sha256(source)
    if initial!=manifest['reviewBlendSha256']:raise RuntimeError('Motion candidate changed.')
    output=Path(args.output);output.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
    scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE')
    scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=800;scene.render.resolution_y=800
    scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
    scene.world=bpy.data.worlds.new('Neutral motion review');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.10,.10,.10,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.4
    scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
    for name,pos,energy in [('Key',(-3,-4,3),350),('Fill',(3,-1,2),250),('Low',(0,-2,-3),180),('Rim',(0,3,4),350)]:
        add_area(scene,name,Vector(pos),Vector((0,0,0)),energy,3)
    cam=bpy.data.objects.new('Motion review camera',bpy.data.cameras.new('Motion review camera'))
    scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
    direction=(1,-.48,.14) if manifest['label']=='WaterBoss' else (.65,-1,.16)
    center=Vector((0,0,-.04));cam.location=center+Vector(direction).normalized()*5
    cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=2.35 if manifest['label']=='WaterBoss' else 2.55
    record={'motionManifest':args.manifest,'sourceSha256':initial,'renders':[],
        'policy':'Actual EEVEE material samples, same fixed camera and lighting, no geometry/material exclusions or source writes.'}
    for clip in manifest['clips']:
        rig.animation_data.action=bpy.data.actions[clip['action']]
        end=clip['endFrame'];frames=sorted(set([0,end//4,end//3,end//2,end*3//4,end]))
        for frame in frames:
            scene.frame_set(frame);bpy.context.view_layer.update()
            target=output/(clip['slot']+'_'+str(frame).zfill(3)+'.png');scene.render.filepath=str(target)
            bpy.ops.render.render(write_still=True)
            record['renders'].append({'slot':clip['slot'],'frame':frame,'path':str(target)})
            record['sourceUnchanged']=sha256(source)==initial
            write_json(output/'RenderedMotionEvidence.json',record)
            print('BOSS_MOTION_RENDER',manifest['label'],clip['slot'],frame,flush=True)

if __name__=='__main__':main()
