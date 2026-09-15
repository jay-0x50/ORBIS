"""Build a separate mapped-material review candidate from the closed seam trial."""
from pathlib import Path
import hashlib,json
import bpy
import numpy as np
STAGE=Path(__file__).resolve().parents[2]
SOURCE=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegIntegrated04/Stella_leg_integrated_trial.blend'
MAPS=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegSurfaceMaps01'
OUTPUT=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegIntegrated05';OUTPUT.mkdir(exist_ok=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
before=sha(SOURCE);transfer=json.loads((MAPS/'Transfer.json').read_text())
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');mesh=obj.data
mat=mesh.materials[1];bsdf=mat.node_tree.nodes.get('Principled BSDF');pad=8;map_hashes={}
for kind in ['Mask','Normal']:
    arrays=[]
    for side in ['Left','Right']:
        path=MAPS/(side+'_LegPatch_'+kind+'.png');map_hashes[str(path)]=sha(path)
        img=bpy.data.images.load(str(path),check_existing=True);img.colorspace_settings.name='Non-Color';w,h=img.size
        p=np.empty(w*h*4,np.float32);img.pixels.foreach_get(p);p=p.reshape(h,w,4)
        p=np.concatenate([p[:,-pad:],p,p[:,:pad]],axis=1);arrays.append(np.pad(p,((pad,pad),(0,0),(0,0)),mode='edge'))
    combined=np.concatenate(arrays,axis=1)
    img=bpy.data.images.new('Stella leg '+kind,width=combined.shape[1],height=combined.shape[0],alpha=True)
    img.colorspace_settings.name='Non-Color';img.pixels.foreach_set(combined.ravel());img.filepath_raw=str(OUTPUT/('LegPatch_'+kind+'.png'));img.file_format='PNG';img.save();img.pack()
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=img
    if kind=='Mask':
        separate=mat.node_tree.nodes.new('ShaderNodeSeparateColor');separate.mode='RGB'
        mat.node_tree.links.new(node.outputs['Color'],separate.inputs['Color'])
        mat.node_tree.links.new(separate.outputs['Green'],bsdf.inputs['Roughness'])
        mat.node_tree.links.new(separate.outputs['Blue'],bsdf.inputs['Metallic'])
    else:
        normal=mat.node_tree.nodes.new('ShaderNodeNormalMap');normal.space='TANGENT';normal.inputs['Strength'].default_value=1
        mat.node_tree.links.new(node.outputs['Color'],normal.inputs['Color']);mat.node_tree.links.new(normal.outputs['Normal'],bsdf.inputs['Normal'])
co=np.empty((len(mesh.vertices),3),np.float32);mesh.vertices.foreach_get('co',co.ravel())
uv=np.empty((len(mesh.loops),2),np.float32);mesh.uv_layers.active.data.foreach_get('uv',uv.ravel())
bpy.context.preferences.filepaths.save_version=0;bpy.context.preferences.filepaths.file_preview_type='NONE'
output=OUTPUT/'Stella_leg_integrated_trial.blend';bpy.ops.wm.save_as_mainfile(filepath=str(output),relative_remap=True)
assert before==sha(SOURCE)
report={'source':str(SOURCE),'source_sha256_unchanged':before,'output':str(output),'output_sha256':sha(output),
    'mesh_coordinates_sha256':hashlib.sha256(co.tobytes()).hexdigest(),'mesh_uv_sha256':hashlib.sha256(uv.tobytes()).hexdigest(),
    'geometry_skin_bones_unchanged_from_integrated04':True,'source_map_hashes':map_hashes,
    'mask_channels':'Green=Roughness; Blue=Metallic, matching supplied material','normal_space':'Source tangent -> world -> local target tangent, inward source microhandle normals explicitly rejected/count in Transfer.json',
    'gutter_pixels':pad,'status':'Actual material seam/full pose visual review pending; no production import.'}
(OUTPUT/'MaterialIntegration.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('STELLA_PATCH_SURFACE_MAPS_APPLIED',flush=True)
