# 오르비스 — 다섯 속성의 통합 섬

Unity **6000.6.0f1**, URP·VFX Graph·Shader Graph **17.6.0**, Cinemachine·Timeline **6.6.0**, Input System **1.20.0**, Addressables **2.11.2** 프로젝트입니다. 완료된 M0~M4에 **KayKit 캐릭터 5종·Humanoid 애니메이션, Kenney 환경·UI, 공통 툰 셰이더**를 연결했습니다. 현재 애니메이션은 KayKit CC0이며 실제 Mixamo FBX는 사용자 반입 대기 중입니다.

캐릭터·환경 매핑과 조작은 [애셋 교체 테스트 문서](Docs/Art_구현_테스트.md), 애셋 변경 전체 목록은 [Art 파일 목록](Docs/Art_FILES.md), 출처·라이선스는 [Credits](Credits.md)에 있습니다. Mixamo 추가 반입은 [수동 다운로드 안내](Docs/Asset_Sourcing_Manual.md)를 따릅니다.


## 게임 씬 열기

**Orbis > World > Open Full Continent for Editing**을 선택하고 Play합니다. `Assets/Orbis/Game/Scenes/Orbis_Island.unity`는 가로·세로 2km의 지형에 아그니아·텔루나·자피르·그라니테·볼트하임을 통합한 씬입니다. 물 위 육지는 약 2.5361km²입니다. 이 메뉴는 본체와 다섯 Addressables 환경 씬을 함께 열어 Terrain·혼합 식생·마을·풍차·절벽·호수·기존 콘텐츠를 Hierarchy에서 확인하게 합니다. 실제 Play는 본체에서 시작하고 환경은 Addressables가 로드합니다.

**F11**로 맑음·비·뇌우·강풍을 전환합니다. 다른 마일스톤 데모로 돌아갈 때는 **Orbis > World > Restore Play from Current Scene**으로 시작 씬 고정을 해제합니다. 최신 안내는 [연속 월드 실행](Docs/World_실행안내.md), [단계별 검수](Docs/WorldArt_검수기록.md), [같은 앵글 Before/After](Docs/WorldArt/BeforeAfter.md)에 있습니다. 회귀 테스트는 EditMode 292개와 PlayMode 71개를 통과했습니다. 실제 1080p 60fps 유지 검증은 아직 남아 있으며 offscreen 렌더 처리율을 게임 FPS로 취급하지 않습니다.

새로 clone한 프로젝트도 위 메뉴로 실행합니다. Unity 패키지·Library·빌드 결과는 에디터에서 복구/생성합니다. 독립 실행 파일은 **Orbis > World Performance > Build Windows Player**에서 실제 Addressables 번들과 함께 만듭니다. 외부 팩 원본 ZIP·중간 변환 캐시·로컬 백업은 Git에 포함하지 않으며, 실행에 필요한 FBX·텍스처·재질·프리팹은 `Assets/`에 있습니다. 모델을 다시 생성할 때 필요한 원본 다운로드 위치와 라이선스는 [Credits](Credits.md) 및 `Docs/WorldArt/Sources/`를 참고하세요.

저장된 주인공으로 자피르 초원에서 시작하며, 미선택 저장은 스텔라/폴라리스 선택 화면을 표시합니다. 두 주인공은 제공된 원화를 참고한 Blender 모델과 기존 Humanoid 애니메이션을 사용합니다. 이번 전신 보완에서는 얼굴형과 머리 비율을 조정하고, 목·어깨·몸통·허리·골반·팔다리 및 의상 메시를 다시 구성합니다. 아이보리 천·남색 망토에 금색 자수 텍스처를 연결하며, 기존 52개 본과 게임 내 높이 1.8m 정규화는 유지합니다. 실행·재생성·검수 절차는 [전신·얼굴 조형 및 의상 안내](Docs/Anatomy_구현_테스트.md)를 참고하세요. 이전 단계의 얼굴 텍스처 기록은 [얼굴 개선 안내](Docs/Face_얼굴개선_테스트.md)에 남아 있습니다. **F10**으로 구역을 빠르게 이동하고 **H**로 상세 HUD를 표시합니다. `Open Open World` 메뉴도 새 섬을 엽니다. 종전 `Orbis_OpenWorld.unity`는 보존했습니다.

[통합 섬·주인공 실행 및 테스트](Docs/Island_통합섬_주인공_테스트.md) · [변경 파일 전체 목록](Docs/Island_FILES.md) · [출처와 라이선스](Credits.md)

## M1.6 주인공으로 시작

**Orbis > M1.6 > Open Character Selection**에서 Play합니다. 스텔라/폴라리스 카드 선택 → 선택 확정 → 여정 시작 순서로 기존 월드에 들어갑니다. 선택은 저장되며 다음 실행부터 이어하기가 표시됩니다.

선택한 주인공은 4인 파티의 **1번 슬롯**에 고정됩니다. **Tab**으로 화·수·풍·암·뢰를 바꾸고 **G**로 공통 원소 스킬을 사용합니다. 일반공격은 여정의 검의 물리 공격이며 **Q**는 기존 궁극기 연출입니다. 두 선택지는 별도 Stella/Polaris 모델과 Humanoid Avatar를 사용하며 기존 애니메이션·상태머신을 공유합니다.

기존 JSON 저장을 v3로 확장해 주인공 선택과 원소를 보존합니다. 데이터·API·검증은 [M1.6 구현·테스트](Docs/M16_구현_테스트.md), 전체 변경 파일은 [M1.6 파일 목록](Docs/M16_FILES.md)을 참고하세요.
## M1.5 재화·뽑기 시연

**Orbis > M1.5 > Open Gacha Demo**로 전용 씬을 열고 Play를 누릅니다. 하단 개발용 **결정 +1600 → 서약서 10장 교환 → 10회 모집** 순서로 시연합니다. 한정(마리스 픽업)·상시 배너, 18명 동료 데이터, 6종 재화, 74/90회 천장과 50/50 보장을 지원합니다.

기존 JSON 저장에 재화·보유·배너별 천장을 통합했고 M4 코인은 루멘으로 이전됩니다. 세부 기본값·API·검증은 [M1.5 구현·테스트](Docs/M15_구현_테스트.md), 변경/생성 파일 전체는 [M1.5 파일 목록](Docs/M15_FILES.md)을 참고하세요.
## 기존 마일스톤 월드 직접 검증

1. Unity Hub에서 `D:\Project\ORBIS`를 **6000.6.0f1**로 엽니다.
2. **Orbis > Art > Setup and Validate** 후 **Open Asset World**를 선택합니다.
3. Play를 누르고 Game 뷰를 클릭합니다. **F10 → ↑/↓ → Enter**로 지역을 이동합니다.

| 입력 | 동작 |
| --- | --- |
| WASD / Shift / Space / 마우스 | 이동 / 달리기 / 점프 / 카메라 |
| E / 공중 Space / 물속 Ctrl·Space | 등반 / 활공 / 잠수·상승 |
| 1~4 / 왼쪽 클릭 | HUD에 표시된 기존 파티 전환 / 원소 기본공격 콤보 |
| F / Q | NPC·상자·도전 입구 상호작용 / 기존 궁극기 연출 시험 |
| F1 / F2 / F3 | 시작 / 퍼즐 / 도전 입구로 시험 이동 |
| F4 / F5 / F6 | 보스 / NPC / 조사 표식으로 시험 이동 |
| F10 / R | 지역 선택 / 전투·퍼즐 연습 초기화 |

NPC 앞 **F**로 오늘의 위임을 수락하고 대상 지역에서 수행한 뒤 NPC **F**로 보상을 받습니다. 퍼즐은 해당 지역 원소로 **I→II→III**를 적중시키고, 도전방은 **F**로 시작해 표적 3개에 지정 반응을 만듭니다. 보스는 약점 원소 3회 적중으로 4초 노출되며, 예고 링 밖으로 피할 수 있습니다.

지역별 조합과 약점, 저장 경로, 기본값, 자동 검증 결과는 [**M4 상세 테스트 문서**](Docs/M4_구현_테스트.md), 전체 변경/생성 파일은 [**M4 파일 목록**](Docs/M4_FILES.md)을 참고하세요.

기존 마일스톤 씬과 검증 문서는 유지했습니다: [M0](Docs/M0_구현_테스트.md), [M1](Docs/M1_구현_테스트.md), [M2](Docs/M2_구현_테스트.md), [M3](Docs/M3_구현_테스트.md). 기획서 원본은 [05 기술스택·로드맵](기획서/05_기술스택_로드맵.md)에 있습니다.

