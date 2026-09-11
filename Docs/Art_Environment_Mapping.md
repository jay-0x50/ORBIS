# Kenney 환경·UI 매핑

`Orbis.Art.Editor.ArtEnvironmentImport.Build(ArtAssetCatalog)`는 아래의 실제 FBX와 PNG를 카탈로그에 연결합니다. FBX/PNG 원본 바이트를 수정하지 않고 Unity importer 설정은 `.meta`에 저장합니다. 생성 프리팹과 재질은 `Assets/Orbis/Art/Resources/Art/Environment/` 아래에 있으며 재실행하면 이 생성물과 카탈로그의 `Environment`, `Icons` 목록을 갱신합니다. 수동 편집은 원본을 유지한 별도 프리팹에서 진행해야 합니다.

프리팹은 렌더러 경계의 XZ 중심, 최저 Y=0에 피벗을 맞추고 원본 메시의 비율과 임포트 스케일을 유지합니다. 모델마다 실제 크기가 다르므로 배치 코드에서 렌더러 경계에 맞춰 크기를 조절합니다. 시각 모델에 콜라이더를 추가하지 않습니다. 이동·수영·등반·공격용 기존 게임플레이 콜라이더는 배치 코드가 관리합니다.

Nature Kit는 FBX에 기록된 나무껍질·잎·돌 등의 diffuse 색을 유지합니다. Castle Kit, Modular Dungeon Kit, Mini Dungeon은 서로 다른 각 팩의 `Models/Textures/colormap.png`를 명시적으로 연결합니다. 파생 재질은 프로젝트 공통 `DefaultToonMaterial`을 사용하며 `_BaseColor`, `_BaseMap`, UV scale/offset을 보존합니다. 팔레트 텍스처는 색 번짐 방지를 위해 Point/no-mipmap, UI는 Bilinear/no-mipmap/Clamp로 임포트합니다.

## 실제 환경 모델

아래 `원본`의 공통 접두사는 `Assets/ImportedAssets/Kenney/`입니다. 생성 경로는 `Assets/Orbis/Art/Resources/Art/Environment/{Key}.prefab`입니다.

| Key | 원본 | 사용 의미 / 대체 여부 |
| --- | --- | --- |
| floor_stone | ModularDungeonKit/Models/template-floor.fbx | 기존 키 이름을 유지한 얇은 양면 바닥 템플릿. 원본 UV는 아틀라스의 흙 무늬 구역을 사용하며 메시 두께는 0. |
| ground_grass | NatureKit/Models/platform_grass.fbx | 잔디 플랫폼 |
| ground_stone | NatureKit/Models/platform_stone.fbx | 실제 두께와 옆면이 있는 석재 플랫폼. 절벽·육지 볼륨에 사용. |
| ground_dirt | MiniDungeon/Models/dirt.fbx | 흙 바닥 |
| wall_stone | CastleKit/Models/wall.fbx | 성벽 |
| rock_large | NatureKit/Models/rock_largeA.fbx | 원본 이름은 rock이지만 실제 재질은 복숭아색 흙과 청록색 잔디. 색상 임포트 오류가 아님. |
| rock_small | NatureKit/Models/rock_smallA.fbx | 작은 바위 |
| rock_bare | NatureKit/Models/stone_largeA.fbx | 풀 없는 실제 돌색 바위. 그라니테·아그니아 장식과 기본 대체 모델. |
| tree_oak | NatureKit/Models/tree_oak.fbx | 참나무 |
| tree_pine | NatureKit/Models/tree_pineTallA_detailed.fbx | 큰 침엽수 |
| tree_palm | NatureKit/Models/tree_palm.fbx | 야자나무 |
| bush | NatureKit/Models/plant_bushDetailed.fbx | 관목 |
| crystal | NatureKit/Models/stone_tallA.fbx | **각진 세로 돌을 광물 표식으로 대체 사용. 전용 수정 메시가 아님.** |
| bridge | NatureKit/Models/bridge_wood.fbx | 목재 다리 |
| column | MiniDungeon/Models/column.fbx | 기둥 |
| arch | CastleKit/Models/tower-square-arch.fbx | 성탑 아치 |
| tower | CastleKit/Models/tower-square-base.fbx + tower-square-mid-windows.fbx + tower-square-top-roof-high.fbx | 원본 3조각을 경계 높이에 따라 수직 적층한 완성 탑 |
| fence | NatureKit/Models/fence_simple.fbx | 목재 울타리 |
| barrel | MiniDungeon/Models/barrel.fbx | 나무통 |
| crate | MiniDungeon/Models/chest.fbx | **전용 운송상자 대신 뚜껑 있는 보물상자 메시를 공유.** |
| chest | MiniDungeon/Models/chest.fbx | 보상 상자 |
| lamp | NatureKit/Models/campfire_stones.fbx | **등기구 대신 모닥불·돌 받침 소품. 광원/VFX 추가 없음.** |
| statue | NatureKit/Models/statue_head.fbx | 석상 머리 |
| enemy | MiniDungeon/Models/character-orc.fbx | 실제 오크 캐릭터 모델. FBX의 idle 클립을 복사해 반복 재생하는 생성 컨트롤러 연결 |
| banner | CastleKit/Models/flag-banner-long.fbx | 세로 깃발 |
| roof | CastleKit/Models/tower-square-top-roof-high.fbx | 성탑 지붕 |
| stairs | CastleKit/Models/stairs-stone.fbx | 석재 계단 |
| board | NatureKit/Models/sign.fbx | 표지판 |
| cliff | NatureKit/Models/cliff_large_rock.fbx | 절벽 실루엣 |
| cave | NatureKit/Models/cliff_cave_rock.fbx | 동굴 입구 |
| cliff_slope | NatureKit/Models/cliff_blockSlope_rock.fbx | 경사 절벽 |
| ground_sand | NatureKit/Models/platform_beach.fbx | 해변 플랫폼 |
| bridge_stone | CastleKit/Models/bridge-straight.fbx | 석재 다리 |
| mine_support | MiniDungeon/Models/wood-support.fbx | 광산 목재 지지대 |
| mine_structure | MiniDungeon/Models/wood-structure.fbx | 광산 목재 구조물 |
| boat | NatureKit/Models/canoe.fbx | 카누 |
| obelisk | NatureKit/Models/statue_obelisk.fbx | 돌 오벨리스크 |
| coin | MiniDungeon/Models/coin.fbx | 3D 동전 소품(UI PNG가 아님) |
| shield | MiniDungeon/Models/shield-round.fbx | 3D 원형 방패(UI PNG가 아님) |

## 실제 런타임 적용

`Assets/Orbis/Art/Runtime/ArtScenePresentation.cs`는 `M4SceneBootstrap`이 있는 다섯 지역 씬에만 카탈로그 기반 외형을 설치합니다. 아그니아는 M2에서 만들어진 지형을 M4 씬 안에서 재사용합니다. M0~M3의 개별 프로토타입 씬을 이 장식 코드가 직접 변경하지 않습니다.

`Assets/Orbis/Art/Runtime/World/ArtWorldPresentation.cs`는 기존 렌더러 이름과 크기를 읽어 imported prefab을 배치하고 기존 회색박스 렌더러만 숨깁니다. 씬의 CharacterController, WaterVolume, ClimbableSurface, 목표 Collider와 상호작용 좌표는 그대로 유지합니다. 생성 시 보존한 원본 비율과 별개로, 런타임 `SpawnFit`은 기존 지형 범위를 맞추기 위해 축별 스케일을 사용할 수 있습니다.

| 런타임 대상 | 현재 실제 키 / 방식 |
| --- | --- |
| 퍼즐 석상 / 도전 표적 / 보스 | statue / enemy / enemy. 석상 순서 표식과 보스 약점 중심은 crystal. 기존 원소·진행 색을 모델에 연결. |
| NPC / 보상 상자 | NPC는 캐릭터 카탈로그의 두 번째 모델. 상자는 chest. NPC 옆에 barrel과 crate 배치. |
| 자피르 지면 / 텔루나 섬 / 기타 지역 지면 | ground_grass / ground_sand / floor_stone. 기타 지역의 두꺼운 지형은 ground_stone으로 구분. |
| 넓은 Bedrock / Seabed | 원래 지면 높이와 충돌을 유지하고 시각 타일 높이는 0.045m로 제한. 평원·해저가 깊은 흙섬 격자로 보이지 않게 얇게 덮음. |
| 그라니테 Eastern High Mine / Terrace / Mesa | 바위 대체 규칙이 아닌 지형 볼륨 규칙으로 처리. 기존 고지대 상면 높이와 경사로 연결을 유지. |
| 벽·경계·보스 문 양옆 | wall_stone을 길이 방향으로 반복 배치. |
| 걸을 수 있는 경사·Road·Switchback·Cove Exit·Ascent | 기존 회전과 경사를 유지해 지면 타일 배치. M2 Gentle Shore는 실제 웨지의 네 꼭짓점으로 경사를 계산. |
| 섬 연결 Footbridge / Causeway / 광산 침목 | bridge. 현재 석재 다리 전용 키 bridge_stone은 별도 카탈로그 준비 항목. |
| 풍차 탑 / 중심 / 날개 | tower / barrel / banner. 풍차 날개는 기존 정적인 배치를 유지. |
| 폭풍 탑 / 지붕·처마 / 램프 기둥·첨탑 | tower / roof / column. 전용 lamp 키는 아직 사용하지 않음. |
| 산호 | 정확히 Coral Main Branch와 Coral Fork 이름만 bush. Coral Cave Entrance의 기둥·지붕은 이 규칙에 포함하지 않음. |
| 비콘·원소 캡·광물·조사 표식 | crystal. 원본은 수정 전용 메시가 아닌 각진 돌. |
| 구름 / 기타 미분류 덩어리 | rock_bare. 별도 구름 모델이나 날씨 시뮬레이션은 추가하지 않음. |
| 수면 | ground_dirt의 메시를 얇게 맞춘 후 전용 WaterMaterial 적용. 원본 흙 컬러맵으로 물을 그리는 방식이 아님. |

지역 장식은 지면 레이캐스트 결과 위에 배치합니다. 자피르는 참나무와 관목, 그라니테는 풀 없는 돌과 작은 광물 표식, 볼트하임은 기둥과 관목, 아그니아는 풀 없는 돌과 관목을 사용합니다. 텔루나는 실제 육지점 `(6,0,-10)`, `(-6,0,-11)`, `(-31,0,13)`, `(-13,0,5)`, `(29,0,37)`, `(28,0,16)`에 야자나무·관목을 두고, 도착 섬 해안 `(8,-0.15,-9)`에는 높이 0.7m의 boat를 둡니다. 이 수치는 장식용 기본값이며 이동 경로나 충돌을 추가하지 않습니다.

카탈로그에 들어 있다고 모두 현재 씬에서 사용되는 것은 아닙니다. `rock_large`, `rock_small`, `tree_pine`, `arch`, `fence`, `lamp`, `stairs`, `board`, `cliff`, `cave`, `cliff_slope`, `bridge_stone`, `mine_support`, `mine_structure`, `coin`, `shield`는 현재 장식에서 직접 참조하지 않는 준비 항목입니다. 지역 테마 보강 시 기존 절벽의 수직 외피에 cliff, 퍼즐·통로에서 벗어난 텔루나/그라니테 배경에 cave, 광산의 기존 목재 구조부에 mine_support를 사용할 수 있습니다. 이 제안은 현재 배치 완료 목록과 구분합니다.

원본 색상 검사 결과 `rock_largeA`의 diffuse 값은 dirt `(0.8862745,0.5137255,0.3411765)`, grass `(0.172549,0.8470588,0.7215686)`이며 생성 재질에도 보존됩니다. 풀 없는 `rock_bare`를 별도 추가한 이유입니다. `floor_stone`은 양면·두께 0 평면이므로 볼륨을 만들기 위해 Y 스케일만 늘릴 수 없습니다. 실제 두께가 있는 `ground_stone`을 볼륨에 사용하고, 얇은 양면 메시에는 뒤집힌 외피 외곽선을 겹치지 않도록 처리합니다.

## UI PNG

모두 실제 `Assets/ImportedAssets/Kenney/UIPack/PNG/Grey/Double/`의 PNG 원본을 참조합니다. 중립 회색 계열을 사용하므로 GUI tint로 지역·상태 색상을 곱할 수 있습니다. 원본 버튼의 모서리와 테두리 역시 이미지에 포함되어 있으며 현재 ArtHud는 GUIStyle.border를 지정해 패널 모서리를 보존합니다. ArtHud가 실제로 사용하는 키는 panel, bar_back, bar_fill, star, check, arrow_right입니다. button, arrow_left, close는 카탈로그에 준비되어 있지만 현재 키보드 조작 HUD에서 직접 그리지 않습니다.

| Key | PNG | 의미 |
| --- | --- | --- |
| panel | button_square_border.png | 정사각형 테두리 버튼을 패널 프레임으로 사용 |
| button | button_rectangle_border.png | 직사각형 버튼 프레임 |
| bar_back | slide_horizontal_grey.png | 가로 게이지 배경 |
| bar_fill | slide_horizontal_grey_section_wide.png | 가로 게이지 채움 구간 |
| star | star.png | 별 |
| check | icon_checkmark.png | 완료 체크 |
| arrow_left | arrow_basic_w.png | 왼쪽 화살표 |
| arrow_right | arrow_basic_e.png | 오른쪽 화살표 |
| close | icon_cross.png | 닫기 |

UI Pack에 heart/coin/gear/shield 의미의 PNG는 없습니다. 이 이름의 UI 이미지를 추정하거나 생성하지 않았습니다. Mini Dungeon의 coin/shield는 환경용 3D 메시입니다.

## 출처와 원본 보존

모두 Kenney 공식 페이지에서 내려받은 무료 CC0 팩입니다. 각 폴더의 `License.txt`를 원문 그대로 보존했습니다. 다섯 라이선스는 개인·교육·상업 프로젝트 사용을 허용하고 크레딧 표기는 의무가 아니라고 명시합니다. 프로젝트에서는 선택한 파일의 출처를 남깁니다. 원문에 연결된 라이선스는 [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/)입니다. 자연 팩 버전은 공식 페이지의 오래된 표시 대신 압축 안의 라이선스 표기 2.1을 기록했습니다.

| 팩 | 공식 출처 | 압축 내부 버전 | 원문 라이선스 |
| --- | --- | --- | --- |
| Nature Kit | https://kenney.nl/assets/nature-kit | 2.1 | Assets/ImportedAssets/Kenney/NatureKit/License.txt |
| Castle Kit | https://kenney.nl/assets/castle-kit | 2.0 | Assets/ImportedAssets/Kenney/CastleKit/License.txt |
| Modular Dungeon Kit | https://kenney.nl/assets/modular-dungeon-kit | 2.1 (ZIP 파일명은 1.0) | Assets/ImportedAssets/Kenney/ModularDungeonKit/License.txt |
| Mini Dungeon | https://kenney.nl/assets/mini-dungeon | 2.0 | Assets/ImportedAssets/Kenney/MiniDungeon/License.txt |
| UI Pack | https://kenney.nl/assets/ui-pack | 2.0 | Assets/ImportedAssets/Kenney/UIPack/License.txt |

배포에 포함된 [라이선스 증빙 목록](AssetLicenses/README.md)에서 각 Kenney 팩의 `source.json`과 `selected-files.sha256.csv`를 확인할 수 있습니다. 예를 들어 [Nature Kit 출처 기록](AssetLicenses/Kenney_NatureKit/source.json)은 다운로드 URL·ZIP 파일명·크기·SHA-256·조회 시각을, [선별 파일 해시](AssetLicenses/Kenney_NatureKit/selected-files.sha256.csv)는 실제 임포트 파일의 원본 상대 경로·프로젝트 경로·크기·SHA-256을 기록합니다. CastleKit, ModularDungeonKit, MiniDungeon, UIPack도 각 `Docs/AssetLicenses/Kenney_팩명/` 폴더에 같은 형식으로 보관합니다. 출처 기록의 `Archive`가 가리키는 `Downloads/AssetSwap/`은 제작용 staging 경로이며 원본 ZIP 자체는 배포 파일에 포함하지 않습니다. 기존 선별 파일에 `NatureKit/fence_simple.fbx`, `MiniDungeon/dirt.fbx` 두 원본을 추가하여 총 145파일(87 FBX, 53 PNG, 5 License.txt), 4,432,664바이트입니다. 그중 빌더는 39 프리팹 키에 39개의 서로 다른 FBX를 사용하고 UI 9 PNG를 연결합니다. ground_stone/rock_bare는 기존 선별 FBX를 추가 참조하므로 원본 파일 수나 다운로드 용량은 늘어나지 않았습니다.

적용 검증은 Unity 메뉴 `Orbis/Art/Setup and Validate` 또는 `Orbis.Art.Editor.ArtProjectSetup.SetupAndValidate`에서 수행합니다. 이 명령이 환경 빌더를 호출하며 모델 렌더러·재질·콜라이더 없음·UI 참조를 검사합니다. 이 빌더 자체는 Unity를 별도로 실행하지 않습니다. 기본 임포트에 idle가 없는 오크 FBX가 들어오면 명확한 경고를 남기고 원본 rest pose를 유지합니다. 나머지 누락 모델·팔레트·UI 이미지·빈 메시 경계·프리팹 저장 실패는 예외로 보고합니다.
넓은 M2 Ground도 높이 0.045m의 시각 덮개로 처리한다. 모서리가 둥근 자연 플랫폼 사이로 빈 공간이 보이지 않도록 원본 평면 floor_stone을 실제 지면보다 0.06m 아래에 깔고 공통 지면색을 적용한다. 충돌면과 길의 폭은 바꾸지 않는다.
