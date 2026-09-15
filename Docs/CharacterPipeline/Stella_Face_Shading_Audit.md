# Stella 얼굴 셰이딩 읽기 감사

2026-09-15. 실제 기존 PNG·원본 atlas·현재 코드를 읽은 검토다. 새 렌더/GPU 작업, mask 생성, shader·mesh·UV·rig·Assets 수정은 하지 않았다.

## 판단

`Integrated12FrontKey01`의 큰 볼/코/입 주변 회색 계단 경계는 tangent normal map을 꺼도 유지된다. 따라서 **normal map만의 문제로 단정할 수 없다.** 현재 셰이더가 기존 표면 방향과 수신 그림자를 강한 3단계 색면으로 바꾸는 것이 직접 확인된 경로다. 두 비교는 같은 Idle·FBX·카메라·조명을 사용하므로 서로 다른 리그/포즈 때문에 생긴 차이는 아니다. 다만 이 두 장만으로 기존 mesh normal과 자기그림자 각각의 기여율까지 확정하거나, 모든 원본 표면 결함이 없다고 선언할 수는 없다.

원본 얼굴/헤어의 형상·UV를 다시 수정할 이유는 이 비교에서 나오지 않았다. 현 단계에서는 원본 보존 후보를 유지하고, 얼굴 전용 조명 정책 개선을 별도 항목으로 둔다.

## 실제 확인한 자료

- `TestResults/CharacterPipeline/UnityPreview/Stella/Integrated12FrontKey01/Face_NormalOn_OutlineOn.png` 및 `Face_NormalOff_OutlineOn.png`, 각각의 JSON.
- 두 JSON의 FBX SHA는 `cbd3327deefefd1de6324395b7f32cbe14041b16dbbf72909aeb2204753aa372`, Idle source pose SHA는 `112116a5f652ed9868f3cd8ccfce1e5f4d82a00730ffe7d8cf756e246b2b5caa`로 동일하다. 카메라 `(0,1.64,2.1)`, FOV 16°, key `(42,145,0)`, intensity 1.1, keyShadows=true, outline=true도 같다. normalStrength만 1/0이다.
- `Assets/Orbis/Game/Characters/Candidates/StellaIntegrated12/Stella/Textures/BaseColor.jpg`를 직접 확인했다. BaseColor/Normal/Mask 실제 파일은 모두 **2048×2048**이다. importer의 maxTextureSize=4096은 없는 원본 디테일을 만들지 않는다. atlas는 얼굴 전용 정사각 UV가 아니라 다수의 분산된 작은 chart가 섞인 전신 지도다.
- 예전 `StellaSurfaceDiagnostic01/Face_NormalOn_OutlineOn_ShadowOn.png` / `…ShadowOff.png`도 확인했다. 해당 FBX SHA는 `92d453e67c67bf2a70f611894302f56d4497248fd56e00b9112ef13f9466f58b`이며 현재 FrontKey와 다른 버전/조명 조건이다. 이 자료로 현재 정면광의 자기그림자가 원인이라고 확정하지 않는다.

1024×1024 PNG를 읽기만 하여 측정한 채널 차이(정밀한 얼굴 segmentation이 아닌 고정 사각 ROI):

| 영역 / 픽셀 좌표 `[x0,y0,x1,y1)` | RGB 평균 절대 차이 / 255 | 어느 채널이든 차이 >16인 픽셀 |
|---|---:|---:|
| 얼굴 하단 `[450,527,626,633)` | 1.2742 | 1.951% |
| 앞머리 `[350,230,650,420)` | 1.5509 | 3.721% |
| 눈/윗얼굴 `[430,462,655,536)` | 2.8731 | 6.697% |

Normal On은 코/입의 작은 변화와 눈·머리 하이라이트를 더한다. 큰 턱/볼 경계가 없어지지는 않는다. 앞머리의 색 얼룩, 눈·문양의 부드러운/뭉개진 표현도 Normal Off에 남는다. 일부 세부 표현이 BaseColor에 이미 들어 있고 원본 texel 밀도에 한계가 있는 것은 확인되지만, 각각의 얼룩을 모두 base map 탓으로 분류하려면 동일 버전의 albedo-only 비교가 추가로 필요하다.

## 현재 코드가 만드는 결과

`Assets/Orbis/Game/Editor/CharacterPipelineImport.cs`는 source normals를 Import, tangents를 Mikk 계산하며, 원본 atlas에 `_PaletteStrength=0`, `_VertexColorStrength=0`, `_FaceLighting=0`을 둔다. 이는 올바른 보존 설정이다. `EX_Face`의 UV 규칙을 새 전신 atlas에 적용하지 않는다.

`Assets/Orbis/Game/LookDev/Shaders/ExplorerToonLighting.hlsl`의 일반 경로는 `saturate(dot(normal, mainLight.direction)) * shadowAttenuation`을 구한 뒤 0.18/0.65에서 즉시 3개의 Band로 분류한다. 서로 가까운 표면 방향·shadow sample도 문턱을 건너면 큰 색면 차이가 된다. 현재 OriginalAtlas는 공통 `_ShadowColor=(0.2901961,0.31764707,0.40392157)`를 쓰므로 밝은 피부가 그늘 band에서 회청색으로 크게 변한다. spec/skinSpec은 0이고 rim은 0.11이어서 현재 큰 내부 그림자 경계를 specular 노이즈로 설명하는 것도 맞지 않는다.

기존 `_FaceLighting>0.5` 경로는 head 축과 **`UV.x-0.5`**를 이용해 cheek mask를 만들며 수신 shadow map을 무시한다. 전신 atlas에 이 값을 켜면 얼굴뿐 아니라 헤어/의상 차트에도 잘못된 좌우 그림자가 적용된다. 전역 활성화는 금지한다.

`ExplorerFaceLighting.cs`는 Humanoid Head와 양쪽 UpperArm으로 head-local forward/right를 보정해 MaterialPropertyBlock에 기록한다. 같은 block을 먼저 읽으므로 M3 속성을 유지한다. 단, 현재 importer는 원본 atlas skin에 이 컴포넌트를 자동으로 추가하지 않고, 해당 경로 자체도 material에서 꺼져 있다. 나중에 별도 얼굴 모드를 추가한다면 renderer에 이 축 공급을 명시적으로 연결하고 head turn에서도 방향을 확인해야 한다.

## 별도 개선 작업을 진행할 때의 최소 순서

1. 현재 FrontKey/FBX/Idle/camera/atlas를 그대로 고정하여 Normal Off 상태에서 **shadow attenuation만** 1로 둔 진단 그림과 albedo-only 진단 그림을 만든다. 전자는 mesh-normal band와 자기그림자를 구분하고, 후자는 원본에 그려진 명암/텍셀 한계를 구분한다. 두 진단은 배포 shader 변경을 의미하지 않는다. `receiveShadows` UI만 꺼서는 custom `GetMainLight` 경로가 실제 우회되는지 확인되지 않으므로 명시적인 진단 값을 써야 한다.
2. 원본 atlas/UV0를 그대로 읽는 **별도 얼굴 mask R8 2048²**를 첫 선택으로 검토한다. 기본은 검정, 검토된 얼굴 skin triangle만 흰색이며 hair/장식/의상은 0이다. 피부색 threshold로 머리카락까지 자동 선택하거나 atlas상의 사각형으로 얼굴을 가정하지 않는다. 원본 triangle/UV 관계로 rasterize하되 UV 중첩 chart의 face/non-face 충돌을 먼저 계수하고, 다른 chart로 번지지 않는 1~2 texel 내부 feather를 검수한다. 기존 `Mask.jpg`는 이름만으로 얼굴 semantic mask라고 사용할 수 없다.
3. 새 `_AtlasFaceMask`와 opt-in 강도(기본 0)를 별도 경로로 연결한다. 가장 작은 1차 실험은 mask 영역의 **diffuse용 normal만** animated Head forward 쪽으로 부드럽게 섞는 방식이다. outline용 geometry normal, 실제 mesh normals/tangents, albedo, alpha/dissolve/overlay 경로는 그대로 둔다. 자기그림자가 큰 원인임이 1번에서 확인될 때만 얼굴 영역의 shadow 수신 강도를 따로 조정한다. 장면 전체 그림자를 끄지 않는다.
4. 기존 analytic cheek 스타일이 필요하다면 새 모드에서만 head-local 좌우 좌표를 계산한다. `WorldPosition`과 MPB의 head-relative 얼굴 중심/Right/폭을 이용하면 원본 UV를 재배치할 필요가 없다. 기존 EX_Face 코드는 기존 모델용으로 남긴다. 깊은 그늘·역광·90° head turn에서도 얼굴만 뜨지 않는지 확인한다.

별도 mask 방식은 추가 draw/renderer/본이 없고 opt-in diffuse에서 texture sample 1회가 추가된다. R8 2048²는 mip 포함 약 5.33MiB/캐릭터다(실제 플랫폼 format 검증 필요). 저해상도/BC4 압축은 packed chart 경계 누출을 먼저 검수한 후 별도 선택한다. UV가 얼굴과 다른 부위를 공유해 안전한 mask를 만들 수 없다면, 복제된 Unity Mesh에 Color32 semantic mask를 추가하는 대안이 있다. 비용은 약 4bytes×imported vertexCount이며 기존 UV/좌표/본은 유지할 수 있으나 새 mesh 데이터/바인딩이 생기므로 현재 R8 제안보다 변경 범위가 넓다. 기존 vertex palette/alpha와도 별도 계약이 필요하다.

이 제안은 원본에 흐릿하게 그려진 눈·자수·헤어 텍셀을 복원하지 않는다. 현 버전의 원본 얼굴 인식·리깅 보존과 얼굴 조명 미세 개선은 분리해서 보고한다. 현재는 제안 문서까지만 작성했으며 구현/시각 통과 상태가 아니다.
