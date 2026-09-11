"""Index original Unity renders; compare camera metadata without modifying any image."""
from pathlib import Path
import hashlib,json

root=Path(__file__).resolve().parents[2]
evidence=root/'TestResults/WorldDev'
views=['Overview','Silhouette','MeadowHighland','CoastalWetland','Village','Lakeside','ThermalFoothills','StormWoodland']
rows=[]
for step in range(1,7):
    for view in views:
        a=f'World{step:02}_Before_{view}';b=f'World{step:02}_After_{view}'
        if step==6:
            original=f'World05_After_{view}'
            assert (evidence/(a+'.png')).read_bytes()==(evidence/(original+'.png')).read_bytes()
            metadata=json.loads((evidence/(original+'.json')).read_text(encoding='utf-8-sig'))
            metadata['aliasNote']='STEP 6 Before is a byte-identical copy of the final STEP 5 clear-weather capture, not a new render.'
            metadata['sourceCapture']=original+'.png'
            metadata['sha256']=hashlib.sha256((evidence/(a+'.png')).read_bytes()).hexdigest()
            (evidence/(a+'.json')).write_text(json.dumps(metadata,indent=2),encoding='utf-8')
        ja=json.loads((evidence/(a+'.json')).read_text(encoding='utf-8-sig'))
        jb=json.loads((evidence/(b+'.json')).read_text(encoding='utf-8-sig'))
        same=all(ja[k]==jb[k] for k in ('camera','focus','fov','monochrome'))
        rows.append({'step':step,'view':view,'before':a,'after':b,'sameCamera':same})
assert all(x['sameCamera'] for x in rows),[x for x in rows if not x['sameCamera']]
(evidence/'World06_CameraPairAudit.json').write_text(json.dumps({'pairs':rows,'all48PairsSameCamera':True},indent=2),encoding='utf-8')
lines=['# 단계별 같은 앵글 비교','','모든 이미지는 Unity URP 원본 렌더다. 아래 48쌍은 위치·주목점·FOV·흑백 설정이 같음을 JSON으로 대조했다. STEP 6 Before는 직전 STEP 5 맑음 렌더의 바이트 동일 복사본이다. 프레임 시간 고정 캡처는 성능 벤치마크가 아니다.','','| 단계 | 뷰 | Before | After |','|---|---|---|---|']
for row in rows:
    lines.append(f'| {row["step"]} | {row["view"]} | [PNG](../../TestResults/WorldDev/{row["before"]}.png) | [PNG](../../TestResults/WorldDev/{row["after"]}.png) |')
lines += ['','## 날씨·동작·근접 뷰','','STEP 5의 위 기본 비교는 맑은 날씨다. 같은 MeadowHighland 카메라에서 실제 날씨는 다음 이미지를 확인한다.']
for name,label in [('World05_Before_MeadowHighland','맑음 기준'),('World05_After_Rain_MeadowHighland','비'),('World05_After_StrongWind_MeadowHighland','강풍'),('World05_After_ThunderstormCloud_MeadowHighland','뇌우 구름'),('World05_After_Thunderstorm_MeadowHighland','뇌우 플래시'),('World03_Detail_VillageClose','자피르 근접'),('World03_Detail_WaterfallClose','폭포 근접'),('World04_After_StellaWorld','캐릭터와 월드 톤'),('World02_Dynamics_Tree_ForcedLOD0','나무 LOD0'),('World02_Dynamics_Tree_ForcedLOD1','나무 LOD1'),('World02_Dynamics_Tree_ForcedLOD2','나무 빌보드'),('World02_Dynamics_Grass_PlayerInside','캐릭터 근처 잔디'),('World06_Visibility_Gap1','시야 공백 1'),('World06_Visibility_Gap2','시야 공백 2'),('World06_Visibility_Gap3','시야 공백 3')]:
    lines.append(f'- [{label}](../../TestResults/WorldDev/{name}.png)')
lines += ['','별도 Windows Packed 실행 원본: [마을](../../TestResults/WorldDev/PerformanceOffscreen/Village.png), [뇌우 숲](../../TestResults/WorldDev/PerformanceOffscreen/HighlandStorm.png). 숨김 RenderTexture 렌더이며 FPS 증거가 아니다.','','실패했던 캡처나 중간 결과도 삭제하지 않았다. 최종 통과 기록은 World06_FullEdit.xml / World06_FinalPlay.xml이고, 최종 스모크 결과는 PerformanceFinalSmoke/WorldPerformance.json이다.']
(root/'Docs/WorldArt/BeforeAfter.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('48 Before/After camera pairs match. Comparison index written; original PNG files untouched.')
