"""Summarize actual player timing JSON. Unsupported GPU counters remain unavailable."""
from pathlib import Path
import json, sys

root = Path(__file__).resolve().parents[2]
paths = [Path(p) for p in sys.argv[1:]] or list((root/'TestResults/WorldDev').glob('Performance*/WorldPerformance.json'))
for path in paths:
    data = json.loads(path.read_text(encoding='utf-8-sig'))
    print(f"{path.parent.name}: {data.get('graphics')} {data.get('width')}x{data.get('height')} "
          f"{data.get('quality')} LOD={data.get('lodBias')} complete={data.get('complete')}")
    if data.get('error'): print('ERROR:', data['error'].splitlines()[0])
    offscreen = data.get('renderingMode', '').startswith('Offscreen')
    if offscreen: print('  OFFSCREEN: loop throughput is not presented game FPS; zero render counters are unavailable in this mode.')
    for station in data.get('stations', []):
        values = {m['name']: m for m in station['metrics']}
        wall = values['Wall frame time']
        gpu = values.get('FrameTiming GPU', {})
        draw = values.get('Draw Calls Count', {})
        throughput = 'loop/s' if offscreen else 'meanFPS'
        draw_available = draw.get('available') and not (offscreen and draw.get('mean', 0) == 0)
        print(f"  {station['name']}: {throughput}={1000/wall['mean']:.1f} "
              f"p50/p95/p99={wall['p50']:.2f}/{wall['p95']:.2f}/{wall['p99']:.2f}ms "
              f"GPU={round(gpu.get('mean', 0), 2) if gpu.get('available') else 'unavailable'}ms "
              f"draw={round(draw.get('mean', 0)) if draw_available else 'unavailable'} "
              f"rendered={station['renderedFrames']}")
    print('  Packed cycle:', data.get('packedSceneCycle'))
