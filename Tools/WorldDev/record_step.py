"""Record authored source changes between consecutive world-art steps; no Unity source mutation."""
from pathlib import Path
import hashlib, json, sys

root = Path(__file__).resolve().parents[2]
step = int(sys.argv[1])
out = root / 'Docs' / 'WorldArt'
out.mkdir(parents=True, exist_ok=True)
previous = root / 'world-baseline.json' if step == 1 else out / f'World{step-1:02}_Hashes.json'
baseline = {k.replace('\\', '/'): v.upper() for k, v in json.loads(previous.read_text(encoding='utf-8-sig')).items()}
current = {}
for folder in ('Assets', 'Docs', 'Packages', 'ProjectSettings', 'Tools', '기획서'):
    for path in (root / folder).rglob('*'):
        if not path.is_file():
            continue
        relative = path.relative_to(root).as_posix()
        if ('/SourceDownloads/' in relative or '/__pycache__/' in relative or '/StreamingAssets/aa/' in relative
                or relative in ('Docs/WorldArt/DeliveryManifest.json', 'Docs/WorldArt/Delivery_FILES.md', 'Docs/WorldArt/DeliveryVerification.json')
                or relative.endswith('.blend1') or (relative.startswith('Docs/WorldArt/World') and
                (relative.endswith('_Hashes.json') or relative.endswith('_FILES.md')))):
            continue
        current[relative] = hashlib.sha256(path.read_bytes()).hexdigest().upper()
for name in ('Credits.md', 'README.md', '.gitignore'):
    path = root / name
    if path.exists(): current[name] = hashlib.sha256(path.read_bytes()).hexdigest().upper()
changed = sorted(k for k, v in current.items() if baseline.get(k) != v)
lines = [f'# World Step {step:02} — 변경 파일', '',
         '단계별 소스 스냅샷 차이입니다. Unity `.meta`도 포함합니다. 테스트 로그·PNG는 `TestResults/WorldDev/`에 별도 보존합니다.', '',
         f'총 {len(changed)}개 파일.', '']
for name in changed:
    state = '생성' if name not in baseline else '변경'
    lines.append(f'- {state}: `{name}`')
(out / f'World{step:02}_FILES.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
(out / f'World{step:02}_Hashes.json').write_text(json.dumps(current, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'World {step}: {len(changed)} changed/new source files recorded.')
