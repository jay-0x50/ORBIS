"""Continue the two explicitly reviewed four-leg/two-wing candidates.

Runs Blender CPU exports serially, never edits source blends or previous FBXs.
The visual observations below refer to the actual six-slot contact sheets read
on 2026-09-15. They permit engine review, not final art/Field acceptance.
"""
import hashlib, json, subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'TestResults/CharacterPipeline'
TOOLS=ROOT/'Tools/CharacterPipeline'
BLENDER=Path('C:/Program Files/Blender Foundation/Blender 5.2/blender.exe')

def read(p): return json.loads(Path(p).read_text(encoding='utf-8-sig'))
def write(p,d): Path(p).write_text(json.dumps(d,indent=2),encoding='utf-8')
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def run(script,args,log):
    command=[str(BLENDER),'--background','--factory-startup','--disable-autoexec',
             '--python-exit-code','1','--python',str(TOOLS/script),'--',*map(str,args)]
    with Path(log).open('w',encoding='utf-8') as stream:
        subprocess.run(command,cwd=ROOT,stdout=stream,stderr=subprocess.STDOUT,check=True)
    print(script,Path(log).name,'PASS',flush=True)

entries=[]
for label,version in [('WindBoss',5),('LightningBoss',4)]:
    motion=BASE/f'BossMotionReviewV{version}'/label
    manifest_path=motion/'motion_manifest.json';m=read(manifest_path)
    rig=Path(m['restRig']);assert sha(rig)==m['restRigSha256']
    contract=read(rig.parent/'FootWeightRestContractVerification.json')
    assert contract['exactRestContractPreserved'] and contract['oldSourceUnchanged']
    review=rig.parent/'VisualPoseReview.json'
    observed={slot:sha(motion/'Renders'/f'{slot}_Sheet.png') for slot in ['Idle','Move','Attack','Hurt','Exposed','Dead']}
    write(review,{'rigSha256':sha(rig),'status':'Accepted for rig-only FBX review; continuous engine QA pending',
        'observedMaterialMotionSheets':observed,
        'observations':['Original four legs and two wings preserved.',
          'No obvious new surface tear in the six sampled pose sheets.',
          'Wing/neck motion is restrained; Dead is a settling crouch, not a full rollover.',
          'Foot support and transient motion still require actual Unity playback.'],
        'restContractVerification':str(rig.parent/'FootWeightRestContractVerification.json')})
    out=BASE/'BossFBXReview/FootV6'/label;out.mkdir(parents=True,exist_ok=True)
    fbx=out/(label+'.fbx')
    if not fbx.exists():
        run('export_boss_review_fbx.py',['--rig',rig,'--expected-sha256',sha(rig),'--output',fbx,
            '--visual-review-record',review,'--origin','standing_feet'],out/'export.log')
    run('verify_boss_review_fbx.py',['--fbx',fbx,'--measured-mesh',BASE/'BossRigReview'/label/'measured_mesh.npz'],out/'roundtrip.log')
    export=read(fbx.with_suffix('.export.json'));rt=read(fbx.with_suffix('.roundtrip.json'))
    assert rt['roundtrip_numeric_pass'] and export['input_sha256']==sha(rig)
    assert export['origin_contract']['uniform_offset_blender_xyz']==m['originContract']['uniform_offset_blender_xyz']
    m['restFbx']=str(fbx);m['restFbxSha256']=sha(fbx);write(manifest_path,m)
    motion_review=motion/'VisualMotionReview.json'
    write(motion_review,{'motionBlendSha256':m['reviewBlendSha256'],'acceptedForMotionFbxReview':True,
        'actualMaterialSheets':observed,'scope':'Six actual Blender material pose sheets inspected; candidate approved only for target-engine review.',
        'limitations':['No continuous Unity playback acceptance yet.','Dead is a crouched final pose.']})
    motion_fbx=motion/(label+'_Motions.fbx')
    if not motion_fbx.exists():
        run('export_boss_motion_fbx.py',['--manifest',manifest_path,'--visual-review',motion_review,'--output',motion_fbx],motion/'motion_export.log')
    run('inspect_boss_motion_edges.py',['--manifest',manifest_path],motion/'edge_warnings.log')
    assert read(manifest_path)['motionFbxRoundtripPass']
    entries.append({'label':label,'fbx':str(fbx),'sha256':sha(fbx),'rig':str(rig),'rigSha256':sha(rig),
        'boneCount':rt['bones'],'origin':export['origin_contract'],'roundtripPass':True,
        'manualShaderChannelImages':rt['explicit_shader_channel_mapping_images'],'textures':export['textures'],
        'exportReport':str(fbx.with_suffix('.export.json')),'roundtripReport':str(fbx.with_suffix('.roundtrip.json')),
        'visualReview':str(review),'restContractVerification':str(rig.parent/'FootWeightRestContractVerification.json'),
        'engineImport':'Pending Import02; preserve Import01','animationClips':'Six authored slots; engine playback pending'})
manifest=BASE/'BossFBXReview/FootV6/ReviewedRigManifest.json'
data=read(manifest)
assert not set(e['label'] for e in data['entries']).intersection(e['label'] for e in entries)
data['entries'].extend(entries);write(manifest,data)
print('WINGED_BOSS_REST_AND_MOTION_EXPORTS_VERIFIED',flush=True)
