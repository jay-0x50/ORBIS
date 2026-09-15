"""Read-only whole-body/ornament/seam/deformation review of local integration."""
import hashlib,json,sys,argparse
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import render_studio
from rig_heroes import test_pose
STAGE=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser();parser.add_argument('--version',choices=['01','02','03','04','05','06','11','12'],default='06')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
label='StellaIntegrated'+args.version
SOURCE=STAGE/('TestResults/CharacterPipeline/RigReview/StellaLegIntegrated'+args.version+'/Stella_leg_integrated_trial.blend')
OUTPUT=SOURCE.parent/'RenderReview';OUTPUT.mkdir(exist_ok=True)
before=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),load_ui=False,use_scripts=False)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
audit=json.loads((STAGE/'TestResults/CharacterPipeline/SourceAudit/Stella.json').read_text(encoding='utf-8'))
test_pose(rig,'Relaxed')
render_studio(OUTPUT,{'label':label+'_Relaxed'},1024,fixed_bounds=audit['render_bounds'])
scene=bpy.context.scene;camera=scene.camera;records=[]
for pose,view,target,direction,span in [
    ('Relaxed','LegFront',(0,-.15,-.33),(0,-1,.02),.50),
    ('Relaxed','LegThreeQuarter',(0,-.15,-.33),(.65,-1,.04),.50),
    ('WalkContact','BentLegs',(0,-.15,-.33),(.65,-1,.08),.72),
    ('JointStress','KneeFlex',(0,-.15,-.33),(.65,-1,.12),1.04)]:
    test_pose(rig,pose);target=Vector(target)
    camera.location=target+Vector(direction).normalized()*3
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=span;path=OUTPUT/(label+'_'+view+'.png');scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)
    records.append({'pose':pose,'view':view,'path':str(path),'target':list(target),'orthographic_span':span})
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==before
(OUTPUT/'Review.json').write_text(json.dumps({'source':str(SOURCE),'source_sha256_unchanged':before,
    'scope':'Full character retained; inspect original metal geometry, patch-end overlap and bending. No source save or Unity acceptance.',
    'renders':records},indent=2),encoding='utf-8')
print('STELLA_INTEGRATED_REVIEW_COMPLETE',flush=True)
