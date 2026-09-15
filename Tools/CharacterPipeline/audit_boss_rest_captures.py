"""Read actual rendered pixels and metadata; no image synthesis or edits."""
from pathlib import Path
from PIL import Image
import numpy as np
import hashlib
import json

root = Path(__file__).resolve().parents[2]
capture = root/'TestResults/CharacterPipeline/UnityBossPreview/Rest02'
rows=[]
for name in ('FireBoss','WaterBoss','RockBoss','WindBoss','LightningBoss'):
    record=json.loads((root/'TestResults/CharacterPipeline/UnityBossImport/Import01'/f'{name}.json').read_text())
    assert record['validGeneric']
    for angle in ('Front','ThreeQuarter'):
        pair=[]
        for outline in ('Off','On'):
            path=capture/name/f'{angle}_Outline{outline}.png'
            metadata=json.loads(path.with_suffix('.json').read_text())
            data=np.array(Image.open(path).convert('RGB'))
            foreground=np.max(np.abs(data.astype(int)-data[0,0]),axis=2)>4
            ratio=float(foreground.mean())
            assert ratio>.015, str(path)+': empty/undersized render'
            assert abs(metadata['normalizedGeometryMax']['y']-metadata['normalizedGeometryMin']['y']-1.8)<.02
            ys,xs=np.where(foreground)
            pair.append(data)
            rows.append({'file':str(path.relative_to(root)), 'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
                         'size':[data.shape[1],data.shape[0]], 'foregroundFraction':ratio,
                         'visiblePixelBounds':[int(xs.min()),int(ys.min()),int(xs.max()),int(ys.max())]})
        changed=float(np.any(pair[0]!=pair[1],axis=2).mean())
        rows[-1]['outlineToggleChangedPixelFraction']=changed
        assert changed>0, name+' outline toggle had no visible effect'
(capture/'PixelAudit.json').write_text(json.dumps({'images':rows,'status':'20 actual images nonempty, normalized height verified, outline toggle changes pixels.',
    'manualReview':'All five ThreeQuarter OutlineOn images directly viewed; not an animation or performance acceptance.',
    'rejectedPredecessor':'Import01 contains blank images from the preview helper mixing BakeMesh scale conventions and fitting the camera beyond its far clip. Retained but invalid.'},indent=2),encoding='utf-8')
print('20 actual rest images audited.')
