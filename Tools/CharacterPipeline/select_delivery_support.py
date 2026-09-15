"""Enumerate reviewed support code and bounded final evidence, without copying assets."""
import argparse
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--output', type=Path, required=True)
args = p.parse_args()
output = args.output.resolve()
assert output.is_relative_to(root / 'Tools/CharacterPipeline') and not output.exists()
selected = set()

def add(relative):
    path = root / relative
    assert path.is_file(), relative
    selected.add(path.relative_to(root).as_posix())
    if relative.startswith('Assets/') and not relative.endswith('.meta'):
        add(relative + '.meta')
    if relative.startswith('Assets/'):
        parent = path.parent
        while parent != root / 'Assets':
            meta = parent.with_name(parent.name + '.meta')
            assert meta.is_file(), str(meta)
            selected.add(meta.relative_to(root).as_posix())
            parent = parent.parent

# All Editor source here was compiled together; include its supporting authoring classes,
# not just a menu entry that would fail to compile without them. Never enumerate art candidates.
for folder in ['Assets/Orbis/Game/Editor', 'Assets/Orbis/Game/Tests/BossAnimationPlayMode',
               'Assets/Orbis/Game/Tests/CharacterEditMode', 'Assets/Orbis/Game/Tests/ExplorerArtPlayMode']:
    for path in (root / folder).rglob('*'):
        if path.suffix in ('.cs', '.asmdef'):
            add(path.relative_to(root).as_posix())
add('Assets/Orbis/Game/Tests/PlayMode/FieldBossSavedSceneTests.cs')

for path in (root / 'Tools/CharacterPipeline').iterdir():
    if path.is_file() and path.suffix in ('.py', '.ps1', '.md', '.json'):
        add(path.relative_to(root).as_posix())
for name in ['Final_Asset_Mapping_Draft.md', 'Field_Animation_Final.md', 'Locomotion_Final.md', 'Weapon_Grip_Final.md', 'Weapon_Grip_Review.md',
             'Stella_Face_Shading_Audit.md', 'Step_Progress.md']:
    add('Docs/CharacterPipeline/' + name)
for name in ['FinalMappingDraft/MappingEvidence.json', 'FinalMappingDraft/DocumentChecks.json',
             'ReviewStellaTerrain10/Review.md', 'ReviewStellaTerrain10/analysis.json',
             'PendingAnimation/ReviewGripTerrain03/Review.md', 'PendingAnimation/ReviewGripTerrain03/analysis.json',
             'PendingAnimation/ReviewPolarisDownhillAfter10/Review.md',
             'PendingAnimation/ReviewPolarisDownhillAfter10/analysis.json',
             'PendingAnimation/ReviewStellaDownhillAfter10/Review.md',
             'PendingAnimation/ReviewStellaDownhillAfter10/analysis.json',
             'DeliveryPreparation/LocomotionEvidenceClosure01.json',
             'PendingFieldFinalTest/SavedField03_Findings.md']:
    add('Tools/CharacterPipeline/' + name)

proof = 'TestResults/CharacterPipeline/'
for run in ['PolarisGripTerrain02', 'PolarisGripTerrain03']:
    add(proof + 'Motion/After/' + run + '/Polaris/Terrain/Slope24/frame_0158.jpg')
add(proof + 'Motion/After/StellaDownhillAfter10/Stella/Terrain/Downhill24/frame_0078.jpg')
for run in ['BossPoseClockTests01', 'FireRuntime01', 'WaterRuntime01', 'WindBossRuntime01',
            'RockBossRuntime01', 'LightningBossRuntime01', 'SavedField03', 'PolarisGripTerrain03',
            'Stella12TerrainAfter10', 'PolarisDownhillAfter10', 'StellaDownhillAfter10',
            'PolarisTerrainLifecycle01', 'StellaTerrainLifecycle01', 'CharacterFieldLogic01',
            'CharacterIslandRuntime01', 'CharacterStreaming01', 'CharacterBuildPolicy01']:
    add(proof + run + '.xml')
for file in ['CharacterFieldOcclusion01.json', 'CharacterReleaseBuild01.json',
             'FieldFinal/SavedField03/SavedFieldBosses.json',
             'HeroFinalBinding/FinalHeroes01/Review.json', 'HeroFinalBinding/FinalHeroes01/Commit.json',
             'HeroFinalBinding/FinalHeroes01/FinalBase01.json', 'HeroFinalBinding/FinalHeroes01/FinalAll01.json',
             'FieldIntegration/BossVisual05/Review.json', 'FieldIntegration/BossVisual05/Commit.json']:
    add(proof + file)
for boss in ['FireBoss', 'WaterBoss', 'WindBoss', 'RockBoss', 'LightningBoss']:
    for file in ['runtime.mp4', 'video_qa.json', 'ActualRuntimePlayback.json']:
        add(proof + 'BossRuntimePlayback/Runtime01/' + boss + '/' + file)
    for side in ['Before', 'After']:
        add(proof + 'FieldIntegration/BossVisual05/' + boss + '_Full_' + side + '.png')
for hero, run in [('Polaris','PolarisGripTerrain03'), ('Stella','Stella12TerrainAfter10')]:
    folder = proof + 'Motion/After/' + run + '/' + hero + '/'
    for file in ['motion.mp4', 'video_qa.json', 'source_provenance.json']:
        add(folder + file)
    for shot in ['Slope24/frame_0157.jpg', 'CrossSlopeTurns/frame_0288.jpg']:
        add(folder + 'Terrain/' + shot)
    downhill = proof + 'Motion/After/' + hero + 'DownhillAfter10/' + hero + '/Terrain/Downhill24/'
    for file in ['motion.mp4', 'video_qa.json']:
        add(downhill + file)
    add(proof + 'Motion/After/' + hero + 'DownhillAfter10/' + hero + '/source_provenance.json')
    add(proof + 'Motion/After/' + hero + 'TerrainLifecycle01/' + hero + '/TerrainLifecycle/terrain_lifecycle.json')
    grip_run = 'Transfer03' if hero == 'Polaris' else 'Stella12Transfer01'
    grip = proof + 'UnityGripReview/' + hero + '/' + grip_run + '/'
    add(grip + 'MeasuredTransfer.json')
    for angle in ['Front', 'Palm', 'Top']:
        add(grip + 'FittedHumanoid_' + angle + '.png')
for file in ['Launch.json', 'WorldPerformance.json', 'Player.log', 'Village.png', 'Forest.png',
             'Lakeside.png', 'HighlandStorm.png', 'IslandTravel.png']:
    add('TestResults/WorldDev/CharacterPackedSmoke01/' + file)

# Credits is intentionally excluded: it predates this task but is absent from the pinned
# baseline. Its reviewed append is transferred separately using the earlier content SHA.
output.parent.mkdir(parents=True, exist_ok=True)
with output.open('x', encoding='utf-8') as stream:
    json.dump(sorted(selected), stream, ensure_ascii=False, indent=2)
print(json.dumps({'files': len(selected), 'bytes': sum((root / f).stat().st_size for f in selected),
                  'output': str(output)}, ensure_ascii=False))
