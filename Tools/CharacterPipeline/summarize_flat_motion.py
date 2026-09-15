"""Read actual recorder JSON; describe distances without turning diagnostic limits into art approval."""
import argparse
import json
import math
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--after', type=Path, required=True)
parser.add_argument('--before', type=Path)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
assert not args.output.exists(), 'Preserve prior evidence'
after = json.loads(args.after.read_text(encoding='utf-8-sig'))
frames = after['frames']
feet = [(f, s) for f in frames for s in f.get('skinFeet', [])]
stable = [(f, s) for f, s in feet if (45 <= f['index'] < 90 or 110 <= f['index'] < 150) and s['contact'] >= .8]
def foot_extreme(items):
    if not items:
        return None
    f, s = min(items, key=lambda pair: pair[1]['minimumY'])
    return {'frame': f['index'], 'phase': f['phase'], 'side': s['side'], 'minimumY': s['minimumY'],
            'distanceToFlat100': s['minimumY'] - 100, 'contact': s['contact'], 'solveWeight': s['solveWeight']}
report = {'source': str(args.after), 'frames': len(frames), 'finite': all(f['finite'] for f in frames),
          'note': 'Flat recipe floor Y=100 only. Raw low-contact, action and planted feet remain distinguished. No slope interpretation or visual approval.',
          'minimumRawFoot': foot_extreme(feet), 'minimumStableContact': foot_extreme(stable),
          'skillFirstFrames': [{k: f[k] for k in ['index', 'gameplayState', 'actionParameter', 'attackClock', 'timeScale']}
                               for f in frames if f['index'] in [240, 241, 242]],
          'finalSpeed': frames[-1]['measuredSpeed']}
if args.before:
    before = json.loads(args.before.read_text(encoding='utf-8-sig'))
    assert len(before['frames']) == len(frames)
    pairs = list(zip(before['frames'], frames))
    def vecdist(a, b):
        return math.sqrt(sum((a[k] - b[k]) ** 2 for k in ['x', 'y', 'z']))
    report['before'] = str(args.before)
    report['comparison'] = {'maxRootDistance': max(vecdist(a['rootPosition'], b['rootPosition']) for a, b in pairs),
                            'maxRequestedSpeedDifference': max(abs(a['requestedSpeed'] - b['requestedSpeed']) for a, b in pairs),
                            'maxMeasuredSpeedDifference': max(abs(a['measuredSpeed'] - b['measuredSpeed']) for a, b in pairs),
                            'gameplayStateMismatches': [a['index'] for a, b in pairs if a['gameplayState'] != b['gameplayState']],
                            'inputMismatches': [a['index'] for a, b in pairs if a['requestedKeys'] != b['requestedKeys']]}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False, indent=2))
