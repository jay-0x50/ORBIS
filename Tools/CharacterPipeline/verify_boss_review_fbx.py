"""CPU round-trip FBX integrity check; does not open Unity or save imported data."""
import argparse,json,sys
from pathlib import Path
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import array,sha256,write_json

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--fbx',required=True)
    parser.add_argument('--measured-mesh',required=True)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    path=Path(args.fbx).resolve();export=json.loads(path.with_suffix('.export.json').read_text(encoding='utf-8'))
    initial=sha256(path)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH'];rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
    if len(meshes)!=1 or len(rigs)!=1:raise RuntimeError('Unexpected mesh/rig count after FBX round trip.')
    obj,rig=meshes[0],rigs[0];mesh=obj.data;mesh.calc_loop_triangles()
    local=array(mesh.vertices,'co',(len(mesh.vertices),3));matrix=np.array(obj.matrix_world)
    world=local@matrix[:3,:3].T+matrix[:3,3]
    triangles=array(mesh.loop_triangles,'vertices',(len(mesh.loop_triangles),3),np.int32)
    bvh=BVHTree.FromPolygons(world.tolist(),triangles.tolist(),all_triangles=True)
    original=np.load(args.measured_mesh)['coordinates']+np.array(export['origin_contract']['uniform_offset_blender_xyz'])
    sample=original[np.linspace(0,len(original)-1,min(20000,len(original))).astype(np.int32)]
    error=np.array([bvh.find_nearest(tuple(p))[3] for p in sample])
    images=[{'name':i.name,'filepath':i.filepath,'resolved':bpy.path.abspath(i.filepath),
        'exists':Path(bpy.path.abspath(i.filepath)).is_file(),'size':list(i.size)} for i in bpy.data.images if i.source=='FILE']
    groups=[[(g.group,g.weight) for g in v.groups if g.weight>1e-6] for v in mesh.vertices]
    report={'fbx':str(path),'sha256':initial,'input_unchanged':sha256(path)==initial,
        'mesh_vertices':len(mesh.vertices),'triangles':len(triangles),'bones':len(rig.data.bones),
        'bone_names_match':set(export['bone_names'])==set(rig.data.bones.keys()),
        'root_bones':[b.name for b in rig.data.bones if b.parent is None],
        'world_bounds_min':world.min(axis=0).tolist(),'world_bounds_max':world.max(axis=0).tolist(),
        'world_surface_error':{'samples':len(sample),'p99':float(np.percentile(error,99)),'max':float(error.max())},
        'textures':images,'all_referenced_textures_exist':bool(images) and all(i['exists'] for i in images),
        'maximum_influences':max(map(len,groups)),'unweighted_vertices':sum(not group for group in groups),
        'max_weight_sum_error':max(abs(sum(w for _,w in group)-1) for group in groups),
        'scope':'Blender round-trip only; Unity Generic import/material mapping remains a separate check.'}
    report['original_texture_files_verified']=all(Path(t['path']).is_file() and sha256(Path(t['path']))==t['sha256'] for t in export['textures'])
    report['explicit_shader_channel_mapping_images']=sorted(set(t['image'] for t in export['textures'])-set(i['name'] for i in images))
    original_rig=Path(export['input']);original_rig_hash=sha256(original_rig)
    bpy.ops.wm.open_mainfile(filepath=str(original_rig),load_ui=False,use_scripts=False)
    report['original_material_graphs']=[]
    for material in bpy.data.materials:
        if not material.use_nodes:continue
        report['original_material_graphs'].append({'name':material.name,
            'image_nodes':[{'node':n.name,'image':n.image.name if n.image else None,
                'colorspace':n.image.colorspace_settings.name if n.image else None} for n in material.node_tree.nodes if n.type=='TEX_IMAGE'],
            'links':[{'from_node':l.from_node.name,'from_socket':l.from_socket.name,'to_node':l.to_node.name,'to_socket':l.to_socket.name} for l in material.node_tree.links]})
    report['original_rig_unchanged']=sha256(original_rig)==original_rig_hash
    report['shader_note']='FBX cannot reliably serialize arbitrary channel-separation node graphs. Use the original graph links and all three extracted original maps when constructing the Unity URP material.'
    report['roundtrip_numeric_pass']=report['bone_names_match'] and report['all_referenced_textures_exist'] and report['original_texture_files_verified'] and report['original_rig_unchanged'] and error.max()<.00001 and report['unweighted_vertices']==0 and report['maximum_influences']<=4
    write_json(path.with_suffix('.roundtrip.json'),report)
    print('BOSS_FBX_ROUNDTRIP',path.stem,report['roundtrip_numeric_pass'],'maxerror',error.max(),'textures',images,flush=True)

if __name__=='__main__':main()
