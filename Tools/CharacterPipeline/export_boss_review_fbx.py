"""Export an explicitly selected Generic rig to a separate FBX review folder.

Run only after the matching pose-render review. This does not import or publish
into Unity, author animations, move root geometry, or modify the input blend.
"""
import argparse,hashlib,json,sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import sha256,write_json,array
from rig_bosses import geometry_fingerprint


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--rig',required=True);parser.add_argument('--expected-sha256',required=True)
    parser.add_argument('--output',required=True);parser.add_argument('--visual-review-record',required=True)
    parser.add_argument('--origin',choices=['standing_feet','bounds_floor','swim_body'],default='standing_feet')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    source,output=Path(args.rig).resolve(),Path(args.output).resolve()
    record=Path(args.visual_review_record).resolve()
    if not record.is_file():raise RuntimeError('The actual pose-render review record must exist.')
    visual_record=json.loads(record.read_text(encoding='utf-8-sig'))
    if visual_record.get('rigSha256')!=args.expected_sha256 or not visual_record.get('status','').startswith('Accepted for rig-only FBX review'):
        raise RuntimeError('Pose review must explicitly accept this exact rig hash for FBX review.')
    if source==output or 'Assets' in output.parts or output.suffix.lower()!='.fbx':
        raise RuntimeError('Export must be a separate .fbx review artifact outside Assets.')
    if output.exists():raise RuntimeError('Use a new export folder; do not overwrite a reviewed FBX.')
    if sha256(source)!=args.expected_sha256:raise RuntimeError('Selected rig hash mismatch.')
    output.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
    if len(meshes)!=1 or len(rigs)!=1:raise RuntimeError('Expected one reviewed skinned mesh and one Generic armature.')
    mesh,rig=meshes[0],rigs[0];before=geometry_fingerprint(mesh.data)
    for bone in rig.pose.bones:
        bone.rotation_mode='XYZ';bone.rotation_euler=(0,0,0);bone.location=(0,0,0);bone.scale=(1,1,1)
    bpy.context.view_layer.update()
    original_positions=array(mesh.data.vertices,'co',(len(mesh.data.vertices),3))
    rig_report_path=source.with_name('rig_review.json')
    rig_record=json.loads(rig_report_path.read_text(encoding='utf-8')) if rig_report_path.exists() else {}
    weight_record=rig_record.get('weights',{})
    if (not rig_record or weight_record.get('nonfinite_weights',1) or weight_record.get('unweighted_vertices',1)
            or not np.isfinite(weight_record.get('max_weight_sum_error',float('nan')))):
        raise RuntimeError('Missing or invalid rig weight validation; do not export.')
    measured_soles=[f['measured_sole_rest_local'] for f in rig_record.get('foot_metadata',[]) if 'measured_sole_rest_local' in f]
    if args.origin=='swim_body':
        if 'Body' not in rig.data.bones:raise RuntimeError('Swim center requires the explicit Body bone.')
        reference_z=float(rig.data.bones['Body'].head_local.z)
        origin_note='Body rest anchor is Z=0; this is a swimming anchor, not a ground contact.'
    elif args.origin=='standing_feet':
        if not measured_soles:raise RuntimeError('Standing origin requires measured physical foot soles.')
        reference_z=min(float(p[2]) for p in measured_soles)
        origin_note='Lowest measured physical foot sole defines support plane Z=0; other sole residuals are explicit. Idle grounding and tail clearance are future pose adjustments, never raw mesh edits.'
    else:
        reference_z=float(original_positions[:,2].min())
        origin_note='Mesh bounds minimum is Z=0. Actual sole offsets are separate; low tail tips are not assumed to be feet.'
    offset=Vector((0,0,-reference_z))
    mesh.data.vertices.foreach_set('co',(original_positions+np.array(offset,dtype=np.float32)).ravel())
    bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT')
    bone_vectors_before={b.name:(b.tail-b.head).copy() for b in rig.data.edit_bones}
    for bone in rig.data.edit_bones:bone.head+=offset;bone.tail+=offset
    bone_vector_error=max((b.tail-b.head-bone_vectors_before[b.name]).length for b in rig.data.edit_bones)
    bpy.ops.object.mode_set(mode='OBJECT');mesh.data.update();bpy.context.view_layer.update()
    shifted=array(mesh.data.vertices,'co',(len(mesh.data.vertices),3))
    uniform_offset_error=float(np.abs(shifted-original_positions-np.array(offset)).max())
    textures=[]
    for image in bpy.data.images:
        if image.source!='FILE':continue
        packed=image.packed_file
        if packed is None:raise RuntimeError('Expected original packed texture: '+image.name)
        data=packed.data
        extension='.png' if data.startswith(b'\x89PNG') else '.jpg' if data.startswith(b'\xff\xd8') else None
        if extension is None:raise RuntimeError('Unrecognized original texture bytes; no format conversion allowed.')
        destination=output.parent/'Textures'/(image.name+extension)
        destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(data)
        image.filepath=str(destination)
        textures.append({'image':image.name,'path':str(destination),'sha256':hashlib.sha256(data).hexdigest(),
            'colorspace':image.colorspace_settings.name,'bytes':len(data)})
    bpy.ops.object.select_all(action='DESELECT');mesh.select_set(True);rig.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(output),use_selection=True,
        object_types={'ARMATURE','MESH'},global_scale=1.0,apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_NONE',axis_forward='-Z',axis_up='Y',
        bake_space_transform=False,use_mesh_modifiers=False,mesh_smooth_type='OFF',
        add_leaf_bones=False,use_armature_deform_only=False,bake_anim=False,
        path_mode='RELATIVE',embed_textures=False)
    after=geometry_fingerprint(mesh.data)
    surface_data_unchanged=all(before[k]==after[k] for k in before if k!='positions')
    foot_offsets=[]
    if rig_record:
        for foot in rig_record.get('foot_metadata',[]):
            if 'measured_sole_rest_local' in foot:
                foot_offsets.append({'branch':foot['branch'],'source_sole':foot['measured_sole_rest_local'],
                    'export_sole':(np.array(foot['measured_sole_rest_local'])+np.array(offset)).tolist(),
                    'ankle_to_sole_unchanged':foot['ankle_to_measured_sole_local']})
    report={'input':str(source),'input_sha256':args.expected_sha256,'input_unchanged':sha256(source)==args.expected_sha256,
        'visual_review_record':str(record),'output':str(output),'output_sha256':sha256(output),
        'geometry_preserved_up_to_recorded_uniform_translation':uniform_offset_error<1e-6,
        'topology_uv_material_indices_preserved':surface_data_unchanged,'textures':textures,
        'origin_contract':{'mode':args.origin,'source_reference_z':reference_z,'uniform_offset_blender_xyz':list(offset),
            'source_mesh_bounds_min_z':float(original_positions[:,2].min()),
            'export_mesh_bounds_min_z':float(shifted[:,2].min()),
            'measured_support_plane_method':'lowest_of_measured_physical_soles' if args.origin=='standing_feet' else 'not_standing_feet',
            'below_support_extent':max(0,-float(shifted[:,2].min())) if args.origin=='standing_feet' else None,
            'uniform_offset_error_max':uniform_offset_error,'bone_vector_error_max':bone_vector_error,
            'note':origin_note,'animation_contract':'No root local translation or root-motion animation in this rig-only export.',
            'foot_offsets':foot_offsets},
        'bone_names':[b.name for b in rig.data.bones],
        'root_bones':[b.name for b in rig.data.bones if b.parent is None],
        'policy':'Generic rig-only FBX review. Export-only uniform origin translation preserves shape/UV/relative bones; source untouched. No animations, AnimationEvent damage, Unity import or new AI.',
        'import_validation':'Unity Generic Avatar, axes, bind poses, scale and texture mapping still require target-engine validation.'}
    write_json(output.with_suffix('.export.json'),report)
    if not report['input_unchanged'] or not surface_data_unchanged or uniform_offset_error>1e-6 or bone_vector_error>1e-6:
        raise RuntimeError('Export integrity check failed.')
    print('BOSS_REVIEW_FBX_EXPORTED',str(output),flush=True)

if __name__=='__main__':main()
