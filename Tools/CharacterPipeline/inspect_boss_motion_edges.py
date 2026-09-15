"""Read-only localization of actual deformed-edge warnings; not an acceptance gate."""
import argparse,json,sys
from pathlib import Path
import bpy,numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import sha256,array,write_json
from rig_bosses import evaluated_positions

parser=argparse.ArgumentParser();parser.add_argument('--manifest',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
m=json.loads(Path(args.manifest).read_text(encoding='utf-8-sig'))
assert sha256(m['reviewBlend'])==m['reviewBlendSha256']
bpy.ops.wm.open_mainfile(filepath=m['reviewBlend'],load_ui=False,use_scripts=False)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH')
rest=array(obj.data.vertices,'co',(len(obj.data.vertices),3))
edges=array(obj.data.edges,'vertices',(len(obj.data.edges),2),np.int32)
lengths=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)
records=[]
for slot in ['Move','Attack','Dead']:
    clip=next(c for c in m['clips'] if c['slot']==slot)
    frame=max(clip['deformationSamples'],key=lambda s:s['maximumEdgeElongation'])['frame']
    rig.animation_data.action=bpy.data.actions[clip['action']]
    bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
    pose=evaluated_positions(obj)
    posed_lengths=np.linalg.norm(pose[edges[:,0]]-pose[edges[:,1]],axis=1)
    delta=posed_lengths-lengths
    samples=[]
    for i in np.argsort(delta)[-12:][::-1]:
        a,b=map(int,edges[i]);samples.append({'vertices':[a,b],
            'originalLength':float(lengths[i]),'posedLength':float(posed_lengths[i]),
            'elongation':float(delta[i]),'originalMidpoint':((rest[a]+rest[b])*.5).tolist(),
            'posedMidpoint':((pose[a]+pose[b])*.5).tolist(),
            'endpointWeights':[{obj.vertex_groups[g.group].name:g.weight for g in obj.data.vertices[v].groups} for v in [a,b]]})
    records.append({'slot':slot,'frame':frame,'largestEdges':samples})
path=Path(args.manifest).parent/'LocalizedEdgeWarnings.json'
write_json(path,{'label':m['label'],'motionBlendSha256':m['reviewBlendSha256'],'samples':records,
    'sourceUnchanged':sha256(m['reviewBlend'])==m['reviewBlendSha256']})
for r in records:print(m['label'],r['slot'],r['frame'],r['largestEdges'][0],flush=True)
