# 스텔라 / 폴라리스 얼굴·머리카락 개선

사용자 원화 `Assets/Img/여주인공.png`, `남주인공.png`를 기준으로 얼굴 비율, 눈·피부·입술 텍스처, 밝은 금발과 부드러운 명암을 수정한다.

## 게임에서 확인

Unity 6000.6.0f1에서 **Orbis > Game > Open Unified Island**를 열고 Play한다. 기존 선택이 있으면 해당 주인공으로 시작하며, 처음 시작하는 프로필은 스텔라/폴라리스 선택 화면을 표시한다.

- 마우스로 카메라를 돌려 얼굴과 옆모습, 머리카락의 겹침을 확인한다.
- WASD로 걷고 왼쪽 클릭으로 공격한다. 기존 Humanoid 애니메이션과 상태머신을 사용한다.
- Tab으로 원소를 바꾸고 1~4로 파티를 전환한다. 주인공으로 돌아왔을 때 같은 얼굴 텍스처가 유지된다.
- 실제 저장 파일을 삭제하지 않고 두 인물을 모두 확인하려면 Test Runner의 `Orbis.Game.Tests.ExplorerArtTests`를 실행한다. GUID 임시 프로필에서 두 선택을 각각 실행한다.

## 파일 매핑

| 용도 | 파일 |
|---|---|
| 스텔라 얼굴 | Assets/Orbis/Game/Island/Textures/Explorers/Stella_Face_BaseColor.png |
| 폴라리스 얼굴 | Assets/Orbis/Game/Island/Textures/Explorers/Polaris_Face_BaseColor.png |
| 두 인물 금발 | Assets/Orbis/Game/Island/Textures/Explorers/AshBlond_Hair_BaseColor.png |
| 게임 모델 | Assets/Orbis/Game/Island/Models/Stella.fbx / Polaris.fbx |
| 게임 프리팹 | Assets/Orbis/Game/Island/Prefabs/Stella.prefab / Polaris.prefab |
| 편집 원본 | Tools/Blender/Sources/Stella.blend / Polaris.blend |
| 수정 전 모델 원본 | Tools/Blender/Sources/FaceV1Sources/ |
| 반복 가능한 수정 스크립트 | Tools/Blender/polish_explorers.py |

얼굴은 별도 눈·코·입 부품을 겹치는 대신, 낮은 콧대와 작은 턱을 가진 연속된 곡면에 `EX_Face` 재질과 UV 텍스처를 연결한다. 원화의 홍채·속눈썹·입술 표현을 텍스처에 담고 피부와 머리카락의 과한 검은 외곽선을 줄인다. 헤어에는 뿌리에서 끝으로 이어지는 UV와 별도 금발 텍스처를 사용한다.

얼굴 명암은 부드러운 조명과 약한 그림자를 사용한다. 귀·목·팔 피부는 얼굴 PNG의 검수된 단색 위치 UV (.025, .88)를 고정 샘플해 얼굴과 색 공간이 일치하도록 했다. 셰이더의 새 옵션은 기본값에서 기존 표현을 유지하며, 이번 값은 두 주인공 재질에만 적용한다. 게임 플레이 수치·세이브 형식은 바꾸지 않는다.

## 원본 재편집

Blender에서 `Sources/Stella.blend` 또는 `Polaris.blend`를 연다. 원본은 휴머노이드 T-pose이며, 52개 본과 기존 몸·의상 스킨 가중치를 유지한다. 얼굴·헤어 수정 스크립트는 `FaceV1Sources`의 이전 원본을 다시 읽으므로 실행할 때마다 변형이 누적되지 않는다.

`Tools/Blender/audit_face_polish.py`는 수정 전후 본의 rest matrix, Head 전용 정점을 제외한 몸 정점과 가중치, 내장 PNG와 외부 상대경로를 비교한다. 두 모델 모두 52개 본이 동일하고, 몸 정점 스텔라 7,572개·폴라리스 7,699개가 동일함을 확인했다. 최종 메시 삼각형 수는 스텔라 99,000개, 폴라리스 78,434개다. 세부 결과는 `TestResults/FacePolish-Source-Audit.json`에 있다.

스크립트는 편집 원본과 `Tools/Blender/FaceExports/`의 FBX를 만든다. 수정한 FBX를 `Assets/Orbis/Game/Island/Models/`의 대응 파일에 반영한 후 **Orbis > Game > Import Explorer Characters**를 실행하면 새 Humanoid Avatar, 텍스처 임포트 설정, 툰 재질과 프리팹 연결을 갱신한다. 프로젝트에 있는 PNG 세 장은 별도로 유지한다.

## 비교 이미지와 검증

`TestResults/Face-before/`에 수정 전 화면을 보존한다. `TestResults/Face_Stella_Front.png`, `Face_Stella_ThreeQuarter.png`, `Face_Stella_Profile.png` 및 폴라리스 대응 파일은 같은 Unity 카메라 설정으로 촬영한 실제 플레이 중 모델이다. PNG에는 OnGUI HUD가 포함되지 않는다.

최종 캡처는 실제 활성 URP의 MSAA와 카메라 설정을 따르고, 지원 샘플 수를 확인한 뒤 resolve하여 저장한다. 기존 수정 전 캡처는 카메라 위치·화각은 같지만 MSAA가 없는 1-sample RT였으므로 가장자리 품질까지 동일 조건인 비교는 아니다. 게임의 MSAA 4 설정 자체는 변경하지 않았다. 밝은 헤어의 선 대비를 줄이기 위해 실제 주인공 머티리얼의 윤곽색도 부드러운 갈색으로 조정했다.

`FacePolish_*` 이미지는 별도의 Blender 검토 렌더다. Unity 실시간 캡처와 구분한다. 최종 회귀 결과는 `TestResults/Face-EditMode.xml`과 `Face-PlayMode.xml` 및 대응 로그에 보존한다. 생성 프롬프트·실제 1254×1254 PNG 해상도·출처는 `Docs/Face_Texture_Prompts.md`와 `Face_Texture_Manifest.json`에 기록한다.

Unity 6000.6.0f1에서 전체 EditMode **288/288**, PlayMode **62/62**가 통과했다. 캡처 색 공간을 게임 설정에 맞춘 후 두 주인공 테스트도 **2/2** 통과했으며 결과는 `Face-Visual-PlayMode.xml/.log`에 별도 보존한다. 최종 화면 로그는 `MSAA=4`, `sRGB=True`, `R8G8B8A8_SRGB`를 확인한다. 텍스처·UV·피부 머티리얼·가느다란 윤곽선, 별도 Humanoid Avatar, 걷기, 원소 변경 및 파티 복귀 시 얼굴 유지 등을 검증했다. 전체 회귀에는 기존 파티·전투·탐사·이펙트·재화·세이브·통합 섬 테스트가 포함된다.

이번 작업은 얼굴·머리카락의 품질 개선이며, 표정 블렌드셰이프·눈 깜빡임·머리카락 물리는 추가하지 않는다. 원화의 전체 의상과 모든 장식까지 동일한 정밀도로 완성한 모델은 아니다.
