# Project Orbis — Credits

## 2026-09-11 — 주인공 룩 개발

- 공통 `ExplorerToon.shadergraph`와 커스텀 라이팅, 3단 램프 데이터, 얼굴 방향 연결, 조명·색보정은 오르비스용으로 작성했습니다. 기존 원소 연출용 재질 인터페이스를 유지합니다.
- 색상은 사용자가 제공한 `여주인공.png` 원본 픽셀에서 직접 추출했습니다. 좌표·원본 해시·HEX는 [팔레트 기록](Docs/LookDev_Palette.md)에 있습니다. 원화와 생성 텍스처의 권리를 CC0로 변경하지 않습니다.
- 여정의 검은 **방식 (b)**: 기존 Kenney Mini Dungeon 2.0의 `weapon-sword.fbx`(CC0) 날 메시를 가는 장검 비율로 수정하고, 오르비스용 골드 가드·보석·장식을 추가한 파생 애셋입니다. 원본 FBX는 보존합니다. 원화의 검을 참고한 장식은 프로젝트 제작물입니다. [Kenney 원본 라이선스](Docs/AssetLicenses/Kenney_MiniDungeon/License.txt)
- 재현 스크립트는 `Tools/LookDev/build_wayfarer_blade.py`입니다. 외부 AI 3D 서비스에 원화를 보내거나 라이선스 미확인 검을 도입하지 않았습니다.

기록 기준: 2026-09-11. 2026-09-10에 도입한 Kenney/KayKit 무료 팩 7개에 Quaternius 자연 팩 2개를 추가했습니다. 감사의 뜻으로 제작자를 표기합니다. 각 팩의 CC0 표기는 해당 외부 애셋에 적용되며, 프로젝트 전체 코드·Unity 패키지·생성 텍스처의 라이선스를 변경하지 않습니다.

## 외부 애셋

| 제작자 | 팩 / 도입 버전 | 프로젝트에서 사용하는 내용 | 라이선스 증빙 |
| --- | --- | --- | --- |
| Kenney | [Nature Kit](https://kenney.nl/assets/nature-kit) 2.1 | 나무, 바위, 절벽, 자연 소품, 석상과 표지판 | [CC0 원문·출처·캡처](Docs/AssetLicenses/Kenney_NatureKit/) |
| Kenney | [Castle Kit](https://kenney.nl/assets/castle-kit) 2.0 | 성벽, 성탑, 아치, 계단, 배너 | [CC0 원문·출처·캡처](Docs/AssetLicenses/Kenney_CastleKit/) |
| Kenney | [Modular Dungeon Kit](https://kenney.nl/assets/modular-dungeon-kit) 2.1 | 던전 바닥 및 석재 모듈 | [CC0 원문·출처·캡처](Docs/AssetLicenses/Kenney_ModularDungeonKit/) |
| Kenney | [Mini Dungeon](https://kenney.nl/assets/mini-dungeon) 2.0 | 상자, 기둥, 광산 목재 구조물, 오크 등 | [CC0 원문·출처·캡처](Docs/AssetLicenses/Kenney_MiniDungeon/) |
| Kenney | [UI Pack](https://kenney.nl/assets/ui-pack) 2.0 | 패널·버튼·게이지 프레임, 별·체크·화살표 아이콘 | [CC0 원문·출처·캡처](Docs/AssetLicenses/Kenney_UIPack/) |
| Kay Lousberg | [KayKit — Adventurers](https://kaylousberg.itch.io/kaykit-adventurers) 2.0 FREE | Knight, Mage, Ranger, Barbarian, Rogue, 한손검 및 원본 아틀라스 | [CC0 원문·출처·캡처](Docs/AssetLicenses/KayKit_Adventurers/) |
| Kay Lousberg | [KayKit — Character Animations](https://kaylousberg.itch.io/kaykit-character-animations) 1.1 | Medium 리그의 이동·점프·근접공격 클립 | [CC0 원문·출처·캡처](Docs/AssetLicenses/KayKit_CharacterAnimations/) |
| Quaternius | [Stylized Nature MegaKit](https://quaternius.com/packs/stylizednaturemegakit.html) **Standard FREE**, 2026-09-11 확보 | CommonTree_3, Pine_5, DeadTree_5, TwistedTree_2, Grass_Common_Short, Fern_1, Rock_Medium_1~3 및 해당 컬러 텍스처 | [동봉 CC0 라이선스](Docs/WorldArt/Sources/Quaternius_MegaKit_License_Standard.txt), [공식 HTML](Docs/WorldArt/Sources/Quaternius_MegaKit_Official.html), [제작자 itch HTML](Docs/WorldArt/Sources/Quaternius_MegaKit_Itch.html) |
| Quaternius | [Ultimate Stylized Nature](https://quaternius.com/packs/ultimatestylizednature.html), 2026-09-11 선별 확보 | BirchTree_1, MapleTree_1, PalmTree_1 및 해당 컬러 텍스처 | [동봉 CC0 라이선스](Docs/WorldArt/Sources/Quaternius_UltimateStylizedNature_License.txt), [공식 HTML](Docs/WorldArt/Sources/Quaternius_UltimateStylizedNature_Official.html) |

제작자 홈페이지: [Kenney](https://kenney.nl/), [Kay Lousberg](https://kaylousberg.com/). 일곱 팩의 원본 라이선스는 CC0 1.0입니다. [CC0 공식 설명](https://creativecommons.org/publicdomain/zero/1.0/)

자연 팩 제작자: [Quaternius](https://quaternius.com/). 두 팩의 공식 제품 페이지와 동봉 파일도 CC0 1.0을 명시합니다. MegaKit은 [제작자 itch 페이지](https://quaternius.itch.io/stylized-nature-megakit)의 무료 Standard ZIP, Ultimate는 [공식 Drive 폴더](https://drive.google.com/drive/folders/1IV3bXHzkNvuNWFHPi4KPx-G4ghuxIuT-)에서 받았습니다. [접근 시각·URL·SHA-256 증빙](Docs/WorldArt/Sources/Quaternius_License_Evidence.json)

Nature Kit 버전은 공식 페이지의 오래된 업데이트 표시 대신 ZIP 안의 `License.txt`에 적힌 2.1을 기록했습니다. Modular Dungeon Kit도 내부 라이선스는 2.1이며 내려받은 ZIP 파일명에는 `_1.0`이 남아 있습니다. 원본 ZIP 전체는 증빙 문서에 복제하지 않았습니다. [증빙 색인·SHA-256 대조 결과](Docs/AssetLicenses/README.md)

캐릭터 표현은 Ignis/화=Knight, Maris/수=Mage, Aura/풍=Ranger, Grom/암=Barbarian, Sparkle/뇌=Rogue로 연결하며, 현재 기본공격 규칙에 맞춰 모두 같은 한손검을 사용합니다. 캐릭터마다 원본의 머리·몸 비율을 보존한 채 전체 높이를 1.8m로 맞춥니다. 원본 FBX/PNG는 보존하고 Unity importer 설정, 프리팹, 머티리얼, AnimatorOverrideController는 별도 생성합니다. [환경·UI 실제 매핑](Docs/Art_Environment_Mapping.md)

실행 메뉴는 **Orbis > Art > Setup and Validate**, **Orbis > Art > Open Asset World**입니다. Art 카탈로그가 준비되면 M4 지역 씬 로드 후 실제 애셋 표현이 자동 연결됩니다.

## 프로젝트에서 제작한 자산

공통 `Orbis/Art/UnifiedToon` 셰이더, 물 표현, 캐릭터·환경 배치 연결, M3 Shader Graph의 효과 수식, Timeline 연출, 수학식으로 생성한 입자 마스크와 `ElementImpact.wav`는 오르비스용으로 작성했습니다. 외부 MIT 툰 셰이더, freesound 음원, 외부 BGM을 도입한 것으로 표기하지 않습니다.

M3 VFX Graph 8종의 컨텍스트 골격은 설치된 Unity Visual Effect Graph 17.6.0의 `Simple_Burst.vfx` 템플릿을 바탕으로 하고, 시스템 설정·색상·크기·속도 곡선은 프로젝트용으로 작성했습니다. 해당 Unity 템플릿은 CC0가 아니며 Unity Companion License 사본을 보존했습니다. [VFX 출처](Assets/Orbis/M3/Resources/M3/Effects/PROVENANCE.md), [Unity 라이선스 사본](Assets/Orbis/M3/Resources/M3/Effects/Unity_VFX_LICENSE.txt), [M3 자산별 제작·편집 계약](Docs/M3_Art_Assets.md)

Unity, URP, Cinemachine, Input System, Timeline, Shader Graph, Visual Effect Graph, Addressables 및 그 의존성은 각 설치 패키지의 라이선스를 따릅니다.

## Mixamo 상태

이 기록 시점에 Mixamo FBX는 도입되지 않았습니다. 현재 애니메이션은 위 KayKit CC0 팩이며, Mixamo 리타겟 경로는 사용자 자료를 기다리고 있습니다. Adobe의 공식 FAQ는 Adobe ID를 통한 무료 이용과 게임을 포함한 개인·상업 프로젝트의 로열티 없는 사용을 안내합니다. [Adobe Mixamo FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html)

실제 도입 시 출처·다운로드 설정·날짜와 라이선스 증빙을 추가하고 이 문서의 상태도 갱신합니다. 프로젝트 기획서 04의 배포 방침에 따라 Mixamo 원본이나 추출 클립을 독립 애셋 상품·공개 애셋 패키지로 재판매·재배포하지 않습니다. 이 프로젝트 방침을 CC0나 Adobe의 추가 이용허락으로 해석하지 않습니다. [수동 다운로드 및 반입 안내](Docs/Asset_Sourcing_Manual.md)


## 2026-09-11 — 통합 섬 / 스텔라·폴라리스

- 2km 지형, 높이장·도로·지면 텍스처·섬 지도·하늘과 해안 수면 셰이더는 오르비스용으로 작성했습니다. 풍차·마을·광산·첨탑·풀·꽃 배치는 위 Kenney 팩의 기존 원본을 재사용합니다. 새 상용 게임 맵이나 외부 유료 팩을 도입하지 않았습니다.
- 스텔라·폴라리스 FBX는 사용자가 제공한 `Assets/Img/여주인공.png`, `Assets/Img/남주인공.png`를 참고하여 로컬 Blender에서 제작한 1차 메시입니다. 원화 제공: 프로젝트 오르비스 사용자. 원화 자체를 CC0로 재분류하지 않습니다.
- 재현 스크립트: `Tools/Blender/create_explorers.py`. 편집 원본: `Tools/Blender/Sources/Stella.blend`, `Polaris.blend`. 게임용 결과: `Assets/Orbis/Game/Island/Models/`의 두 FBX 및 별도 Unity 프리팹·재질·Humanoid Avatar.
- 주인공의 애니메이션 7개와 임시 한손검은 위 KayKit CC0 자산을 새 Humanoid 리그에 연결했습니다. 모델·Animator는 원소 전환 시 교체하지 않습니다.
- 이번 메시 제작은 로컬 Blender 스크립트를 사용했습니다. Meshy나 다른 외부 생성 서비스로 이미지를 전송하지 않았습니다. 3D 결과는 원화의 색·의상·나침반 모티브를 따른 초기 모델이며 원화의 고정밀 복제본은 아닙니다.

## 2026-09-11 — 얼굴·헤어 텍스처 개선

- 스텔라·폴라리스의 얼굴과 금발 텍스처 3장은 사용자 제공 원화를 참고하여 내장 imagegen으로 제작했습니다. 원본 PNG는 1254×1254이며 `Assets/Orbis/Game/Island/Textures/Explorers/`에 보존합니다. 생성 프롬프트와 실제 사용 목적은 [텍스처 제작 기록](Docs/Face_Texture_Prompts.md)에 있습니다.
- 생성 텍스처를 Kenney/KayKit의 CC0 자산으로 분류하지 않습니다. 참고 원화의 제공자는 기존과 동일하며, 이 작업에서 원화의 라이선스를 변경하지 않습니다.
- 얼굴 연속 곡면·얼굴 UV·헤어 UV와 가는 헤어 층은 `Tools/Blender/polish_explorers.py`로 오르비스 모델에 추가했습니다. 이 얼굴 개선 단계에서는 두 `.blend` 편집 원본에 사용 텍스처를 포함했고, 당시의 몸·의상·휴머노이드 본과 KayKit 애니메이션을 이어서 사용했습니다.
- 이번 작업은 실제 게임용 모델·머티리얼 변경입니다. `FacePolish_*`는 Blender 검토 렌더, `Face_*`는 Unity 플레이 중 캡처이며 생성형 2D 이미지를 게임 화면으로 표시하지 않습니다.


## 2026-09-11 — 전신 조형·의상 보완

- 사용자 제공 원화 `Assets/Img/여주인공.png`, `Assets/Img/남주인공.png`의 턴어라운드·의상·천체 문양을 참고하여 얼굴형, 머리 비율, 몸통과 팔다리, 의상 메시를 조정합니다. 원화 제공자와 원화의 권리 조건은 위 기록과 동일합니다.
- `Body_Ivory_BaseColor.png`와 `Body_Navy_BaseColor.png`는 내장 imagegen으로 생성한 공통 의상 알베도입니다. 두 PNG의 실제 반환 크기는 각각 1254×1254이며 생성 원본 그대로 사용합니다. 각 주인공의 `EX_TailoredIvory`와 `EX_TailoredNavy`가 이 두 텍스처를 공유합니다. [정확한 프롬프트·제작 기록](Docs/Anatomy_Texture_Prompts.md), [원본 경로·SHA-256·임포트 설정](Docs/Anatomy_Texture_Manifest.json)
- 새 재현 파이프라인: `Tools/Blender/refine_explorer_anatomy.py`가 보존 입력 `Tools/Blender/Sources/AnatomyBaseSources/`에서 시작하고, `refine_explorer_head.py`와 `tailor_explorer_body.py`를 실행합니다. `--export`는 편집 원본 `Tools/Blender/Sources/Stella.blend`, `Polaris.blend`와 `Tools/Blender/AnatomyExports/`의 FBX를 작성합니다. Unity용 FBX와 프리팹·재질은 기존 프로젝트 경로에 연결합니다.
- 각 주인공 편집 원본에는 자신의 얼굴, 공통 헤어, 아이보리·남색 의상 등 사용 텍스처 4장을 포함하도록 구성합니다. 52개 본의 이름·휴지 자세와 기존 KayKit Humanoid 애니메이션을 공유하면서 실제 몸·의상 메시를 교체합니다.
- `AnatomyStudio_*`는 Blender 검토 렌더이고, `Anatomy_*` PNG는 Unity 씬에서 정면·사선·측면·후면·걷기·실제 공격을 확인하기 위한 캡처입니다. 최종 시각 확인과 테스트 실행 결과는 [구현·검수 안내](Docs/Anatomy_구현_테스트.md)에 기록합니다.
- 이 단계는 원화의 비율과 옷의 인상을 반영하는 조형 보완입니다. 원화의 전체 장식을 정밀 복제하거나 표정 블렌드셰이프·의상 물리 시뮬레이션을 완성한 것으로 표기하지 않습니다. 생성 의상 텍스처도 Kenney/KayKit의 CC0 자산으로 재분류하지 않습니다.

## 2026-09-11 — 월드 자연 에셋·지면 개선

- **확보 범위:** MegaKit 무료 Standard ZIP의 실제 모델 수는 68개입니다. 동봉 안내는 유료 Pro를 전체 116개 모델, 유료 Source를 전체 모델에 Unity URP·Unreal·Godot 프로젝트와 셰이더가 추가된 판으로 구분합니다. 공식 페이지 일부 이미지 설명에 다른 개수가 혼재하지만, 이번 기록은 확보한 ZIP의 라이선스·파일 목록을 따릅니다. Pro/Source판은 구매하거나 다운로드하지 않았으며 제작자의 URP 프로젝트/셰이더도 사용하지 않았습니다. [Standard 다운로드 내역](Tools/WorldDev/SourceDownloads/Quaternius_MegaKit_Standard/SourceManifest.json), [Ultimate 선별 다운로드 내역](Tools/WorldDev/SourceDownloads/Quaternius_UltimateStylizedNature_Selection/SourceManifest.json)
- **원본과 파생 에셋:** 위 Quaternius 원본 12개에서 LOD0/LOD1 FBX 24개를 만들고, 나무 7수종마다 실제 LOD0 메시를 8방향으로 무조명 촬영한 투명 빌보드를 만들었습니다. 원본 반복 UV와 컬러 텍스처를 재사용하고, 줄기/잎의 LOD 감소와 바람용 UV 데이터를 추가했습니다. 512² 컬러 이미지 15장은 원본 텍스처의 축소본입니다. 모델·빌보드·축소 텍스처는 CC0 원본의 파생 데이터이며 imagegen 결과가 아닙니다. 재현: `Tools/WorldDev/build_nature_lods.py`. [파일 매핑·삼각형 수·원본 해시](Assets/Orbis/Game/World/Nature/NatureModelManifest.json)
- **생성 지면 이미지:** `Assets/Orbis/Game/World/Ground/Textures/`의 `Meadow.png`, `Earth.png`, `Stone.png`, `Moss.png`는 내장 imagegen으로 만든 프로젝트용 알베도입니다. 실제 반환 크기는 각각 1254×1254이며 원본을 보존합니다. 이 4장은 Quaternius/Kenney/KayKit 팩의 구성품이 아니고 이번 작업에서 CC0를 부여하지 않습니다. [생성 도구·전체 프롬프트·반환 경로](Tools/WorldDev/GroundGeneration.json)
- **프로젝트 구현:** 식생 텍스처 배열 셰이더, 바람·접촉 흔들림, LOD 프리팹 구성, 지면 텍스처 연결과 이끼 표현은 오르비스용 구현입니다. 구매하지 않은 Quaternius Source판의 코드나 셰이더를 도입했다고 표기하지 않습니다.
- **라이선스 증빙 상태:** 공식 응답 HTML 3개와 동봉 라이선스 TXT 2개를 보존했습니다. Ultimate 동봉 TXT 제목의 `Ultimate Platformer Pack` 표기는 원문 그대로 두었고, 실제 Ultimate Stylized Nature 공식 제품 페이지의 CC0 표시를 함께 확인했습니다. 실제 라이선스 화면 스크린샷은 연결된 브라우저 화면이 없어 확보하지 못했으므로 기획서 04의 스크린샷 체크는 미완료입니다. HTML·제품 프리뷰·텍스트 이미지를 실제 화면 캡처로 간주하지 않습니다. [증빙 상태와 원문 파일](Docs/WorldArt/Sources/Quaternius_증빙_안내.md)

## 2026-09-11 — 마을 건축 3종·풍차·첨탑

- `House_Cottage`, `House_Merchant`, `House_Workshop`, `Windmill`, `StormSpire`는 **프로젝트 자체 제작 건축물**입니다. `Tools/WorldDev/build_architecture.py`가 외부 모델 입력 없이 로컬 Blender에서 지붕 타일·목재 골조·기단·창문·현관과 원환/나침반 장식을 생성합니다. 기존 Kenney 망루의 재색칠이나 특정 상용 게임 건축물의 추출·복제가 아닙니다.
- 건축 5종의 LOD0/LOD1 FBX 10개와 공통 재질 팔레트는 `Assets/Orbis/Game/World/Architecture/`에 연결합니다. 이 메시들을 Kenney/KayKit/Quaternius의 CC0 팩 구성품으로 분류하지 않으며, 이번 작업에서 프로젝트 제작물에 CC0를 부여하지 않습니다. 주변에 함께 배치된 기존 외부 프롭은 위 각 팩의 출처를 그대로 따릅니다.
- 원본 FBX의 실제 Unity 정면은 **-Z**이고, `WorldLandmarkBuilder`가 각 LOD 자식을 Y축 180° 회전하여 **최종 프리팹의 정면을 +Z**로 맞춥니다. 초기 매니페스트의 원본 `+Z front` 설명은 잘못된 가정으로 정정했습니다. FBX 자체를 다시 내보내는 변경은 아닙니다. [출처·좌표 계약과 정정 근거](Docs/WorldArt/Architecture_Coordinates.md)
- `Architecture_*` PNG는 실제 Blender 메시의 검토 렌더이고 `World03_*` PNG는 실제 Unity 씬 캡처입니다. Blender 재임포트 검사는 메시·UV·재질·수치와 파일 보존을 확인하며, Unity 정면 방향은 프리팹 bounds와 게임 화면에서 별도로 확인합니다. Blender 왕복검사를 Unity 시각 검증으로 표기하지 않습니다.
