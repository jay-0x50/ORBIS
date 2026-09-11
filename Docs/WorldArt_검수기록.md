# 오르비스 연속 오픈월드 아트 검수

이 기록은 실제 저장된 Unity 씬과 실행 결과를 구분한다. 콘셉트 이미지를 게임 스크린샷으로 제시하지 않는다. 모든 비교 PNG는 `TestResults/WorldDev/`의 Unity URP 1920×1080 렌더이며, 같은 이름의 JSON에 카메라 위치·주목점·FOV가 있다. 고정 시간 간격을 사용하는 이미지 테스트는 FPS 측정이 아니다.

## STEP 1 — 연속 지형과 별도 스트리밍 경계

- 1km Terrain 4개가 이음새 높이 오차 0으로 연결된다. 전체 격자 2km×2km, 물 위 육지 약 2.5361km², 최고점 약 211.38m.
- 다섯 세계관 지역은 유지하고, 약 176m의 10–90% 바이옴 전이와 불규칙 경계를 사용한다. 기존 콘텐츠 허브의 65m 보호 영역은 유지한다.
- `Orbis_Island.unity`에 지형·물리·캐릭터·콘텐츠 상태가 남는다. 다섯 `Environment_*.unity`는 Addressables로 시각 오브젝트만 가산 로드한다. 전이 식생의 실제 범위와 스트리밍 경계는 별도 데이터다.
- 바이옴 EditMode 3개, 스트리밍 PlayMode 2개, 씬/캡처 PlayMode 1개 통과. 에디터 Addressables 로드/해제 검증이며 Packed 플레이어 검증과는 다르다.
- 비교: `World01_Before_Overview.png` / `World01_After_Overview.png`, `World01_*_Silhouette.png` 및 지상 6개 앵글.
- 파일: [World01_FILES.md](WorldArt/World01_FILES.md).

## STEP 2 — 초목, 바람, 잔디, 지면

- CC0 Quaternius 나무 7종을 지역별 최소 3종씩 확률 혼합한다. LOD0/LOD1/8방향 빌보드, 공통 텍스처 배열, Shader Graph 바람, WindZone 및 플레이어 근접 잔디 굽힘을 사용한다.
- 최초 생성 시 나무 4,895개, 하층 식생·바위 2,636개, 잔디 배치 995,445개. STEP 3에서 건물·콘텐츠·접근로와 겹친 일부를 제거했으므로 이 수치는 최종 합계가 아니다.
- 잔디는 32m 셀, GPU 인스턴싱 최대 256개 배치, 표시 거리 100m, 75m부터 페이드한다. 밀도와 거리는 이후 성능 검수 대상이다.
- 프로젝트용 생성 지면 텍스처 4종으로 7개 TerrainLayer를 구성한다. 높이·경사·습도에 따른 이끼 전이, 바위의 삼중평면 이끼 혼합을 적용한다.
- 잔디 데이터 EditMode 1개, 실제 GPU 렌더 PlayMode 1개, 바람/빌보드/캐릭터 굽힘 PlayMode 1개, 비교 캡처 1개 통과.
- 비교: `World02_Before_*` / `World02_After_*`; 추가 동작 이미지 `World02_Dynamics_*`.
- 파일: [World02_FILES.md](WorldArt/World02_FILES.md). 소싱 근거와 원문 증빙은 `WorldArt/Sources/`, 사용 범위는 `Credits.md` 참조.

## STEP 3 — 랜드마크와 기존 콘텐츠 동선

- 자체 제작 건축물 5종의 LOD 모델 10개를 사용한다. 자피르 주거 9채, 풍차 3기, 볼트하임 도체 첨탑 6기, 지역별 작업장/항해 거점, 해안 아치·광산 입구·부두·탐사길 표식과 암반을 배치한다.
- 기존 퍼즐 5개와 도전의 방 5개를 허브 밖으로 이동했다. 지역별 퍼즐–방 거리 약 163–178m. 기존 30개 표적의 ID·순서·참조, 보스·스폰·진행도와 보상 키는 유지한다.
- 지면에 맞춘 절벽 폭포 2곳을 호수에 연결했다. 물줄기와 암반은 별도 공유 메시/머티리얼이며 전투 VFX 풀을 사용하지 않는다.
- `World03_After.xml`: 실제 PlayMode 테스트 2개 통과. 저장 콘텐츠 참조, 30개 표적 지면, 등반 바위 충돌체, 풍차 회전, 환경 씬 로드·해제와 8개 고정 뷰를 검사했다.
- 별도 건축 FBX Blender 재임포트 10개 통과. Blender 좌표 검사와 Unity 정면 확인의 차이는 [Architecture_Coordinates.md](WorldArt/Architecture_Coordinates.md)에 기록한다.
- 비교: `World03_Before_*` / `World03_After_*`; 추가 근접 뷰 `World03_Detail_VillageClose.png`, `World03_Detail_WaterfallClose.png`.
- 확인한 후속 문제: 지면의 과한 반사와 식생의 강한 채도는 STEP 4에서 수정한다. 모든 동선에서 항상 다음 랜드마크가 보인다는 전수 시야 검사는 아직 수행하지 않았다.
- 파일: [World03_FILES.md](WorldArt/World03_FILES.md).

## STEP 4 — 공통 환경 스타일

- Terrain 기본/추가/원거리 패스와 바위의 3단 조명, 식생 Shader Graph의 잎 색 보정, 건축물의 램프 명암을 적용했다. 지형 높이와 기존 콘텐츠 위치는 이 단계에서 변경하지 않았다.
- 불투명 텍스처 알파가 smoothness=1로 읽히던 문제를 ConstantOnly/0으로 수정했다. 검은 지면 암부에는 절제된 청색 환경광을 추가했다.
- `M0_Renderer.asset`에 누락돼 있던 공식 URP PostProcessData를 연결했다. WorldGrade priority20은 기존 캐릭터 계열의 Contrast7/Saturation5/Neutral을 적용하고 M3의 priority100/1000 연출은 우선한다.
- M3의 정확한 원소 팔레트를 흰색에 6.5% 혼합한 연속 앰비언트를 사용한다. 고지대·전체 지도에서는 저고도 안개가 옅어져 대륙을 가리지 않는다.
- `World04_After.xml`: PlayMode 4개 통과(월드 캡처, 스타일/궁극기, GPU 잔디, 바람/빌보드/잔디 굽힘). 1m 이동 시 바이옴 색에 단절이 없고, 궁극기 ElementBurst 신호에서 상위 Volume이 실제 활성화됨을 확인했다.
- 비교: `World04_Before_*` / `World04_After_*`. 같은 렌더링 안의 캐릭터·마을은 `World04_After_StellaWorld.png`에 기록했다.
- 파일: [World04_FILES.md](WorldArt/World04_FILES.md), 구현 설명 [World_Ground_Style.md](World_Ground_Style.md).

## STEP 5 — 연속된 시각 날씨

- 본체 씬의 WorldWeather 하나가 맑음/비/뇌우/강풍을 6초 동안 보간한다. F11로 순환한다. 원소 부착·피해·활공 상승력 등 게임 규칙은 추가하지 않았다.
- 카메라 주변 재사용 메시의 비 1,024개와 강풍 리본 96개는 최대 2 draw, 3,584 triangles다. 지형/건물 깊이와 실내 차폐를 사용하며, 이동 중 입자가 4m마다 튀던 문제를 연속 좌표로 수정했다.
- 하늘·태양·안개·식생 WindZone은 같은 연속 날씨 값을 받는다. 뇌우는 짧은 2회 광량 플래시이며 번개 볼트 모델/사운드는 포함하지 않는다.
- `World05_After.xml`: 최종 날씨 PlayMode 1개 통과. 실제 5→0→5 환경 씬 로드, 본체 날씨 및 파티 보존, 1m 간격 최대 변화 0.001619637, WindZone 0.65→2.2를 확인했다. 앞선 통합 실행에서 월드 캡처와 스타일 검사를 포함한 3개도 통과했다.
- 비교: 맑은 `World05_Before_MeadowHighland.png`와 같은 앵글의 `World05_After_Rain_MeadowHighland.png`, `World05_After_StrongWind_MeadowHighland.png`, `World05_After_ThunderstormCloud_MeadowHighland.png`. 플래시 순간은 별도의 `World05_After_Thunderstorm_MeadowHighland.png`다. 정지 화면은 바람의 연속 움직임을 증명하지 않으므로 런타임 변화 검사를 함께 남겼다.
- 파일: [World05_FILES.md](WorldArt/World05_FILES.md).

## STEP 6 — 최적화, Packed 플레이어, 회귀 검사

- 6개 씬을 함께 베이크한 공통 OcclusionCullingData를 저장했다. 고정 건축물과 큰 불투명 바위 중 occluder 648개, renderer 바인딩 1,554개, PVS payload 10,583,248 bytes를 검증했다. 잎·수동 인스턴싱 잔디는 이 PVS 대상이 아니다. 베이크의 존재와 실제 프레임 개선은 별개다.
- 환경 5씬의 공유 의존성 64개를 별도 Addressables 그룹으로 묶었다. 실제 환경+공유 압축 번들 합계는 78.611→36.213MiB, 53.93% 감소했다. 기존 M4 주소와 원본 자원 해시는 유지했다. RAM/VRAM 감소량을 측정한 결과로 해석하지 않는다. [번들 감사](WorldArt/World06_BundleAudit.md).
- 최종 Windows Development 빌드 `World06_FinalBuild.log` / `World06_Build_Development.json`: Succeeded. 실제 Packed 빌드의 마을·숲·호숫가·뇌우 고원·이동 구간에서 1920×1080 URP offscreen 요청 20,099회를 처리했고 5→0→5 지역 씬 왕복도 통과했다. 최종 진단 필드 수정 후 `PerformanceFinalSmoke` 마을 렌더 1,182회와 같은 씬 왕복을 추가 확인했다.
- 최종 플레이어 기준 잔디 배치 977,312개, 전체 LODGroup 7,508개(나무만의 개수가 아니다), 잔디 CPU 행렬/셀 버퍼 62,688,496 bytes. 마을 검수 카메라에서 잔디 약 6,147개를 25개 배치로 보냈다. 나무는 MeshRenderer/SRP Batcher 경로이며 잔디의 GPU 인스턴싱과 구분한다.
- 순환길 고체 시야 감사에서 새 랜드마크가 정면에 잡히는 40m 표본 비율은 양방향 93.75%, 카메라 회전 탐색은 100%다. 최장 정면 공백은 정방향 240m, 역방향 160m다. 실제 공백 이미지 3곳은 기존 허브이며 기존 표식/콘텐츠가 보이지만 새 랜드마크 검사에는 포함하지 않는다. 잎·안개·모든 샛길의 가독성까지 보장하지 않는다. [시야 검사 방법과 한계](World_Landmark_Visibility.md).
- 최종 전체 회귀: `World06_FullEdit.xml` **292/292**, `World06_FinalPlay.xml` **71/71**, 실패·스킵 0. 이전 `World06_After.xml`의 단일 실패는 공용 Terrain 경계의 정확한 Collider ray 문제였으며, 경계에서만 최대 1mm 내부 광선을 허용하고 재검사했다. 지면 구멍/내부 광선 실패는 계속 실패한다. 이후 편집기 시작 씬 해제 메뉴의 컴파일·실행도 `World06_FinalEditorMenu.log`에서 exit 0을 확인했다.
- 캐릭터, 리그, 애니메이션, 프리팹, 재질 등 보호 대상 140개 파일이 기존 스냅샷과 바이트 단위로 같다. 기존의 본 구조 검사를 새로 실행했다는 의미는 아니다. [보호 파일 감사](WorldArt/World06_ProtectedCharacterAudit.json).
- 비교: `World06_Before_*` / `World06_After_*`. 최적화 단계에서는 지형/배치를 의도적으로 바꾸지 않았다. 최종 전체 테스트의 동일 앵글은 `World06_FinalPlay_*`에도 보존한다. 파일: [World06_FILES.md](WorldArt/World06_FILES.md).

### 실제 목표 FPS 검증은 남아 있음

확인한 PC는 i7-10700 / GTX 1650 SUPER 4GB / 32GB RAM이다. 임시 검수 조건은 1080p, 기존 Ultra, LOD bias 2, MSAA 4×, URP 그림자 거리 45m이며 목표는 60fps로 가정했다. 사용자가 별도 목표 PC를 확정한 것은 아니다.

일반 숨김 창은 Unity가 화면 렌더를 생략해 `renderedFrames=0`으로 실패 처리됐다. Offscreen 검사는 실제 RenderTexture를 생성하지만 창 표시를 포함하지 않으며 GPU 시간도 사용할 수 없었다. 따라서 그 처리율을 게임 FPS로 보고하지 않는다. 실제 1080p 게임 창 표시 허용 질문에 답변이 없어 창을 띄우지 않았다. **PC 목표 프레임 유지 및 오클루전의 실측 효과는 미검증이며 STEP 6 전체 완료로 표시하지 않는다.**

게임 창을 직접 띄워 확인하려면 [실행 안내](World_실행안내.md)의 `RunBenchmark.ps1 -Visible`을 사용한다. 별도 임시 프로필로 저장하며 기존 세이브를 바꾸지 않는다. 현재 기본 단계 통계를 본 뒤 심한 저하가 있으면 밀도 하향/먼 잔디 LOD 강화를 선택할 수 있다. 측정 전 임의로 그래픽 품질을 낮추지 않았다.

## 아트 결과의 범위

연속 지형, 혼합 식생, 자체 건축물, 폭포/해안/광산 랜드마크와 통일된 환경 톤을 실제 씬에 적용했다. 현재 결과는 플레이 가능한 스타일라이즈드 월드 프로토타입이다. 건축물 주변 생활 소품, 손으로 다듬은 지형 구성·거리별 디테일, 전체 동선의 시각 가독성은 상용 게임 수준과 차이가 남는다. 몬드성 상용 아트 완성도를 달성했다고 주장하지 않는다.

- [단계별 같은 앵글 Before/After](WorldArt/BeforeAfter.md)
- [최종 변경·전달 파일 목록](WorldArt/Delivery_FILES.md)
- [씬 열기와 테스트 조작](World_실행안내.md)
