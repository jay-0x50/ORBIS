"""Plan a guarded world-art delivery. Only reads the real project; never deletes files."""
from pathlib import Path
from collections import Counter
import hashlib, json

ROOT = Path(__file__).resolve().parents[2]
TARGET = Path('D:/Project/ORBIS')
BASE = {k.replace('\\', '/'): v.upper() for k, v in json.loads((ROOT/'world-baseline.json').read_text(encoding='utf-8-sig')).items()}
def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024*1024), b''): h.update(chunk)
    return h.hexdigest().upper()
def sources():
    for folder in ('Assets','Docs','Packages','ProjectSettings','Tools','기획서'):
        for path in (ROOT/folder).rglob('*'):
            if not path.is_file(): continue
            name=path.relative_to(ROOT).as_posix()
            if any(x in name for x in ('/__pycache__/','/StreamingAssets/aa/','/Drafts/')) or name.endswith('.blend1'):
                continue
            if name in ('Docs/WorldArt/DeliveryManifest.json','Docs/WorldArt/Delivery_FILES.md','Docs/WorldArt/DeliveryVerification.json'):
                continue
            yield path
    for name in ('Credits.md','README.md','.gitignore','run-world-unity.ps1','world-baseline.json'):
        if (ROOT/name).is_file(): yield ROOT/name

def main():
    entries=[]; counts=Counter(); conflicts=[]
    paths=list(sources())
    for directory in ('TestResults/WorldDev', 'Builds/WorldBenchmark/Development', 'WorldArtBackups/BeforeWorldUpgrade'):
        for path in (ROOT/directory).rglob('*'):
            if not path.is_file(): continue
            name=path.relative_to(ROOT).as_posix()
            if any(x in name for x in ('/BenchmarkProfile/', 'BackUpThisFolder_ButDontShipItWithYourGame', '/DeliveryVerification.json')): continue
            paths.append(path)
    for path in sorted(paths):
        name=path.relative_to(ROOT).as_posix(); desired=sha(path); baseline=BASE.get(name)
        if desired == baseline: continue
        target=TARGET/name
        actual=sha(target) if target.is_file() else None
        if actual not in (None,baseline,desired) or (baseline is not None and actual is None):
            conflicts.append({'path':name,'baseline':baseline,'target':actual,'desired':desired})
        kind=('original-backup' if name.startswith('WorldArtBackups/') else 'source' if name.startswith(('Assets/','Packages/','ProjectSettings/','기획서/')) else
              'evidence' if name.startswith('TestResults/') else 'player' if name.startswith('Builds/') else
              'source-download' if '/SourceDownloads/' in name else 'regeneration-cache' if '/BuildCache/' in name else 'documentation-tools')
        entries.append({'path':name,'sha256':desired,'baseline':baseline,'observedTarget':actual,'bytes':path.stat().st_size,'kind':kind})
        counts[kind]+=1
    missing=[k for k in BASE if not (ROOT/k).exists()]
    result={'schemaVersion':1,'sourceRoot':str(ROOT),'targetRoot':str(TARGET),'operation':'Copy only; no target deletions or mirror. Recheck every source and target hash immediately before copying.','entries':entries,'conflicts':conflicts,'missingFromStageBaseline':missing,'counts':dict(counts),'bytes':sum(e['bytes'] for e in entries)}
    out=ROOT/'Docs/WorldArt';out.mkdir(exist_ok=True,parents=True)
    (out/'DeliveryManifest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    lines=['# 실제 월드 변경·전달 파일 목록','','`world-baseline.json`의 작업 시작 시점과 비교한 목록이다. 기존 캐릭터와 다른 미변경 파일은 전달하지 않는다. 대상 폴더를 삭제하거나 미러링하지 않고, 충돌 없는 항목만 해시 검증 후 복사한다.','',f'총 {len(entries):,}개 파일, {result["bytes"]/(1024*1024):.1f} MiB. 라이선스 원본, 재생성 캐시, 검수 이미지·로그, 실행용 Windows 빌드도 별도 분류한다.','','| 구분 | 파일 수 |','|---|---:|']
    lines += [f'| {key} | {value} |' for key,value in sorted(counts.items())]
    lines += ['','프로젝트 소스 목록:']
    for e in entries:
        if e['kind'] in ('source','documentation-tools'): lines.append(f'- `{e["path"]}`')
    lines += ['','기타 파일의 전체 경로·SHA-256·크기·분류는 [DeliveryManifest.json](DeliveryManifest.json)에 있다. 이 목록과 manifest 자체는 자기 참조 해시에서 제외되며 최종 복사 검증에는 포함한다.']
    (out/'Delivery_FILES.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps({'entries':len(entries),'counts':dict(counts),'bytes':result['bytes'],'conflicts':conflicts,'missingFromStageBaseline':missing,'projectSettings':[e['path'] for e in entries if e['path'].startswith('ProjectSettings/')]},ensure_ascii=False,indent=2))
if __name__=='__main__': main()
