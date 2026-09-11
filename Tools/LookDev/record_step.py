"""Record source changes for one sequential look-development stage."""
from pathlib import Path
import hashlib,json,sys
root=Path(__file__).resolve().parents[2]
step=int(sys.argv[1]);name=f'Step{step:02d}'
before=root/('lookdev-baseline.json' if step==1 else f'lookdev-step{step-1:02d}-hashes.json')
if not before.exists():before=root/'TestResults/LookDev'/('Baseline.json' if step==1 else before.name)
baseline=json.loads(before.read_text(encoding='utf-8-sig'))
current={}
for folder in ('Assets','Docs','Packages','ProjectSettings','Tools','기획서'):
    for path in (root/folder).rglob('*'):
        if not path.is_file():continue
        rel=path.relative_to(root).as_posix()
        if any(x in rel for x in ('/__pycache__/','/HeadRefine-preview/','/AnatomyExports/','/FaceExports/','/FinalExports/')):continue
        if rel.endswith('.blend1') or rel.startswith(('Assets/AddressableAssetsData/Windows','Assets/StreamingAssets/aa')):continue
        if rel in ('Tools/Blender/create_inferno_hornbeast.py','ProjectSettings/TimeManager.asset'):continue
        current[rel.replace('/','\\')]=hashlib.sha256(path.read_bytes()).hexdigest().upper()
for rel in ('README.md','Credits.md','.gitignore'):current[rel]=hashlib.sha256((root/rel).read_bytes()).hexdigest().upper()
changed=sorted(k.replace('\\','/') for k,v in current.items() if baseline.get(k,'').upper()!=v)
doc=root/f'Docs/LookDev_{name}_Files.md'
lines=[f'# {name} 변경 파일','',f'직전 단계 대비 {len(changed)}개 파일(.meta 포함). 테스트 임시 TimeManager 변화와 캐시는 제외합니다.','']
lines += ['- '+path for path in changed]
lines+=['','## 동일 앵글 검증 이미지','']
for character in ('Stella','Polaris'):
    for view in ('Full','Close'):
        lines.append(f'- {character} {view}: [Before](../TestResults/LookDev/{name}_Before_{character}_{view}.png) / [After](../TestResults/LookDev/{name}_After_{character}_{view}.png)')
doc.write_text('\n'.join(lines)+'\n',encoding='utf-8')
current[doc.relative_to(root).as_posix().replace('/','\\')]=hashlib.sha256(doc.read_bytes()).hexdigest().upper()
(root/f'lookdev-step{step:02d}-hashes.json').write_text(json.dumps(current,indent=2,ensure_ascii=False),encoding='utf-8')
print(f'{name}: {len(changed)} changed files. {doc.relative_to(root)}')
