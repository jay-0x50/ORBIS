"""Stage immutable reviewed Generic FBXs and exact original maps. Run with Unity closed."""
from pathlib import Path
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[2]
review = root / 'TestResults/CharacterPipeline/BossFBXReview'
manifest = json.loads((review / 'ReviewedRigManifest.json').read_text(encoding='utf-8-sig'))
roles = {'Image_0': 'BaseColor', 'Image_1': 'Mask', 'Image_2': 'Normal'}

def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

work = []
for entry in manifest['entries']:
    name = entry['label']
    if name not in ('RockBoss', 'WindBoss', 'LightningBoss'):
        continue
    export = json.loads((review/name/(name+'.export.json')).read_text(encoding='utf-8-sig'))
    source = Path(entry['fbx'])
    assert digest(source) == entry['sha256'] == export['output_sha256'], name
    assert entry['roundtripPass'] and export['input_unchanged']
    assert export['topology_uv_material_indices_preserved']
    destination = root / 'Assets/Orbis/Game/Characters/BossCandidates/Import01' / name
    assert not destination.exists(), 'Preserve previous candidate: '+str(destination)
    textures = {t['image']: t for t in export['textures']}
    assert set(textures) == set(roles)
    for image, data in textures.items():
        assert digest(data['path']) == data['sha256'], name+' '+image
    work.append((name,source,destination,textures,entry))
assert len(work) == 3
for name,source,destination,textures,entry in work:
    (destination/'Textures').mkdir(parents=True)
    shutil.copy2(source,destination/source.name)
    for image,role in roles.items():
        shutil.copy2(textures[image]['path'],destination/'Textures'/(role+'.jpg'))
    (destination/'SourceManifest.json').write_text(json.dumps({
        'reviewedRig':entry, 'verifiedOriginalTextureRoles':roles,
        'note':'Image_1 has G roughness and B metallic; no AO input was connected in the supplied material. Rest source is immutable.'
    },indent=2),encoding='utf-8')
    print(name,entry['sha256'])
