"""Explicitly authorized GPU review only: same-camera material Rest/Pose evidence."""
import argparse,json,math,sys
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import add_area,write_json,sha256

VIEWS={
 'WaterBoss':[
   ('full',(0,0,-.01),(1,-.5,.18),2.3,['rest','fins']),
   ('fins_close',(0,-.39,-.12),(1,-.2,-.18),.80,['rest','fins']),
   ('jaw_close',(0,-.74,-.06),(1,-.25,0),.42,['rest','jaw_open'])],
 'FireBoss':[
   ('full',(0,0,-.05),(-.8,-1,.16),2.4,['rest','RearLeg.R_flex']),
   ('rear_foot_close',(-.32,-.01,-.54),(-1,-.12,-.14),.64,['rest','RearLeg.R_flex'])],
 'WindBoss':[
   ('full',(0,0,-.02),(.6,-1,.12),2.3,['rest','wing_flex']),
   ('wing_close',(.32,-.22,.10),(1,-.7,.04),.8,['rest','wing_flex']),
   ('leg_close',(-.12,-.49,-.42),(-1,-.4,.05),.70,['rest','FrontLeg.R_flex'])],
 'LightningBoss':[
   ('full',(0,0,-.03),(.6,-1,.12),2.3,['rest','wing_flex']),
   ('wing_close',(.35,-.25,.16),(1,-.6,.08),.85,['rest','wing_flex']),
   ('leg_close',(-.18,-.12,-.30),(-1,-.2,-.05),.65,['rest','RearLeg.R_flex'])],
 'RockBoss':[
   ('full',(0,0,0),(.6,-1,.12),2.3,['rest','arm_flex']),
   ('arm_close',(.50,-.02,.05),(1,-1,.0),.87,['rest','arm_flex']),
   ('leg_close',(.27,-.10,-.57),(1,-1,.10),.90,['rest','Leg.L_flex'])],
}

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--rig-root',required=True)
    parser.add_argument('--landmarks-root',required=True);parser.add_argument('--output',required=True)
    parser.add_argument('--labels',nargs='+',required=True)
    parser.add_argument('--rig-map',help='Optional JSON mapping each label to a specific frozen rig path.')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    rig_root,landmark_root,output=map(Path,(args.rig_root,args.landmarks_root,args.output))
    rig_map=json.loads(Path(args.rig_map).read_text(encoding='utf-8-sig')) if args.rig_map else {}
    for label in args.labels:
        path=Path(rig_map[label]) if label in rig_map else rig_root/label/(label+'_rig_review.blend')
        initial=sha256(path)
        profile=json.loads((landmark_root/label/'landmarks.json').read_text(encoding='utf-8'))
        bpy.ops.wm.open_mainfile(filepath=str(path),load_ui=False,use_scripts=False)
        scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE')
        scene.render.engine='BLENDER_EEVEE'
        scene.render.resolution_x=1024;scene.render.resolution_y=1024;scene.render.resolution_percentage=100
        scene.render.image_settings.file_format='PNG'
        scene.world=bpy.data.worlds.new('Boss rig neutral studio');scene.world.use_nodes=True
        scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.10,.10,.10,1)
        scene.world.node_tree.nodes['Background'].inputs[1].default_value=.4
        scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
        target=Vector((0,0,0))
        for name,pos,energy in [('Key',(-3,-4,3),350),('Fill',(3,-1,2),250),('Low',(0,-2,-3),180),('Rim',(0,3,4),350)]:
            add_area(scene,name,Vector(pos),target,energy,3)
        cam=bpy.data.objects.new('Boss rig camera',bpy.data.cameras.new('Boss rig camera'))
        scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
        folder=output/label;folder.mkdir(parents=True,exist_ok=True)
        report={'input':str(path),'input_sha256':initial,'renders':[],'policy':'No mesh/material exclusions or file saves; identical camera and lighting within each Rest/Pose pair.'}
        for view,center,direction,scale,poses in VIEWS[label]:
            target=Vector(center);cam.location=target+Vector(direction).normalized()*5
            cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
            for pose in poses:
                for bone in rig.pose.bones:bone.rotation_mode='XYZ';bone.rotation_euler=(0,0,0)
                rotations=[]
                if pose!='rest':
                    rotations=next(p['rotations'] for p in profile['pose_probes'] if p['name']==pose)
                    for r in rotations:rig.pose.bones[r['bone']].rotation_euler['XYZ'.index(r['axis_local'])]=math.radians(r['degrees'])
                rig.update_tag();bpy.context.view_layer.update()
                dest=folder/(view+'_'+pose+'.png');scene.render.filepath=str(dest)
                bpy.ops.render.render(write_still=True)
                report['renders'].append({'view':view,'pose':pose,'rotations':rotations,'path':str(dest),
                    'camera_position':list(cam.location),'camera_target':list(target),'orthographic_scale':scale})
                report['input_unchanged']=initial==sha256(path);write_json(folder/'RenderedPoseEvidence.json',report)
                print('BOSS_RIG_RENDER',label,view,pose,flush=True)

if __name__=='__main__':main()
