"""Read-only ordered planar sections for explicit boundary reconstruction."""
from pathlib import Path
import json
import numpy as np
from PIL import Image,ImageDraw

STAGE=Path(__file__).resolve().parents[2]
folder=STAGE/'TestResults/CharacterPipeline/RigReview/Stella400kV3'
geometry=np.load(folder/'Stella_geometry.npz');skin=np.load(folder/'Stella_weights.npz')
co=geometry['world'];norm=geometry['normalized'];tri=geometry['triangles']
result=[];canvas=Image.new('RGB',(1600,840),'white');draw=ImageDraw.Draw(canvas)
for si,side in enumerate(['Left','Right']):
    cols=np.isin(skin['names'],[side+'UpperLeg',side+'LowerLeg',side+'Foot',side+'Toes'])
    other='Right' if side=='Left' else 'Left'
    othercols=np.isin(skin['names'],[other+'UpperLeg',other+'LowerLeg',other+'Foot',other+'Toes'])
    vertex_mask=(skin['regions']=='leg')&(skin['weights'][:,cols].sum(1)>skin['weights'][:,othercols].sum(1))
    side_tri=tri[vertex_mask[tri].all(1)]
    for zi,z in enumerate([.230,.235,.412,.417]):
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
            seed=min(unseen);stack=[seed];unseen.remove(seed);members=[]
            while stack:
                item=stack.pop();members.append(item)
                for other in adj[item]:
                    if other in unseen:unseen.remove(other);stack.append(other)
            closed=all(len(adj[k])==2 for k in members)
            if not closed:continue
            ordered=[seed];previous=None;current=seed
            while True:
                nxt=next(x for x in adj[current] if x!=previous)
                if nxt==seed:break
                ordered.append(nxt);previous,current=current,nxt
            pts=np.array([points[k] for k in ordered]);xy=pts[:,:2]
            area=.5*np.sum(xy[:,0]*np.roll(xy[:,1],-1)-xy[:,1]*np.roll(xy[:,0],-1))
            components.append({'points':len(pts),'area':float(abs(area)), 'signed_area':float(area),
                'xy_bounds':[xy.min(0).tolist(),xy.max(0).tolist()], 'edge_keys':ordered,'positions':pts.tolist()})
        components.sort(key=lambda r:-r['area'])
        allxy=np.asarray([p[:2] for c in components for p in c['positions']]);mid=(allxy.min(0)+allxy.max(0))*.5
        scale=310/max(np.ptp(allxy,axis=0));offset=np.array([zi*400+200,si*420+230])
        for ci,c in enumerate(components):
            xy=np.asarray(c['positions'])[:,:2];xy=np.vstack([xy,xy[0]])
            pts=(xy-mid)*scale*np.array([1,-1])+offset
            draw.line([tuple(x) for x in pts],fill=['#095fb8','#e57612','#81969b'][min(ci,2)],width=2 if ci<2 else 1)
        draw.text((zi*400+10,si*420+15),f'{side} z={z}: {len(components)} loops',fill='black')
        result.append({'side':side,'normalized_z':z,'crossing_triangles':len(crossing),'components':components})
output=STAGE/'TestResults/CharacterPipeline/RigReview/StellaLegSmooth01'
(output/'OrderedCutLoops.json').write_text(json.dumps(result),encoding='utf-8')
canvas.save(output/'OrderedCutLoops.png')
print(json.dumps([{'side':r['side'],'z':r['normalized_z'],'largest':[{k:c[k] for k in ['points','area','xy_bounds']} for c in r['components'][:2]]} for r in result],indent=2))
