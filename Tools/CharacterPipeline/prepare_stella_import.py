"""Prepare an isolated import bundle after byte-for-byte source-map verification.

This does not run Unity or change Assets. The mask is copied unmodified: it is
the supplied source's channel-packed map, not a newly baked Unity mask.
"""
from pathlib import Path
import hashlib
import json
import shutil

STAGE = Path(__file__).resolve().parents[2]
OUTPUT = STAGE / 'Tools/CharacterPipeline/PendingImport/StellaGrounded01'
EVIDENCE = STAGE / 'TestResults/CharacterPipeline/Processed/Stella400kDirect/MapAndReopenedTopologyVerification.json'
EXTRACTED = STAGE / 'TestResults/CharacterPipeline/Reduction/Stella500k/Textures'
FBX = STAGE / 'TestResults/CharacterPipeline/RigReview/StellaV3GroundedExport/Stella_grounded_object_offset.fbx'

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    verified = json.loads(EVIDENCE.read_text(encoding='utf-8'))
    assert verified['all_original_packed_map_bytes_and_color_spaces_exactly_preserved']
    assert verified['all_inputs_unchanged']
    original = verified['records']['original']
    repaired = verified['records']['repaired']
    assert sha(Path(original['path'])) == original['sha256']
    assert sha(Path(repaired['path'])) == repaired['sha256']
    expected = {m['name']: m for m in original['maps']}
    mapping = [('Image_0', 'BaseColor', 'sRGB'), ('Image_2', 'Normal', 'Non-Color'), ('Image_1', 'Mask', 'Non-Color')]
    records = []
    for image_name, role, colorspace in mapping:
        source = EXTRACTED / (image_name + '.jpg')
        assert sha(source) == expected[image_name]['packed_hashes'][0]
        assert source.stat().st_size == expected[image_name]['packed_bytes'][0]
        assert expected[image_name]['colorspace'] == colorspace
        records.append({'role': role, 'source_image': image_name, 'source': str(source),
            'file': 'Textures/' + role + '.jpg', 'sha256': sha(source),
            'bytes': source.stat().st_size, 'source_colorspace': colorspace,
            'source_dimensions': expected[image_name]['size'],
            'original_packed_bytes_identical': True})
    assert sha(FBX) == '92d453e67c67bf2a70f611894302f56d4497248fd56e00b9112ef13f9466f58b'
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / 'Textures').mkdir(exist_ok=True)
    for record in records:
        destination = OUTPUT / record['file']
        shutil.copyfile(record['source'], destination)
        assert sha(destination) == record['sha256']
    shutil.copyfile(FBX, OUTPUT / 'Stella.fbx')
    assert sha(OUTPUT / 'Stella.fbx') == sha(FBX)
    result = {'status': 'Staging only; no Assets write or Unity import performed',
        'source_blend': original['path'], 'source_blend_sha256': original['sha256'],
        'accepted_clean_blend': repaired['path'], 'accepted_clean_blend_sha256': repaired['sha256'],
        'source_map_verification': str(EVIDENCE), 'source_map_verification_sha256': sha(EVIDENCE),
        'fbx': {'file': 'Stella.fbx', 'source': str(FBX), 'sha256': sha(FBX), 'bytes': FBX.stat().st_size},
        'textures': records,
        'notes': ['Image_2 feeds the original Normal Map node. Image_1 feeds Separate Color; preserve its channel packing until material integration is explicitly reviewed.',
                  'No image recompression, pixel editing, rebake or generated replacement texture was used.',
                  'Source and cleaned blend SHA256 were rechecked before staging.']}
    (OUTPUT / 'SourceManifest.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False, indent=2))

if __name__ == '__main__':
    main()
