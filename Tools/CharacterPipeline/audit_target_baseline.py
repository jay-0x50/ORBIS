"""Read-only target audit before preparing an explicit delivery. Never copies or deletes files."""
import hashlib
import json
from pathlib import Path
import argparse

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--target', type=Path, default=Path('D:/Project/ORBIS'))
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
target = args.target.resolve(strict=True)
output = args.output.resolve()
assert output.is_relative_to(root / 'TestResults/CharacterPipeline') and not output.exists()
baseline_path = root / 'Tools/CharacterPipeline/ProjectBaseline.json'
baseline = json.loads(baseline_path.read_text(encoding='utf-8-sig'))

def digest(path):
    if not path.is_file():
        return None
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

changed, missing, staged = [], [], []
for relative, expected in baseline.items():
    relative_path = Path(relative)
    assert not relative_path.is_absolute() and '..' not in relative_path.parts
    actual = digest(target / relative_path)
    source = digest(root / relative_path)
    if actual is None:
        missing.append(relative)
    elif actual != expected:
        changed.append({'path': relative, 'baseline': expected, 'target': actual, 'staging': source})
    if source != expected:
        staged.append({'path': relative, 'baseline': expected, 'staging': source, 'target': actual,
                       'targetSafeToReplace': actual == expected})
report = {'target': str(target), 'baselineSha256': digest(baseline_path), 'baselineFiles': len(baseline),
          'targetChangesSinceBaseline': changed, 'targetMissingSinceBaseline': missing,
          'stagingChangesToExisting': staged,
          'scope': 'Read-only snapshot, not permission to overwrite. Recheck every selected destination immediately before delivery. New assets require a separate explicit dependency manifest.'}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'baselineFiles': len(baseline), 'targetChanged': len(changed), 'targetMissing': len(missing),
                  'stagingChangedExisting': len(staged), 'report': str(output)}, ensure_ascii=False))
