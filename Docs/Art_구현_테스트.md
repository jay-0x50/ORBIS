# 오르비스 실제 애셋 연결 — 구현·테스트

완료된 M0~M4의 게임플레이를 유지하고 M4의 5지역에 실제 메시·텍스처·애니메이션·HUD를 연결했다. 원본 FBX/PNG는 `Assets/ImportedAssets/`, 생성한 Unity 파생 자산과 연결 코드는 `Assets/Orbis/Art/`에 있다. Mixamo 원본은 아직 없으며 현재 재생되는 클립은 KayKit Character Animations 1.1이다.

## 실행 씬

1. Unity Hub에서 `D:\Project\ORBIS`를 Unity **6000.6.0f1**로 연다.
2. **Orbis > Art > Setup and Validate**를 실행한다. 캐릭터별 Humanoid Avatar, 기존 FSM을 감싼 AnimatorOverrideController, 환경 프리팹과 공통 머티리얼·카탈로그를 생성/검증한다.
3. **Orbis > Art > Open Asset World**로 `Assets/Orbis/M4/Scenes/M4_Launcher.unity`를 연다.
4. 전체 HUD는 Game 뷰 **1280×720 이상**에서 한 화면에 표시된다. 작은 Game 뷰에서는 HUD를 스크롤할 수 있으며, Esc로 커서를 해제한 뒤 스크롤바를 사용한다. Play를 누르고 Game 뷰를 클릭한다. 처음 아그니아가 열린다. **F10 → ↑/↓ → Enter**로 다른 지역을 선택한다.

Art 연결은 M4 지역 씬 로드 직후 자동 설치된다. 기존 M0~M3 단독 프로토타입 씬은 원래 검증용 표현을 유지한다. M4의 원본 충돌체와 입력·피해·스태미나·퍼즐·보스·저장 로직은 계속 사용하고, 눈에 보이는 메시만 교체한다. 도입한 장식 프리팹에는 게임플레이 충돌체를 추가하지 않는다.

## 캐릭터 매핑

| 기존 캐릭터 / 원소 | KayKit Adventurers 2.0 FREE 모델 | 확인 위치 |
| --- | --- | --- |
| 이그니스 / 화 | Knight | 아그니아 1번 |
| 마리스 / 수 | Mage | 아그니아 2번 |
| 아우라 / 풍 | Ranger | 아그니아 4번 |
| 그롬 / 암 | Barbarian | 그라니테 산맥 4번 |
| 스파클 / 뢰 | Rogue | 아그니아 3번 |

기존 지역별 4인 파티 구성은 유지한다. 다섯 모델은 같은 SD 계열의 원본 머리·몸 비율을 유지하고 전체 키만 1.8m로 맞춘다. 기존 한손 근접공격에 맞춰 모두 `sword_1handed.fbx`를 오른손 소켓에 연결한다. Mage나 Ranger 모델 선택이 원거리 공격·스킬 추가를 의미하지 않는다.

| 기존 Animator 상태 | 현재 KayKit 클립 |
| --- | --- |
| Idle | Idle_A |
| Walk | Walking_A |
| Run | Running_A |
| Jump | Jump_Idle |
| Attack1 | Melee_1H_Attack_Slice_Horizontal |
| Attack2 | Melee_1H_Attack_Slice_Diagonal |
| Attack3 | Melee_1H_Attack_Chop |

각 모델은 자기 Humanoid Avatar를 사용한다. 원래 7상태 FSM을 유지하는 캐릭터별 override controller로 실제 Medium 리그 클립을 리타겟한다. Root Motion은 끄고 원본 클립의 회전·Y·XZ를 pose에 bake해 기존 CharacterController와 공격 타이밍이 이동을 담당한다. 무기 궤적은 실제 검 끝 소켓을 따르며, 전환 잔상은 현재 SkinnedMesh를 미리 할당한 메시 버퍼에 BakeMesh하여 표시한다.

현재 팩에는 등반·수영·활공 전용 클립이 없으므로 기존 탐사 상태의 Jump 표현을 공유한다. Q는 기존 Timeline 연출 시험이며 새 궁극기·스킬 상태를 만들지 않았다.

## 환경·UI 매핑

| 대상 | 연결한 실제 애셋 |
| --- | --- |
| 바닥·경사·높이가 있는 지역 지형 | Kenney 던전 바닥, Nature 잔디·해변·석재 플랫폼 |
| 벽·탑·지붕·배너 | Castle Kit 성벽·성탑 모듈·깃발 |
| 아그니아 | 석재 바닥·바위·관목, 기존 절벽·해안 배치에 맞춘 지형 |
| 텔루나 군도 | 해변 플랫폼·야자수·목교·카누·산호를 표현하는 관목 |
| 자피르 초원 | 잔디 플랫폼·참나무·풍차를 표현하는 탑·배너 |
| 그라니테 산맥 | 석재 플랫폼·맨바위·광석을 표현하는 돌, 기존 광산 길에 맞춘 경사 |
| 볼트하임 | 성탑·석재 바닥·기둥, 어두운 바위로 표현한 기존 구름 실루엣 |
| 원소 석상·광석 코어 | Nature statue_head·stone_tallA, 기존 원소색·진행색 연결 |
| 도전 표적·필드보스 | Mini Dungeon 오크 모델, 기존 크기·위치·약점 코어 유지 |
| 위임 NPC·상자 | KayKit Mage, Mini Dungeon chest·barrel |
| 활공기 | Castle 배너 날개·기둥 지지대, 기존 활공기 Transform과 활성 상태를 따름 |
| 패널·게이지·체크·별·화살표 | Kenney UI Pack 2.0의 실제 PNG 9종 |

전체 소스 파일과 카탈로그 키는 [환경 상세 매핑](Art_Environment_Mapping.md)에 있다. Kenney UI Pack에는 오르비스 고유 원소 그림이 없으므로 별·체크 아이콘에 기존 원소색과 캐릭터명을 함께 표시한다.

## 스타일 기본값

- 캐릭터와 환경에 프로젝트에서 작성한 URP `Orbis/Art/UnifiedToon`을 사용한다. 원본 아틀라스·UV·팩 고유 색은 유지하며 명암을 3단계로 나눈다.
- 일반 메시 아웃라인은 화면 기준 **1.5px**, 색 `(0.055, 0.065, 0.085)`로 통일한다. 두께가 없는 양면 바닥에는 뒤집힌 외곽선 면을 만들지 않는다. 투명 수면도 외곽선에서 제외한다.
- 공통 Color Adjustments는 채도 **-8**, 대비 **+5**, 색 필터 `(1, 0.98, 0.95)`이다. 기존 M3 피격 플래시와 원소 색상·VFX는 유지한다.
- 물은 공통 셰이더의 투명 머티리얼이며 기존 수영 볼륨을 사용한다. 실제 물 시뮬레이션을 추가하지 않았다.
- 1.8m 높이·1.5px 외곽선·보정값·장식 밀도는 기획서에 없는 시각 기본값으로 코드에 주석을 남겼다.

## 직접 확인할 조작

| 조작 | 확인 내용 |
| --- | --- |
| WASD, Shift, Space | 실제 캐릭터의 Idle/Walk/Run/Jump, 발 높이·카메라 추적 |
| 1~4 | 모델·텍스처·무기 변경, 이전 모델 잔상과 새 모델 출현 효과 |
| 왼쪽 클릭 연속 입력 | 기존 3단 콤보, 칼날 궤적·피격 이펙트·애니메이션 동작 |
| Q | 원본 캐릭터 메시의 원소 실루엣·Timeline 카메라·연출 종료 후 조작 복구 |
| E / 공중 Space / 물속 Ctrl·Space | 기존 등반·활공·수영, 활공기 시각 모델의 동행·숨김 |
| F2, F3, F4, F5 | 석상·도전 표적·보스·위임 NPC 모델 및 기존 F 상호작용 |
| F10 → 그라니테 산맥 → 4 | 다섯 번째 모델 Barbarian(그롬) 확인 |

퍼즐·도전·보스 원소 조합과 저장/위임 규칙은 [M4 테스트 문서](M4_구현_테스트.md)를 그대로 따른다. 평소 저장을 바꾸지 않고 검증하려면 Test Runner의 자동 테스트를 사용한다. 자동 테스트는 별도 임시 저장 경로를 주입한다.

## Mixamo 반입 대기

Adobe 로그인 후 같은 소스 캐릭터에서 `Source_TPose.fbx`, `Idle.fbx`, `Walk.fbx`, `Run.fbx`, `Jump.fbx`, `Attack01.fbx`, `Attack02.fbx`, `Attack03.fbx`를 받아 `Assets/ImportedAssets/Mixamo/`에 넣는다. 선택용 `Skill_Cast.fbx`·`Death.fbx`는 현재 FSM에 자동 연결하지 않는다. [정확한 다운로드 설정·이유·리타겟 절차](Asset_Sourcing_Manual.md)

8개 필수 파일이 모두 준비되면 **Orbis > Art > Setup and Validate**가 Mixamo 소스 Avatar와 7개 클립을 검증하고 같은 override controller에 연결한다. 파일이 일부만 있으면 경고와 함께 KayKit 세트를 유지한다. 실제 Mixamo 파일의 포즈·칼 접촉 시점은 반입 후 별도 확인해야 한다. 현재 Mixamo 리타겟 검증 완료로 간주하지 않는다.

## 검증 결과

Unity 6000.6.0f1에서 **EditMode 183/183, PlayMode 44/44 통과**, 실패·생략 0개다. 기존 M0~M4 전체 검사와 새 Art 검사 14개를 포함한다. 캐릭터 5종의 기준 자세 높이·발 위치, 실제 공격 동작, 전환·재활성화, 잔상 크기·자세 고정, 기존 이펙트 풀 재사용, 5지역 충돌·모델·UI 참조를 검증했다. 작은 Game 뷰용 HUD 스크롤 보완 후 관련 PlayMode 6개도 재검사했다(`Art-Hud-PlayMode.xml`). 지역 Addressables 콘텐츠 빌드와 플레이어 스크립트 컴파일도 성공했다. 검증 로그와 캐릭터·지역 렌더 캡처는 `TestResults/Art*`에 제공한다. 카메라 캡처에는 OnGUI HUD가 포함되지 않으며 UI 연결은 자동 검증과 Game 뷰 조작으로 확인한다.

소스·라이선스는 [Credits](../Credits.md)와 [증빙 색인](AssetLicenses/README.md), 변경 파일 전체는 [Art_FILES.md](Art_FILES.md)에 있다.

원본 Medium 리그 일부 클립에는 Humanoid Translation DoF 경고가 있다. 기본 Humanoid 설정에서 일부 팔·다리 위치 키를 사용하지 않는다는 뜻이며, 사용 중인 대각 베기(Attack2)도 왼쪽 허벅지 위치 키가 해당한다. 현재는 회전 기반 Humanoid 동작을 사용하고 원본 파일은 보존한다. 세부 경고는 `TestResults/ArtImportDiagnostics.txt`에 기록했다.
