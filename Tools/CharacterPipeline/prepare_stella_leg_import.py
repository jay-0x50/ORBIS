"""Prepare a separate two-material Unity review bundle; no live Assets writes."""
from pathlib import Path
import argparse,hashlib,json,shutil
STAGE=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser();parser.add_argument('--version',choices=['06','12'],default='06');args=parser.parse_args()
EXPORT=STAGE/('TestResults/CharacterPipeline/RigReview/StellaLegIntegrated'+args.version+'GroundedExport')
REVIEW=STAGE/('TestResults/CharacterPipeline/RigReview/StellaLegIntegrated'+args.version)
ORIGINAL=STAGE/'Tools/CharacterPipeline/PendingImport/StellaGrounded01/Textures'
OUTPUT=STAGE/('Tools/CharacterPipeline/PendingImport/StellaLegIntegrated'+args.version)
OUTPUT.mkdir(exist_ok=True);(OUTPUT/'Textures').mkdir(exist_ok=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
expected={'BaseColor.jpg':'9978fbb55f0b2351e6b6376c229071a35ab666aec56de05c0c07d78982707062',
    'Normal.jpg':'5aac3660dcbb9ec10051c31d6cdea7c7a7cf8027605b4485d77bf1e62b72d371',
    'Mask.jpg':'8db8d5fef7616be3ce6bd04483473ea4ce2d61eb9a565c4906a0df64876a11e4'}
sources=[(EXPORT/'Stella_grounded_object_offset.fbx',OUTPUT/'Stella.fbx')]
for name,value in expected.items():
    source=ORIGINAL/name;assert sha(source)==value;sources.append((source,OUTPUT/'Textures'/name))
for name in ['BaseColor','Normal','Mask']:sources.append((REVIEW/('LegPatch_'+name+'.png'),OUTPUT/'Textures'/('LegPatch_'+name+'.png')))
files=[]
for source,target in sources:
    digest=sha(source)
    if target.exists() and sha(target)!=digest:raise RuntimeError('Refusing to overwrite different frozen candidate: '+str(target))
    shutil.copy2(source,target);assert sha(target)==digest;files.append({'source':str(source),'file':str(target),'sha256':digest})
report={'status':'Isolated Unity review candidate only; original/live hero catalog unchanged.',
    'model':'Stella','source_blend':str(REVIEW/'Stella_leg_integrated_trial.blend'),
    'source_blend_sha256':sha(REVIEW/'Stella_leg_integrated_trial.blend'),
    'source_mesh_counts':{'vertices':128416 if args.version=='06' else 111651,'triangles':274247 if args.version=='06' else 220138,'bones':66,'humanoid_contract_bones':52},
    'files':files,'materials':[
        {'slot':0,'fbx_material':'Material_0','base_color':'Textures/BaseColor.jpg','normal':'Textures/Normal.jpg','mask':'Textures/Mask.jpg','note':'Supplied original atlas bytes unchanged.'},
        {'slot':1,'fbx_material':'Stella local leg atlas','base_color':'Textures/LegPatch_BaseColor.png','normal':'Textures/LegPatch_Normal.png','mask':'Textures/LegPatch_Mask.png','note':'Local leg UV only, 800x784 with 8px gutters. Do not assign the original normal/atlas to this slot.'}],
    'mask_channels':{'G':'roughness','B':'metallic','Unity_smoothness_if_used':'1 - G'},
    'normal_map_note':'New map is target tangent-space, source tangent->world->target transfer. Import as NormalMap; compare actual tangent orientation in URP before adoption.',
    'export':json.loads((EXPORT/'Stella_grounded_export.json').read_text()),
    'acceptance_limits':[(('BentLegs retains a visible horizontal step at the upper/lower seam; KneeFlex retains upper original-thigh folds.') if args.version=='06' else 'Expanded integrated11 geometry removes the major upper step in actual Blender flex review. Narrow original lower strip requires matched Unity toon/outline review; new actual-target material transfer12 is not yet visually accepted.'),
        'Outer base seams are connected. Preserved gold sheet boundaries are intentionally open over the closed base, not watertight volumes.',
        'Inspect exact Integration.json pose metrics, actual Unity motion, source normals and weights before final use.',
        'Two material slots add base/outline draws; measure imported vertex count and cost. No claim of performance approval.']}
(OUTPUT/'SourceManifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('STELLA_LEG_REVIEW_BUNDLE_READY '+str(OUTPUT),flush=True)
