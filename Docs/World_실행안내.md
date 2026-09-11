# 연속 월드 실행 및 아트 검수

Unity Hub에서 `D:\Project\ORBIS`를 Unity 6000.6.0f1로 연다. 패키지 임포트가 끝나면 아래 절차를 사용한다. 이전 M0~M4의 `Setup` 메뉴는 작은 독립 프로토타입을 재생성하는 도구이므로 이번 월드를 확인할 때 실행할 필요가 없다.

## 씬과 Hierarchy

1. **Orbis > World > Open Full Continent for Editing**: `Assets/Orbis/Game/Scenes/Orbis_Island.unity`와 `Assets/Orbis/Game/World/Scenes/Environment_*.unity` 5개를 함께 연다.
2. Hierarchy의 본체에는 연속 Terrain 4개, 물/물리, 기존 지역 콘텐츠, 카메라 진입점, 환경 스트리밍·바람·날씨 컨트롤러가 있다. 환경 씬에는 `Nature / Biome Blending`, `Landmarks / Continental Routes`와 잔디 필드가 있다.
3. 이 메뉴는 Play 시작 씬을 본체로 지정한다. 실행 중에는 Addressables가 환경 씬을 관리한다. 편집용으로 열어 둔 환경을 게임용 객체로 복제하는 작업은 하지 않는다.
4. Play 후 Game 뷰를 클릭한다. 저장된 주인공이 없으면 기존 스텔라/폴라리스 선택 화면을 사용한다. 기존 세이브가 있으면 선택된 주인공으로 시작한다.

본체 씬만 직접 열어도 실행할 수 있다. 이 경우 편집 시 초목이 비어 보이는 것은 별도 환경 씬이 닫혀 있기 때문이다. 위 메뉴로 다섯 환경 씬까지 함께 열 수 있다.

다른 M0~M4 데모를 다시 실행할 때는 **Orbis > World > Restore Play from Current Scene**으로 Play 시작 씬 고정을 해제한 뒤 원하는 데모 씬을 연다. 전체 대륙 메뉴가 설정한 시작 씬은 편집기 설정으로 유지되므로, 씬 탭만 바꾸면 섬에서 시작할 수 있다.

## 검수 동선과 조작

| 확인 항목 | 방법 |
|---|---|
| 자연스러운 경계 | 자피르 마을에서 서쪽 산지 방향 길을 걸어 초지·자작나무·침엽수가 같은 화면에서 섞이는지 확인한다. 북동쪽은 폭풍 숲, 동남쪽은 습지/해안으로 이어진다. |
| 지역 빠른 이동 | F10의 기존 지역 이동 메뉴를 사용한다. 같은 대륙의 좌표로 이동하며 주변 환경은 비동기로 채워진다. |
| 이동/등반/활공 | WASD·Shift·Space, 등반 가능 바위 가까이에서 E. 공중 Space는 기존 활공 조작이다. |
| 초목 | 가까운 나무와 먼 실루엣을 비교하고, 풀 사이를 걸어 캐릭터 주변 잔디가 눕는지 확인한다. |
| 랜드마크 | 자피르 주거·풍차, 그라니테 광산 입구, 텔루나 항해 거점·해안 아치, 아그니아 작업장·화산암, 볼트하임 도체 첨탑을 확인한다. 호수에는 지형에서 흘러내리는 폭포 2곳이 있다. |
| 기존 콘텐츠 | 각 지역 허브 밖의 기존 원소 퍼즐과 도전방을 방문한다. F 상호작용과 원래 원소/보상 규칙을 유지한다. |
| 날씨 | F11: 맑음 → 비 → 뇌우 → 강풍 → 맑음, 기본 전환 6초. 지역 경계를 걸어도 효과가 즉시 끊기지 않아야 한다. 뇌우 플래시는 시각 연출이며 번개 피해나 신규 원소 부착 규칙은 없다. |
| 전투 톤 | 기존 일반공격과 Q 연출을 확인한다. M3 연출 Volume이 기본 월드 그레이딩보다 우선한다. |

## 증거와 성능 측정

- 단계별 기록: `Docs/WorldArt_검수기록.md`, 파일 목록: `Docs/WorldArt/World01_FILES.md`~`World06_FILES.md`.
- 동일 앵글 Before/After: `TestResults/WorldDev/World01_*`~`World06_*`. PNG와 JSON 카메라 좌표를 함께 보존한다.
- 성능용 빌드: **Orbis > World Performance > Build Windows Player**. 정상 프로젝트 씬과 실제 Packed Addressables를 빌드한다.
- `Tools/WorldDev/RunBenchmark.ps1 -Visible -Name PerformanceLocal`은 1080p 게임 창을 열고 임시 세이브로 다섯 구간을 측정한다. 이전 측정 폴더를 덮어쓰지 않으므로 `-Name`은 매번 새 이름을 사용한다.
- 숨김 일반 게임 창은 Unity가 렌더를 생략할 수 있다. `renderedFrames=0`인 실행은 실패로 처리한다. `-Offscreen`은 실제 URP RenderTexture 검사이며, 창 표시를 포함한 게임 FPS 측정과 구분한다.
- 스크린샷 ReadPixels, 최초 로딩, 셰이더 예열은 정상 프레임 통계에서 분리한다. 지원되지 않는 GPU 계측 값을 0ms 성능으로 해석하지 않는다.

이 문서는 조작과 구조를 설명한다. 실제 통과 수치와 품질 한계는 검수 기록 및 해당 실행 JSON이 기준이다.
