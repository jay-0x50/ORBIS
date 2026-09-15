# 여정의 검 소켓 조사와 임시 파지 검수

2026-09-14. 현재 검/새 주인공 리그를 읽고 **별도 검수 씬만** 제작했다. 게임 카탈로그, 프리팹, 공격/이동 클립과 소켓은 변경하지 않았다. 최초 후보는 손가락과 손잡이 간 중첩이 있어 채택 보류이며, 실제 근접 렌더 후 보정한다.

## 현재 무기 계약

| 항목 | 실제 파일 근거 / 값 |
|---|---|
| 모델 | `Assets/Orbis/Game/LookDev/Models/WayfarerBlade.fbx` |
| 제작 원본 | `Tools/LookDev/Sources/WayfarerBlade.blend` |
| 프리팹 | `Assets/Orbis/Game/LookDev/Resources/LookDev/WayfarerBlade.prefab` |
| 원본 SHA256 | `df01e0bd8367c02546ad3eaa0dd8a0f10575a7487e1a1638f5407c233bc5e1f2` |
| FBX SHA256 | `87ea7c713bc9db1bdcc0a511df9b1161f4c9c835f734ffbed298a5880c6e1521` |
| 비용 | 한 메시/한 재질, 1,281 vertices / 2,418 triangles |
| 실제 길이 / 가드 폭 | 1.06m / 약 0.227m |
| 손잡이 | 중심 원점. Blender Z -0.079~+0.063, 타원 반경 X 0.011~0.014 / Y 0.009~0.011m |
| Blender 축 | +Z 날끝, -Y 장식면, +X 가드 폭 |
| Unity 메시 축 | +Y 날끝, +Z 장식면, +X 가드 폭 |
| 궤적 기준 | 기존 `TrailTipLocalPosition=(0,0.94,0)` |
| 라이선스 | Kenney Mini Dungeon `weapon-sword.fbx` CC0 파생. `Credits.md`, `Docs/AssetLicenses/Kenney_MiniDungeon/License.txt`, `TestResults/LookDev/Step03_WayfarerBlade_RoundtripAudit.json` |

참조 파일은 `D:/Project/ORBIS` 기준이다. 신규 소싱/다운로드나 무기 재제작은 수행하지 않았다.

## 현재 부착 코드

`Assets/Orbis/Art/Runtime/Characters/ArtCharacterRoster.cs`의 `BuildView`는 캐릭터 표시 높이를 1.8로 정규화한 뒤 `RightHand` 하위에 검을 만들고 카탈로그의 위치/회전/스케일을 적용한다. 주인공 카탈로그는 `Assets/Orbis/Art/Resources/Art/Catalog.asset`이다.

| 캐릭터 | Bone | Local Position / Euler | 기존 Local Scale |
|---|---|---|---|
| Stella | RightHand | (0,0,0) / (0,0,0) | (0.92494375, 0.92494375, 0.92494375) |
| Polaris | RightHand | (0,0,0) / (0,0,0) | (0.9340079, 0.9340079, 0.9340079) |

`ExplorerLookWeapon.cs`는 예전 Avatar의 실제 손 lossyScale 역수를 측정해 이 값을 저장했다. 새 Avatar에 해당 숫자를 무조건 복사하면 검의 실제 길이를 보장하지 못한다. 새 imported RightHand의 위치/축/정규화 후 lossyScale을 다시 측정해야 한다. 관절 이름 계약을 유지하는 것만으로 다른 손바닥 형상에서 zero offset 파지가 맞는 것은 아니다.

## 준비한 검수 도구와 결과

- `Tools/CharacterPipeline/review_hero_grip.py`: 새 리그와 기존 검을 read-only 로드한다. 기존 zero 소켓의 Blender 대응 배치와, 실제 손바닥 중심에 위치시킨 별도 파지 후보를 저장한다.
- `TestResults/CharacterPipeline/RigReview/StellaGrip01/Stella_grip_review.blend`
- `TestResults/CharacterPipeline/RigReview/StellaGrip01/Stella_grip_report.json`
- `TestResults/CharacterPipeline/RigReview/PolarisGrip01/Polaris_grip_review.blend`
- `TestResults/CharacterPipeline/RigReview/PolarisGrip01/Polaris_grip_report.json`

각 보고서에 원본 불변 SHA, RightHand rest world matrix, 제안 검 matrix, 손가락별 임시 quaternion을 기록했다. 이 matrix는 **Blender source bone 공간**이며 Unity Euler가 아니다. 실제 Unity 임포트 축 대조 없이 런타임 값으로 전사하지 않는다.

최초 손가락 굽힘은 검수를 위한 Proximal 55° / Intermediate 70° / Distal 40°이며 최종 애니메이션이 아니다. 검 손잡이를 원래의 타원 단면/테이퍼로 근사하여 정점 침투를 측정했을 때 Stella 11개, Polaris 22개 정점이 2mm 이상 들어가며 최대 약 7mm다. 실제 표면 교차와 손가락 접촉을 근접 렌더로 확인한 뒤 조정할 예정이다. 이 수치는 정점 진단이며 메시 면의 충돌을 완전히 증명하지 않는다.

Stella의 실제 동일 앵글 6장(기존/제안 × 정면/위/손바닥)은 `StellaGrip01/RenderReview`에 저장했다. Top/Front/Palm을 직접 확인했으며, 제안 배치의 손바닥 중심·검 방향은 자연스러워졌지만 일부 손가락이 손잡이를 관통하고 엄지/손바닥 간격이 남았다. 현재 각도/소켓은 **파지 완성으로 채택하지 않는다**.

Polaris도 `PolarisGrip01/RenderReview`의 기존/제안 6장을 실제 렌더링해 확인했다. zero 소켓은 열린 손바닥 위로 가드/검신이 지나가므로 새 리그에 그대로 재사용할 수 없다. 후속 `PolarisGrip02`, `PolarisGrip03`는 실제 손잡이의 테이퍼 타원 단면과 손 정점을 사용해 소켓 위치와 손가락 굽힘만 보정했다. 메시·UV·웨이트·본 rest는 변경하지 않았다.

| 후보 | 2mm 이상 손잡이 내부인 정점 | 최소 추정 간격 | 판정 |
|---|---:|---:|---|
| Grip01 | 22 | 약 -7mm | 최초 제안, 채택 보류 |
| Grip02 | 9 | -3.148mm | 접촉 개선, 일부 관절 굽힘 부자연스러움 |
| Grip03 | 8 | -3.144mm | 관절 각도 범위 제한, Unity 전사 검수 후보 |

Grip02/03의 Front/Top/Palm 세 앵글을 각각 실제 렌더링했으며, Grip03 Palm/Top을 직접 검수했다. 손바닥 접촉은 개선됐지만 새끼손가락과 엄지 배치는 추가 확인이 필요하다. 정점 기반 타원 근사는 삼각형 표면 충돌을 보증하지 않는다.

`export_polaris_grip_pose.py`는 Grip03의 source rest/posed world matrix와 실제 검 정점을 기록한다. `HeroGripTransfer.cs`는 공통 rest 본 위치로 좌표계·스케일·이동을 측정하고 world 회전 차이를 각 Unity 본 축에 전사한다. 검의 축도 실제 imported/source 정점의 부호 축 순열을 대조한다. 이후 Humanoid muscle 왕복과 같은 앵글 6장을 확인한다. 현재 이 도구는 검수용이며 실제 카탈로그/공격·이동 클립에 파지값을 적용하지 않는다.

통과 전에는 이 포즈나 소켓을 게임에 적용하지 않는다. 최종 승인에는 손바닥/엄지/각 손가락의 접촉, 가드/손목 장식 간 간섭, 공격/이동 시 검의 부착과 기존 궤적 보존을 확인해야 한다.

## 실제 Unity 전사와 파지 보정(2026-09-15)

`UnityGripReview/Polaris/Transfer01`부터 실제 같은 앵글 이미지로 비교했다. 원본/Unity rest 본 대응 오차 최대0.000500mm, 직접 finger pose 전사0.000292mm, 검 geometry 대응0이다. 측정된 대응은 반사(det=-1)를 포함한다. 표의 축 설명 중 ‘+X 가드 폭’은 폭의 방향만 뜻하며, 실제 Blender +X가 Unity +X로 보존된다는 뜻이 아니다.

`Transfer02`에서 HumanPose 왕복 후 손 자체를 기준으로 재측정했다. 손가락 본 최대6.50mm/손 표면18.95mm의 차이가 남으므로 상체 이동만으로 설명할 수 없다. `Transfer03`에서는 원본 직접 포즈를 목표로 오른손20개 Humanoid muscle만 제한 범위 안에서 보정했다. 같은 실제 이미지의 FittedHumanoid_Palm/Top 확인 및 수치상 본3.48mm/표면8.36mm로 개선됐다. 본 rest/웨이트/메시/검은 바꾸지 않았다.

최종 기록의 검 길이는1.0600008m다. 검수 소켓 위치(.0076154,.0921066,-.0252170),Euler(6.2021,6.0689,268.7546)는 해당 Polaris Avatar 전용이며 Stella에 복사하지 않는다. 새 Idle 정규화 후 손의 실제 scale을 다시 측정해 검의 world scale을 유지해야 한다.

`Tools/CharacterPipeline/PendingGrip/CharacterGripCandidateInstaller.cs`는 별도 runtime 후보의20개 오른손 커브만 교체하는 준비 도구다. 원래 body/root/contact/action 커브는 유지하며, 플레이 영상·궤적 확인 전에는 카탈로그를 교체하지 않는다. 현재 파지 보정은 후보 단계이고, 발 IK/이동 결과와 별개로 검증한다.
