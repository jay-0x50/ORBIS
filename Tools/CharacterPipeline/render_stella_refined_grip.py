"""Read-only candidate closeups with exactly the StellaGrip01 camera fixture."""
from pathlib import Path
import argparse,hashlib,json,sys
import bpy
from mathutils import Matrix
sys.path.insert(0,str(Path(__file__).resolve().parent))
from review_hero_grip import make_studio,render_views
ROOT=Path(__file__).resolve().parents[2]/'TestResults/CharacterPipeline/RigReview'
parser=argparse.ArgumentParser();parser.add_argument('--run',default='StellaGrip02');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
assert args.run.isalnum() and args.run.startswith('StellaGrip')
record=json.loads((ROOT/'StellaGrip01/Stella_grip_report.json').read_text(encoding='utf-8'))
folder=ROOT/args.run;source=folder/'Stella_grip_refined.blend';out=folder/'RenderReview'
if out.exists():raise RuntimeError('Preserve previous render evidence')
out.mkdir();before=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
target=Matrix(record['candidate']['weapon_world']).translation;camera=make_studio(target,record['source_height'])
paths=render_views(out,'Stella',args.run,target,record['source_height'],camera)
assert before==hashlib.sha256(source.read_bytes()).hexdigest()
(out/'RenderProvenance.json').write_text(json.dumps({'source':str(source),'sourceSha256Unchanged':before,'camera':'Exact StellaGrip01 target, three directions, orthographic span and light fixture.','actualRenders':paths,'status':'Temporary source pose/socket review only; Unity transfer pending.'},indent=2),encoding='utf-8')
print('STELLA_GRIP_RENDERS_COMPLETE',flush=True)
