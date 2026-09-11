# 탐구자 체형·의상·얼굴형 보완 — 구현 및 확인 안내

이 문서는 전신·얼굴 조형, 의상 텍스처 연결과 재생성·검수 절차를 기록합니다. 아래 조형 수치는 소스에 정한 아트 기본값이며, 최종 모델 계측값·Unity 시각 확인·테스트 실행 결과는 마지막 절에 기록했습니다.

## 이번 작업 범위

- `Assets/Img/여주인공.png`, `Assets/Img/남주인공.png`의 오른쪽 턴어라운드를 기준으로 스텔라·폴라리스의 전신 비율, 몸통 실루엣, 의상 형태와 얼굴형을 보완합니다.
- 얼굴·머리카락 텍스처와 함께 실제 3D 형상 및 의상의 UV를 검토합니다. 목·어깨·흉곽·허리·골반의 연결, 팔다리 및 턱의 형태를 전신과 측면에서 확인합니다.
- `EX_TailoredIvory`, `EX_TailoredNavy`는 실제 몸·의상 메시의 연속 UV에 새 의상 알베도를 연결합니다. 기존 얼굴 패스에서 사용한 몸을 그대로 두는 작업이 아니라, 목·몸통·팔다리·옷의 형상을 다시 구성하는 범위입니다.
- 기존 52개 본의 이름과 휴지 자세, Humanoid 리타겟 경로, 기존 애니메이션·FSM을 유지합니다. 모델이 바뀌어도 이동·공격·파티·원소 전환 규칙은 그대로 사용합니다.
- 몸통/얼굴 조형과 의상 표현의 보완이 범위이며, 게임플레이 확장이나 밸런스 변경은 포함하지 않습니다.

## 조형 기본값과 텍스처 매핑

원화의 턴어라운드를 눈으로 비교하여 정한 아트 기본값입니다. 원화에 물리 치수가 없으므로 정밀 계측값으로 해석하지 않습니다.

| 항목 | 소스 기본값 | 적용 의미 |
|---|---|---|
| 스텔라 벨트 | authoring Z = 1.095 | 허리선을 위로 정리하는 조형 기준 |
| 폴라리스 벨트 | authoring Z = 1.083 | 남성 몸통·골반 비율에 맞춘 조형 기준 |
| 머리 크기 | XY ×0.95, Z ×0.93 | Blender authoring 기준점 (0,0,1.50) 주변 축소 |
| 머리 위치 | authoring Z −0.015 | 조정된 머리와 새 목의 연결 |
| 게임 내 전체 높이 | 기존 Idle 기준 1.8m | 소스 비율을 보존한 기존 런타임 전체 정규화 |

벨트·머리 수치는 게임 씬에서의 월드 높이가 아닙니다. 52개 본의 위치를 바꾸는 대신 가중치가 있는 메시 형상과 옷 패널을 조정합니다. 목·어깨·흉곽·허리·골반을 전후좌우에서 이어 보고, 기존 머리 텍스처 위에 코·볼·입 주변·턱의 실제 깊이와 헤어 실루엣을 확인합니다.

| 재질 역할 | 사용 파일 | 대상 및 UV |
|---|---|---|
| EX_TailoredIvory | `Body_Ivory_BaseColor.png` | 두 주인공의 밝은 상의·소매·옷자락 등. 패널별 UV 0..1 |
| EX_TailoredNavy | `Body_Navy_BaseColor.png` | 두 주인공의 남색 옷·망토. 문양 중심 U≈0.5, V≈0.25 |
| EX_Face | `Stella_Face_BaseColor.png` 또는 `Polaris_Face_BaseColor.png` | 각자의 얼굴 전체 UV |
| EX_Skin | 자신의 얼굴 PNG | 검수된 피부 좌표 (0.025, 0.88) 고정 샘플로 목·귀·팔의 색 일치 |
| EX_Hair / EX_HairLight | `AshBlond_Hair_BaseColor.png` | 공통 금발 흐름 텍스처 |

텍스처는 `Assets/Orbis/Game/Island/Textures/Explorers/`에 있습니다. 새 의상 두 장은 내장 imagegen 생성 결과를 그대로 복사한 **1254×1254** PNG이며 두 주인공이 공유합니다. 2048을 요청했으나 반환 해상도를 확대하지 않았습니다. 각 캐릭터의 Blender 편집 원본에는 자신의 얼굴 1장·공통 헤어 1장·의상 2장, 총 4장의 사용 이미지를 포함하도록 구성합니다. [프롬프트와 제작 기록](Anatomy_Texture_Prompts.md), [원본 경로와 SHA-256](Anatomy_Texture_Manifest.json)

Unity는 sRGB·Trilinear·mipmap·원본 NPOT 크기 보존·무압축 설정으로 새 의상 알베도를 임포트합니다. 새 옷 재질의 BaseColor는 흰색, UV scale(1,1)/offset(0,0), 외곽선은 0.35px입니다. `ExplorerArtImport`에서 두 새 재질의 누락, 텍스처·UV 연결과 임포트 옵션을 확인합니다.

원화의 비율과 의상 인상을 반영하는 단계이며 모든 장식의 정밀 복제, 표정 블렌드셰이프, 망토·치마의 Cloth 물리 완성을 포함하지 않습니다. 의상은 기존 Humanoid 본 가중치로 변형되므로 실제 걷기와 공격 장면에서 간섭을 확인해야 합니다.

## Blender에서 다시 생성

프로젝트 루트에서 설치된 Blender로 실행합니다.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python Tools/Blender/refine_explorer_anatomy.py -- --export
```

처리 순서:

1. `Tools/Blender/Sources/AnatomyBaseSources/Stella.blend`, `Polaris.blend`를 보존 입력으로 엽니다. 현재 출력 파일을 다시 입력해 크기 보정이 누적되지 않게 합니다.
2. `refine_explorer_head.py`로 머리 조형을, `tailor_explorer_body.py`로 실제 몸·의상 메시와 UV를 구성합니다. `refine_explorer_anatomy.py`가 두 모듈을 함께 실행하고 52개 본의 휴지 자세 및 가중치 합을 확인합니다.
3. `--export`는 `Tools/Blender/Sources/Stella.blend`, `Polaris.blend`와 `Tools/Blender/AnatomyExports/Stella.fbx`, `Polaris.fbx`를 작성합니다. 스크립트가 Unity의 Assets FBX를 직접 덮어쓰지는 않습니다. `--export` 없이 실행하면 검토 렌더와 계측 JSON을 만들며, 빠른 검토에는 `--fast --preview`를 사용할 수 있습니다.
4. Blender 검토 후 두 FBX를 `Assets/Orbis/Game/Island/Models/Stella.fbx`, `Polaris.fbx`로 복사합니다. `.blend` 원본은 Tools에 둡니다.
5. Unity **Orbis → Game → Import Explorer Characters**를 실행합니다. `ExplorerArtImport.Build()`가 Humanoid Avatar와 텍스처·재질을 연결하고 `Island/Prefabs/`의 두 프리팹 및 기존 Art 카탈로그를 갱신합니다.
6. 아래 Unity 정면·사선·측면·후면·걷기·공격 캡처로 게임에서 보이는 상태를 확인합니다.

Blender의 `TestResults/AnatomyStudio_*`는 조형 검토용 스튜디오 렌더입니다. Unity 씬 캡처인 `TestResults/Anatomy_*` PNG와 구분합니다. 소스 계측 JSON은 `Anatomy_Stella_Model.json`, `Anatomy_Polaris_Model.json`이며 원본 입력·본 보존·메시 수와 조형 기본값을 기록합니다.

## 직접 실행

1. Unity에서 `Assets/Orbis/Game/Scenes/Orbis_Island.unity`를 열거나 **Orbis → Game → Open Unified Island**를 선택합니다.
2. **Play**를 누릅니다. 저장된 탐구자 선택이 있으면 해당 캐릭터로 시작합니다. 선택값이 없으면 선택 화면에서 스텔라 또는 폴라리스를 선택합니다.
3. 마우스로 카메라를 돌려 정면·옆면·뒷면의 목, 어깨, 허리선, 골반, 의상 겹침을 확인합니다.
4. **WASD**로 걷고 **마우스 왼쪽 버튼**으로 기본 공격을 합니다. 팔·몸통·소매 및 치마/코트 자락이 동작 중 찢어지거나 몸에 심하게 파고드는지 봅니다.
5. **Tab**으로 원소를 전환하고 **2 → 1**로 동료에서 탐구자로 돌아옵니다. 얼굴과 의상 텍스처가 유지되는지 확인합니다.

개인 저장 파일을 지우지 않고 두 모델을 모두 비교하려면 아래 자동 검증을 사용합니다. 테스트는 임시 프로필을 만들고 종료 시 정리합니다.

## 자동 검증과 캡처

**Window → General → Test Runner → PlayMode**에서 `Orbis.Game.Tests.ExplorerArtTests`의 기존 두 테스트를 실행합니다. 각각 스텔라와 폴라리스를 선택합니다. 새 테스트 항목을 추가하지 않고 두 테스트 안에서 확인을 확장합니다.

검증하는 항목:

- 선택한 탐구자만 활성화되는지, 올바른 Humanoid Avatar와 기존 애니메이션 컨트롤러를 사용하는지 확인합니다.
- 얼굴·머리카락·의상 텍스처가 실제 런타임 재질에 연결되어 있는지 확인합니다. 의상 텍스처는 각 변이 최소 1024픽셀이며 UV가 정점 수와 일치하고 유효해야 합니다. 반복 UV는 허용합니다.
- 기존 Idle 기준 모델 높이 1.8m와 발바닥 정렬 확인을 유지합니다. 목과 벨트의 위치는 생성 코드의 수치를 복제하는 테스트로 고정하지 않고 캡처에서 원화와 비교합니다.
- 실제 걷기로 위치와 다리 본이 변하는지 확인합니다. 이어서 기존 `BasicAttackCombo.RequestAttack` 경로로 지상 1타를 실행하고 실제 모터·Animator의 공격 상태를 확인합니다. 공격 캡처는 임의로 포즈를 지정한 이미지가 아닙니다.
- 걷기·공격·원소 전환·파티 복귀 후에도 같은 얼굴과 의상 맵을 사용하는지 확인합니다.

출력 위치는 프로젝트의 `TestResults/`입니다. 아래 전신 정면·사선·측면·후면·걷기·공격은 각 주인공 6장씩 총 12장을 생성하고 검수했습니다. 이 개수는 캡처 수이며 테스트 통과 횟수가 아닙니다.

| 파일 패턴 | 확인 목적 |
|---|---|
| `Anatomy_Stella_Front.png`, `Anatomy_Polaris_Front.png` | 전신 정면의 어깨·허리·골반 및 의상 비율 |
| `Anatomy_{이름}_ThreeQuarter.png` | 전신 35도에서 흉곽·몸통 깊이와 의상 겹침 |
| `Anatomy_{이름}_Profile.png` | 전신 측면의 목·몸통·골반 및 팔다리 실루엣 |
| `Anatomy_{이름}_Back.png` | 등, 망토, 뒤쪽 의상과 머리카락 |
| `Anatomy_{이름}_Walk.png` | 기존 걷기 동작 중 다리·의상 변형 |
| `Anatomy_{이름}_Attack.png` | 실제 기본 공격 1타 중 어깨·팔·몸통 변형 |
| `Face_{이름}_Front.png`, `_ThreeQuarter.png`, `_Profile.png` | 얼굴 정면·35도·측면 |
| `Island_{이름}_Unity.png` | 기존 전신 비교 화면 |

캡처는 실제 씬의 Main Camera 설정과 URP 후처리를 복사하고, 별도의 조명이나 배경을 만들지 않습니다. 전신은 1200×1600, 얼굴은 1024×1024 해상도입니다. 활성 URP와 원래 카메라의 MSAA 설정, 그래픽 장치의 지원 샘플 수, 프로젝트 색 공간에 따른 sRGB 변환을 반영합니다. 출력에만 별도 미화 효과를 추가하지 않습니다.

## 최종 기록 — 2026-09-11

- Unity 6000.6.0f1에서 최종 FBX와 두 의상 텍스처를 임포트하고 두 Humanoid 프리팹을 갱신했습니다. `Anatomy-Art.log`의 프로세스 종료 코드는 0입니다.
- 전체 **EditMode 288/288, PlayMode 62/62**, 실패·건너뜀 0. 별도 시각 검증 실행도 기존 두 테스트 **2/2** 통과했습니다. 근거는 `Anatomy-EditMode.xml`, `Anatomy-PlayMode.xml`, `Anatomy-Visual-PlayMode.xml`입니다.
- `Anatomy-Source-Audit.json`: 52개 본의 행렬·계층 보존, 유한하고 정규화된 본 가중치, 얼굴·헤어·의상 UV, 모델별 4개 PNG 내장 및 외부 파일 SHA 일치 검사 통과. 원본·FBX·PNG 11개 파일은 읽기 전용 audit 전후 SHA가 같습니다.
- 목의 측면 폴리곤은 각 모델 218개를 확인했고 모두 바깥쪽을 향합니다. 원통의 열린 면 방향 때문에 Unity에서 목 전체가 갈색 아웃라인으로 보이던 현상은 닫힌 목 메시로 수정했습니다.
- 각도별 전신 12장을 직접 검수했습니다. 걷기에서 스텔라 허벅지가 치마 앞단을 관통하던 부분은 밑단과 앞 옷자락을 Hips/UpperLeg에 혼합 스키닝하여 수정했습니다. 허리는 Hips에 고정하고 밑단의 다리 영향은 최대 65%로 정한 아트 기본값입니다.
- 캡처에서는 URP가 destination 연결을 소유하게 하고 resolve 전에 이전 렌더 타깃을 복원합니다. 수정 후 집중 검사와 전체 PlayMode의 폴라리스 사선 화면 모두 정상 색상으로 확인했습니다.

| 최종 Blender 소스 | 메시 수 | 삼각형 수 | 소스 높이 | Unity Idle 높이 |
|---|---:|---:|---:|---:|
| Stella | 13 | 180,996 | 1.683767m | 1.8m |
| Polaris | 14 | 205,526 | 1.700301m | 1.8m |

삼각형 수는 현재 편집 원본의 조형 밀도입니다. LOD 또는 플랫폼별 최적화를 마친 수치로 해석하지 않습니다. 시각 검수는 저장된 정지 각도와 걷기·기본 공격 표본에 대한 결과이며 모든 가능한 동작의 옷 간섭을 보장하지는 않습니다. 원화와의 정밀 일치, 표정 및 천 물리는 남아 있습니다.

수정 전 비교 이미지는 `TestResults/Anatomy-before/`, 전달 시 원본 소스 백업은 `TestResults/Anatomy-before-refine/`에 보관합니다. 전체 변경 파일은 [Anatomy_FILES.md](Anatomy_FILES.md)에 정리했습니다.
