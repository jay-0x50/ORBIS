"""Read-only cross-section topology of the proposed restricted retopo window."""
from pathlib import Path
import json
import numpy as np
STAGE=Path(__file__).resolve().parents[2]
folder=STAGE/'TestResults/CharacterPipeline/RigReview/Stella400kV3'
geometry=np.load(folder/'Stella_geometry.npz');skin=np.load(folder/'Stella_weights.npz')
co=geometry['world'];norm=geometry['normalized'];tri=geometry['triangles']
result=[]
for side in ['Left','Right']:
    cols=np.isin(skin['names'],[side+'UpperLeg',side+'LowerLeg',side+'Foot',side+'Toes'])
    other='Right' if side=='Left' else 'Left'
    othercols=np.isin(skin['names'],[other+'UpperLeg',other+'LowerLeg',other+'Foot',other+'Toes'])
    vertex_mask=(skin['regions']=='leg')&(skin['weights'][:,cols].sum(1)>skin['weights'][:,othercols].sum(1))
    side_tri=tri[vertex_mask[tri].all(1)]
    for z in [.08,.10,.12,.14,.16,.18,.20,.22,.235,.25,.30,.38,.40,.412,.425,.44,.455,.47,.485,.5,.52,.535]:
        hit=(norm[side_tri,2].min(1)<z)&(norm[side_tri,2].max(1)>z)
        crossing=side_tri[hit];adj={};points={}
        for t in crossing:
            ends=[]
            for a,b in [(t[0],t[1]),(t[1],t[2]),(t[2],t[0])]:
                if (norm[a,2]-z)*(norm[b,2]-z)>=0:continue
                key=(int(min(a,b)),int(max(a,b)))
                amount=(z-norm[a,2])/(norm[b,2]-norm[a,2])
                points[key]=co[a]+amount*(co[b]-co[a]);ends.append(key)
            if len(ends)==2:
                a,b=ends;adj.setdefault(a,[]).append(b);adj.setdefault(b,[]).append(a)
        unseen=set(adj);components=[]
        while unseen:
            seed=next(iter(unseen));stack=[seed];unseen.remove(seed);members=[]
            while stack:
                item=stack.pop();members.append(item)
                for other in adj[item]:
                    if other in unseen:unseen.remove(other);stack.append(other)
            pts=np.array([points[k] for k in members]);components.append({'points':len(pts),'xy_bounds':[pts[:,:2].min(0).tolist(),pts[:,:2].max(0).tolist()],
                'degree_not_two':sum(len(adj[k])!=2 for k in members)})
        components.sort(key=lambda r:-r['points'])
        result.append({'side':side,'normalized_z':z,'crossing_triangles':len(crossing),'components':components})
out=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegSmooth01/CrossSectionsExtended.json'
out.write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps([{'side':r['side'],'z':r['normalized_z'],'loops':len(r['components']),'sizes':[c['points'] for c in r['components'][:8]]} for r in result],indent=2))
