"""Hash-guarded source/evidence manifest. Does not write into the game project."""
from pathlib import Path
import hashlib,json,sys
root=Path(__file__).resolve().parents[2]
step=int(sys.argv[1])
baseline_file=root/'lookdev-baseline.json'
if not baseline_file.exists():baseline_file=root/'TestResults/LookDev/Baseline.json'
baseline=json.loads(baseline_file.read_text(encoding='utf-8-sig'))
prior_file=root/'lookdev-delivered-hashes.json'
prior=json.loads(prior_file.read_text(encoding='utf-8-sig')) if prior_file.exists() else baseline
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def entries():
    paths=[]
    for folder in ('Assets','Docs','Packages','ProjectSettings','Tools','기획서'):
        paths.extend(p for p in (root/folder).rglob('*') if p.is_file())
    paths.extend(root/p for p in ('README.md','Credits.md','.gitignore'))
    for p in paths:
        rel=p.relative_to(root).as_posix()
        if any(x in rel for x in ('/__pycache__/','/HeadRefine-preview/','/AnatomyExports/','/FaceExports/','/FinalExports/')):continue
        if rel.endswith('.blend1') or rel.startswith(('Assets/AddressableAssetsData/Windows','Assets/StreamingAssets/aa')):continue
        if rel in ('Tools/Blender/create_inferno_hornbeast.py','ProjectSettings/TimeManager.asset'):continue
        key=rel.replace('/','\\');sha=digest(p)
        if sha!=baseline.get(key):yield dict(Path=key,Hash=sha,Baseline=baseline.get(key),Expected=prior.get(key))
doc=root/'Docs/LookDev_FILES.md'
doc.write_text('# 주인공 룩 개발 변경 파일\n',encoding='utf-8') if not doc.exists() else None
rows=sorted(entries(),key=lambda x:x['Path'])
lines=['# 주인공 룩 개발 변경 파일','',f'STEP 1–{step} 전달 목록. 원본 캐릭터 FBX·본·애니메이션 파일은 변경하지 않았습니다.','.meta 포함 '+str(len(rows))+'개 소스 파일. Library·빌드 캐시·테스트 임시 TimeManager 변경은 제외합니다.','']
for r in rows:lines.append('- '+r['Path'].replace('\\','/'))
lines+=['','검수: [단계별 기록](LookDev_검수기록.md). 수정 전 원본 백업: `TestResults/LookDev-before/`.']
doc.write_text('\n'.join(lines)+'\n',encoding='utf-8')
rows=sorted(entries(),key=lambda x:x['Path'])
for r in rows:
    if r['Path'].startswith('Assets\\') and not r['Path'].endswith('.meta'):
        assert (root/(r['Path']+'.meta')).exists(),r['Path']+' missing .meta'
for mode,min_count in [('EditMode',288),('PlayMode',62)]:
    import xml.etree.ElementTree as ET
    test=ET.parse(root/f'TestResults/LookDev/Step{step:02d}_{mode}.xml').getroot()
    assert test.attrib['result']=='Passed' and int(test.attrib['failed'])==0 and int(test.attrib['skipped'])==0
    assert int(test.attrib['total'])>=min_count
artifacts=[]
for p in sorted((root/'TestResults/LookDev').glob('*')):
    if not p.is_file():continue
    import re
    m=re.match(r'Step(\d+)',p.name)
    if m and int(m[1])>step:continue
    artifacts.append(dict(Path=str(p.relative_to(root)),Hash=digest(p)))
data=dict(Stage=str(root),Target='D:\\Project\\ORBIS',CompletedStep=step,SourceEntries=rows,Artifacts=artifacts)
(root/'lookdev-delivery.json').write_text(json.dumps(data,indent=2,ensure_ascii=False),encoding='utf-8')
print(f'Prepared {len(rows)} source files, {len(artifacts)} evidence files through STEP {step}.')
