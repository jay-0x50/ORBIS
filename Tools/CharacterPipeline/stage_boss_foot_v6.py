"""Stage reviewed foot-weight revision in a fresh candidate folder; run with Unity closed.

Defaults to verification only. --apply creates explicitly requested labels under Import02.
No previous candidate, source blend, rig, material or Field scene is modified.
"""
import argparse,hashlib,json,shutil
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--labels',nargs='+',required=True)
parser.add_argument('--run',default='Import02')
parser.add_argument('--manifest',help='Optional explicit reviewed revision manifest; default remains FootV6.')
parser.add_argument('--apply',action='store_true')
args=parser.parse_args()
assert args.run.replace('_','').isalnum() and args.run!='Import01'
root=Path(__file__).resolve().parents[2]
review=root/'TestResults/CharacterPipeline/BossFBXReview/FootV6'
manifest_path=Path(args.manifest) if args.manifest else review/'ReviewedRigManifest.json'
manifest=json.loads(manifest_path.read_text(encoding='utf-8-sig'))
entries={e['label']:e for e in manifest['entries']}
roles={'Image_0':'BaseColor','Image_1':'Mask','Image_2':'Normal'}
def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
work=[]
for name in args.labels:
    entry=entries[name]; source=Path(entry['fbx'])
    assert entry['roundtripPass'] and digest(source)==entry['sha256']
    assert digest(entry['rig'])==entry['rigSha256']
    textures={t['image']:t for t in entry['textures']}; assert set(textures)==set(roles)
    for data in textures.values():assert digest(data['path'])==data['sha256']
    destination=root/'Assets/Orbis/Game/Characters/BossCandidates'/args.run/name
    assert not destination.exists(), 'Preserve previous candidate: '+str(destination)
    work.append((name,source,destination,textures,entry))
for name,source,destination,textures,entry in work:
    if args.apply:
        (destination/'Textures').mkdir(parents=True)
        shutil.copy2(source,destination/source.name)
        for image,role in roles.items():shutil.copy2(textures[image]['path'],destination/'Textures'/(role+'.jpg'))
        (destination/'SourceManifest.json').write_text(json.dumps({'reviewedRig':entry,
            'verifiedOriginalTextureRoles':roles,
            'sourceReviewManifest':str(manifest_path),
            'note':'Image_1 G=roughness/B=metallic; no connected AO source. Original support plane and bone axes preserved; exact local skin revision is recorded in reviewedRig.'},indent=2),encoding='utf-8')
    print('STAGED' if args.apply else 'VERIFIED_ONLY',name,entry['sha256'])
