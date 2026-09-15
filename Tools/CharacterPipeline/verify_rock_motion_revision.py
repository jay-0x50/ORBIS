"""CPU full integer-frame pose and immutable rest-contract comparison."""
import argparse,json,sys
from pathlib import Path
import bpy,numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from audit_raw_meshes import sha256,write_json,array
from rig_bosses import geometry_fingerprint,packed_maps,evaluated_positions

def inspect(path):
    m=json.loads(Path(path).read_text(encoding='utf-8-sig'))
    assert sha256(m['reviewBlend'])==m['reviewBlendSha256']
    bpy.ops.wm.open_mainfile(filepath=m['reviewBlend'],load_ui=False,use_scripts=False)
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
    rest=array(mesh.data.vertices,'co',(len(mesh.data.vertices),3))
    bones={b.name:{'parent':b.parent.name if b.parent else None,'matrix':[list(r) for r in b.matrix_local],
          'connect':b.use_connect,'deform':b.use_deform} for b in rig.data.bones}
    state={'rigObjectName':rig.name,'meshObjectName':mesh.name,'bones':bones,'maps':packed_maps(),
           'geometry':geometry_fingerprint(mesh.data),'rigMatrix':[list(r) for r in rig.matrix_world],
           'meshMatrix':[list(r) for r in mesh.matrix_world]}
    poses={};targets={}
    edges=[[47546,47676],[9756,9873],[33855,34918]]
    for c in m['clips']:
        rig.animation_data.action=bpy.data.actions[c['action']]
        samples=[]
        for frame in range(c['endFrame']+1):
            bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
            samples.append(np.array([list(b.matrix_basis) for b in rig.pose.bones]))
        poses[c['slot']]=np.array(samples)
        if c['slot']=='Dead':
            co=evaluated_positions(mesh)
            for a,b in edges:
                targets[str([a,b])]=float(np.linalg.norm(co[a]-co[b])-np.linalg.norm(rest[a]-rest[b]))
    return m,state,poses,targets

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--baseline',required=True);parser.add_argument('--candidate',required=True)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    a,as_,ap,at=inspect(args.baseline);b,bs,bp,bt=inspect(args.candidate)
    checks={k:as_[k]==bs[k] for k in as_}
    clip_stats=[]
    for old,new in zip(a['clips'],b['clips']):
        assert old['slot']==new['slot']
        clip_stats.append({'slot':old['slot'],'poseMatrixMaximumDelta':float(np.abs(ap[old['slot']]-bp[old['slot']]).max()),
            'oldMaximumEdgeElongation':max(d['maximumEdgeElongation'] for d in old['deformationSamples']),
            'newMaximumEdgeElongation':max(d['maximumEdgeElongation'] for d in new['deformationSamples']),
            'oldMaximumEdgesOver2x':max(d['edgesOver2x'] for d in old['deformationSamples']),
            'newMaximumEdgesOver2x':max(d['edgesOver2x'] for d in new['deformationSamples']),
            'candidateMaximumSoleError':new['maximumSkinnedSoleTargetError'],
            'loopEndpointMatrixError':new['loopEndpointMatrixError']})
    report={'baseline':args.baseline,'candidate':args.candidate,'checks':checks,
            'restContractPass':all(checks.values()),'integerFramesPerAction':'All inclusive 0..end at 30fps',
            'clips':clip_stats,'trackedDead60Edges':[{'edge':e,'before':at[e],'after':bt[e]} for e in at],
            'baselineUnchanged':sha256(a['reviewBlend'])==a['reviewBlendSha256'],
            'candidateUnchanged':sha256(b['reviewBlend'])==b['reviewBlendSha256'],'gameReady':False}
    report['sameAuthoredPoseCurves']=all(c['poseMatrixMaximumDelta']<1e-6 for c in clip_stats)
    path=Path(args.candidate).parent/'RevisionComparison.json';write_json(path,report)
    print('ROCK_REVISION',report['restContractPass'],report['sameAuthoredPoseCurves'],flush=True)
    print(json.dumps(clip_stats),flush=True);print('TRACKED',report['trackedDead60Edges'],flush=True)

if __name__=='__main__':main()
