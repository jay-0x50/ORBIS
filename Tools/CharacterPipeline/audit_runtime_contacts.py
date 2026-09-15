"""Audit all captured skinned sole points, including releases and transition frames."""
import argparse,hashlib,json,math,statistics
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('source',type=Path);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
raw=a.source.read_bytes();data=json.loads(raw.decode('utf-8-sig'));frames=data['frames']
assert not data.get('failure') and len(frames)==data['expectedFrameCount']
assert not a.output.exists()
def v(d):return [d[k] for k in 'xyz']
def distance(a,b,planar=False):return math.sqrt(sum((a[k]-b[k])**2 for k in ('xz' if planar else 'xyz')))
rows=[];phases={}
for prev,frame in zip(frames,frames[1:]):
    dt=frame['sampleTime']-prev['sampleTime'];assert dt>0
    feet={f['side']:f for f in frame['skinFeet']};old={f['side']:f for f in prev['skinFeet']}
    for side,foot in feet.items():
        before=old[side];assert foot['pointIds']==before['pointIds']
        speeds=[distance(x,y,True)/dt for x,y in zip(foot['pointsWorld'],before['pointsWorld'])]
        row={'frame':frame['index'],'phase':frame['phase'],'side':side,
             'sourceContact':foot['contact'],'effectiveContact':foot.get('effectiveContact',foot['contact']),
             'released':foot.get('released',False),'recovering':foot.get('recovering',False),
             'soleStepMetres':distance(foot['calibratedSoleWorld'],before['calibratedSoleWorld']),
             'soleHeight':foot['calibratedSoleWorld']['y']-data['floorCenter']['y'],
             'meanSurfacePlanarSpeed':statistics.mean(speeds),'maxSurfacePlanarSpeed':max(speeds),
             'interiorPlant':foot.get('effectiveContact',foot['contact'])>=.99 and before.get('effectiveContact',before['contact'])>=.99,
             'samePhase':frame['phase']==prev['phase']}
        rows.append(row);phases.setdefault(row['phase'],[]).append(row)
summary=[]
for phase,values in phases.items():
    plant=[r for r in values if r['interiorPlant'] and r['samePhase']]
    summary.append({'phase':phase,'samples':len(values),'interiorPlantSamples':len(plant),
        'plantMeanSurfaceSpeed':statistics.mean(r['meanSurfacePlanarSpeed'] for r in plant) if plant else None,
        'plantMaxSurfaceSpeed':max((r['maxSurfacePlanarSpeed'] for r in plant),default=None),
        'allFramesMaxSoleStep':max(r['soleStepMetres'] for r in values),
        'releasedSamples':sum(r['released'] for r in values),'recoveringSamples':sum(r['recovering'] for r in values)})
result={'source':str(a.source),'sha256':hashlib.sha256(raw).hexdigest(),'phases':summary,
    'largestSteps':sorted(rows,key=lambda r:r['soleStepMetres'],reverse=True)[:24],
    'note':'All transitions included. Interior plant uses actual effective-contact flag on both adjacent frames; all-frame step/release rows remain visible. These observations do not replace image/video review.'}
a.output.write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2))

