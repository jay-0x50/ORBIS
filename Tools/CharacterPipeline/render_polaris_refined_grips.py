"""Compare two constrained grip candidates at the exact original review camera angles."""
from pathlib import Path
import hashlib,json,sys
import bpy
from mathutils import Matrix
sys.path.insert(0,str(Path(__file__).resolve().parent))
from review_hero_grip import make_studio,render_views
root=Path(__file__).resolve().parents[2]/'TestResults/CharacterPipeline/RigReview'
original=json.loads((root/'PolarisGrip01/Polaris_grip_report.json').read_text())
target=Matrix(original['candidate']['weapon_world']).translation
for run in ('PolarisGrip02','PolarisGrip03'):
    folder=root/run; source=folder/'Polaris_grip_refined.blend';output=folder/'RenderReview'
    assert not output.exists(), 'Preserve rendered evidence'
    output.mkdir()
    before=hashlib.sha256(source.read_bytes()).hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
    camera=make_studio(target,original['source_height'])
    paths=render_views(output,'Polaris',run,target,original['source_height'],camera)
    assert before==hashlib.sha256(source.read_bytes()).hexdigest()
    (output/'RenderProvenance.json').write_text(json.dumps({'source':str(source),'sha256Unchanged':before,
        'camera':'Exactly same target/directions/orthographic scale as Grip01, even if sword socket moved.',
        'actualRenders':paths,'status':'No artistic approval implied by numerical collision score.'},indent=2),encoding='utf-8')
