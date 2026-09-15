# 캐릭터 전달 매핑 초안 — 아직 staging

2026-09-15. 이 문서는 staging에 연결된 최종 후보와 검증 결과를 정리한다. Field export·관련 회귀·Windows 빌드·Packed Player 렌더 검사는 완료했다. 타깃 전달, 모든 프레임의 완전한 접지, 화면에 표시되는 게임의 목표 FPS, GitHub 업로드를 완료했다는 보고는 아니다. 최신 통합 요약은 `Field_Animation_Final.md`를 함께 본다.

- 검사한 staging: `C:/Users/Mirim/.codex/visualizations/2026/09/10/01a08932-ad4d-7dd1-99a5-ab8a6115569e/orbis-m0`
- 원본을 읽은 타깃: `D:/Project/ORBIS`
- 아래 `Assets/…`, `TestResults/…`는 달리 적지 않으면 **staging 기준** 경로다. 타깃에 이미 전달됐다는 의미가 아니다.
- 실제 YAML의 Mesh/Avatar/Controller/Profile GUID를 `.meta`로 역참조한 자료: `Tools/CharacterPipeline/FinalMappingDraft/MappingEvidence.json`. 수집기는 같은 폴더의 `collect_mapping_evidence.py`이며 Assets를 쓰거나 Unity를 실행하지 않는다.

## 현재 바인딩 상태

보스 5종은 `TestResults/CharacterPipeline/FieldIntegration/BossVisual05/Commit.json`에서 Field 저장을 완료했다. 이 기록의 `committed_export_pending`은 저장 당시 상태이며, 이후 `CharacterFieldExport02`와 `CharacterFieldOcclusion01`이 성공했다. 실제 `Assets/Scenes/Field.unity` SHA는 commit 기록과 일치한다. 기존 gameplay root, Weakness Core, 콜라이더를 유지한 시각 자식 교체다. 현재 export 상태와 7개 출력 해시는 `FinalAll01.json`에서 재검증했다.

주인공 카탈로그도 이후 staging에서 연결을 완료했다. `TestResults/CharacterPipeline/HeroFinalBinding/FinalHeroes01/Commit.json`은 `committed_catalog_only`, `onlyApprovedCatalogFieldsChanged=true`, `assetDependenciesUnchanged=true`를 기록한다. 현재 `Assets/Orbis/Art/Resources/Art/Catalog.asset`을 다시 읽은 SHA256은 commit의 `7a9bae62b00ccf2467b774ca358311a5e5446ee7ba5581e2987c981ba9323916`과 일치하며, 아래 Stella12GripRuntime01 / Motion06GripRuntime01 두 후보를 참조한다. Field 씬은 이 catalog commit 전후 같은 해시를 유지했다. 초기 `MappingEvidence.json`의 legacy 카탈로그 목록은 그 이전 스냅샷이며 최신 바인딩은 이 commit을 기준으로 본다. staging 연결과 runtime export는 완료했으나 타깃 전달·전체 게임 수동 검수·화면 표시 FPS 검증은 별도다.

## 원본 → 주인공 모델

| 대상 | 보호하는 원본 | 선택한 geometry / Avatar FBX | 현재 선택한 runtime prefab |
|---|---|---|---|
| 스텔라 | `D:/Project/ORBIS/Assets/blend/여주인공.blend` | `Assets/Orbis/Game/Characters/Candidates/StellaIntegrated12/Stella/Stella.fbx` | `Assets/Orbis/Game/Characters/Candidates/Stella12GripRuntime01/Stella/Stella.prefab` |
| 폴라리스 | `D:/Project/ORBIS/Assets/blend/남주인공.blend` | `Assets/Orbis/Game/Characters/Candidates/OriginTrial01/Polaris/Polaris.fbx` | `Assets/Orbis/Game/Characters/Candidates/Motion06GripRuntime01/Polaris/Polaris.prefab` |

| 대상 | Geometry 삼각형 | SkinnedMeshRenderer / 메시 | 스킨 본 참조 | 몸체 머티리얼 슬롯 | Unity 임포트 정점 |
|---|---:|---:|---:|---:|---:|
| 스텔라 Integrated12 | 220,138 | 1 / 1 | 66 | 2 | 284,450 |
| 폴라리스 OriginTrial01 | 99,998 | 1 / 1 | 64 | 1 | 79,951 |

본 수는 SkinnedMeshRenderer의 전체 본 참조 수다. Unity Humanoid 매핑 본 수와 동일한 숫자가 아니다. 메시/삼각형 수는 몸체 기준으로 공용 검, 별도 아웃라인 렌더러, 파티클은 포함하지 않는다. 임포트 정점은 UV/노멀 경계 분리 때문에 Blender의 원래 정점 수와 다를 수 있다.

스텔라의 2슬롯은 `StellaIntegrated12/Stella/OriginalAtlas.mat`와 `LegPatchAtlas.mat`이다. 기존 얼굴·헤어·의상 atlas를 유지하고 다리 국소 처리 영역은 원본 맵에서 전사한 별도 atlas를 사용한다. 폴라리스는 `OriginTrial01/Polaris/OriginalAtlas.mat`를 사용한다. 각 geometry 폴더의 `Textures/`에 BaseColor/Normal/Mask가 있으며, 스텔라에는 `LegPatch_*` 맵도 있다. 원본 Mask의 G=roughness/B=metallic 데이터는 보존하지만, 현재 공용 diffuse toon은 이 PBR Mask를 입력으로 사용하지 않는다. 원본 PBR 광택이 그대로 재현됐다고 표현하지 않는다.

근거: `TestResults/CharacterPipeline/UnityImport/StellaIntegrated12/TwoMaterial.json`, `UnityImport/OriginTrial01/Polaris.json`, `RigReview/PolarisCoatV6/Polaris_rig_report.json`, `RigReview/PolarisV6GroundedExport/Polaris_grounded_export.json` 및 현재 두 runtime prefab의 직렬화 참조.

## 주인공 모션·검 연결

| 대상 | 선택 모션 Profile / Controller 폴더 | 원래 모션 저작·검수 manifest | 손잡이 보정 근거 |
|---|---|---|---|
| 스텔라 | `Assets/Orbis/Characters/Animation/Stella12GripRuntime01/Stella/`의 `MotionProfile.asset`, `Character.controller`, `Clips/*.anim` | `Assets/Orbis/Game/Characters/MotionCandidates/Stella12Stop26/Stella/MotionManifest.json` | `Assets/Orbis/Game/Characters/Candidates/Stella12GripRuntime01/Stella/GripBinding.json` → `TestResults/CharacterPipeline/UnityGripReview/Stella/Stella12Transfer01/MeasuredTransfer.json` |
| 폴라리스 | `Assets/Orbis/Characters/Animation/Motion06GripRuntime01/Polaris/`의 `MotionProfile.asset`, `Character.controller`, `Clips/*.anim` | `Assets/Orbis/Game/Characters/MotionCandidates/Motion05Stops/Polaris/MotionManifest.json` | `Assets/Orbis/Game/Characters/Candidates/Motion06GripRuntime01/Polaris/GripBinding.json` → `TestResults/CharacterPipeline/UnityGripReview/Polaris/Transfer03/MeasuredTransfer.json` |

각 Profile은 26개의 실제 `.anim`을 참조한다: Idle; Walk/WalkBack/WalkLeft/WalkRight; Run/RunBack/RunLeft/RunRight; StopLeft/StopRight; TurnLeft90/TurnRight90/TurnLeft180/TurnRight180; SteeringNeutral/SteeringLeft/SteeringRight; Air; Attack1/Attack2/Attack3; Skill/Burst/Hurt/Dead.

지상 이동·정지·회전 모션은 측정한 리그와 다리 도달 범위를 사용한 프로젝트 저작이다. 전투 3타, Skill/Burst, Hurt/Dead에는 기존 **KayKit Character Animations 1.1 / CC0** 클립을 리타겟하고 손가락 그립을 보정한 결과가 들어 있다. 슬롯별 정확한 원본은 각 Profile의 `SourceLicense`에 기록되어 있다.

- `Rig_Medium_CombatMelee.fbx`: `Melee_1H_Attack_Chop`, `Melee_1H_Attack_Slice_Diagonal`, `Melee_1H_Attack_Slice_Horizontal`
- `Rig_Medium_CombatRanged.fbx`: `Ranged_Magic_Spellcasting`, `Ranged_Magic_Summon`
- `Rig_Medium_General.fbx`: `Hit_A`, `Death_A`
- 공통 원본 경로: `Assets/ImportedAssets/KayKit/CharacterAnimations/Rig_Medium/`

이번 후보에는 **Mixamo 다운로드/FBX가 없다**. Humanoid 구성과 실제 Mixamo 파일 리타겟 검증을 혼동하지 않는다. 여정의 검은 기존 공용 무기 애셋을 유지하며, 새 후보의 오른손 소켓을 실제 그립 증거값으로 연결한다. 두 GripBinding의 검 월드 길이 측정값은 약 1.06m이고, 변경한 애니메이션 채널은 오른손 손가락 20개다. 캐릭터 루트 이동은 기존 PlayerMotor/CharacterController가 소유하며 Animator root motion은 사용하지 않는다.

## 원본 → 보스 5종

원본 폴더는 `D:/Project/ORBIS/Assets/blend/몬스터/보스몬스터/`이다. 아래 파일은 모두 그 폴더에 있는 사용자 제공 원본이다.

| 보스 / 지역 | 원본 파일 | 실제 보존 형상과 리그 | geometry / Avatar FBX |
|---|---|---|---|
| 홍염각수 / Agnia | `불보스몬스터.blend` | **6다리 + 말린 꼬리**, 사용자 승인. Generic | `Assets/Orbis/Game/Characters/BossCandidates/Import02/FireBoss/FireBoss.fbx` |
| 심해성룡 / Teluna | `물보스몬스터.blend` | **다리 없음**. 몸통·꼬리·지느러미·목·턱 Generic | `Assets/Orbis/Game/Characters/BossCandidates/Import01/WaterBoss/WaterBoss.fbx` |
| 창공령조 / Zephyr | `바람보스몬스터.blend` | **4다리 + 2날개**, 사용자 승인. Generic | `Assets/Orbis/Game/Characters/BossCandidates/Import02/WindBoss/WindBoss.fbx` |
| 지맥거신 / Granite | `바위보스몬스터.blend` | 바위 거인의 두 팔·두 다리, **Generic** | `Assets/Orbis/Game/Characters/BossCandidates/Import03/RockBoss/RockBoss.fbx` |
| 천뢰익룡 / Voltheim | `번개보스몬스터.blend` | **4다리 + 2날개**, 사용자 승인. Generic | `Assets/Orbis/Game/Characters/BossCandidates/Import02/LightningBoss/LightningBoss.fbx` |

화 보스를 초기 시각 검사에서 4족으로 잘못 분류했으나, 실제 밑면 검사에서 여섯 발을 확인하고 사용자 승인에 따라 6족을 유지했다. 이 판단 변경은 세계관 이름이나 공격 판정을 바꾼 것이 아니다.

| 보스 | 몸체 삼각형 | Unity 임포트 정점 | Generic 스킨 본 | SkinnedMeshRenderer / 메시 / 머티리얼 | 실제 motion FBX |
|---|---:|---:|---:|---|---|
| FireBoss | 79,998 | 61,457 | 30 | 1 / 1 / 1 | `Assets/Orbis/Game/Characters/BossMotionCandidates/FootV6Motion01/FireBoss/OriginalMotion.fbx` |
| WaterBoss | 79,992 | 66,646 | 20 | 1 / 1 / 1 | `Assets/Orbis/Game/Characters/BossMotionCandidates/Motion02/WaterBoss/OriginalMotion.fbx` |
| WindBoss | 119,930 | 123,189 | 31 | 1 / 1 / 1 | `Assets/Orbis/Game/Characters/BossMotionCandidates/FootV6Motion01/WindBoss/OriginalMotion.fbx` |
| RockBoss | 99,968 | 90,618 | 17 | 1 / 1 / 1 | `Assets/Orbis/Game/Characters/BossMotionCandidates/UpperV11Motion01/RockBoss/OriginalMotion.fbx` |
| LightningBoss | 119,978 | 92,238 | 31 | 1 / 1 / 1 | `Assets/Orbis/Game/Characters/BossMotionCandidates/FootV6Motion01/LightningBoss/OriginalMotion.fbx` |

각 motion FBX 옆 `SourceManifest.json`에 rest `.blend`/FBX의 정확한 경로·SHA256, 모션 FBX SHA256, 6take 이름·프레임·접촉 시점·원점 계약이 있다. `MappingEvidence.json`에도 최신 바인딩된 원본 경로를 추출했다. 현재 rest 리그는 Fire/Wind/Lightning=`TestResults/CharacterPipeline/BossRigReviewV6/{Name}/{Name}_rig_review.blend`, Water=`BossRigReviewV4/WaterBoss/WaterBoss_rig_review.blend`, Rock=`BossRigReviewV11/RockBoss/RockBoss_rig_review.blend`다.

| 보스 | 최종 연결 대상으로 만든 runtime prefab | 같은 폴더의 Profile / Controller |
|---|---|---|
| FireBoss | `Assets/Orbis/Game/Characters/Bosses/Original01/FireBoss/FireBoss.prefab` | `MotionProfile.asset`, `Boss.controller`, `Hurt.mask` |
| WaterBoss | `Assets/Orbis/Game/Characters/Bosses/Original01/WaterBoss/WaterBoss.prefab` | 동일 파일명 |
| WindBoss | `Assets/Orbis/Game/Characters/Bosses/Original01/WindBoss/WindBoss.prefab` | 동일 파일명 |
| RockBoss | `Assets/Orbis/Game/Characters/Bosses/Original01/RockBoss/RockBoss.prefab` | 동일 파일명 |
| LightningBoss | `Assets/Orbis/Game/Characters/Bosses/Original01/LightningBoss/LightningBoss.prefab` | 동일 파일명 |

모든 보스는 **프로젝트에서 형상별로 직접 저작한** Idle/Move/Attack/Hurt/Exposed/Dead 6클립이다. Attack은 30fps, 0–90프레임 중 30프레임 접촉(`AttackContact=1/3`)을 M4 예고/회복 시간에 맞춰 표현한다. 피해는 기존 M4 시계가 계산하며 AnimationEvent로 중복 계산하지 않는다. `Move`는 검수/향후 연결용 라이브러리 클립이다. 기존 고정 앵커 보스에 새로운 이동 AI를 추가한 것이 아니다.

## Field에서의 연결 위치

`Assets/Scenes/Field.unity`가 편집할 단일 필드다. 런타임 Addressables 씬 분리는 유지된다. BossVisual05 commit의 좌표·크기는 다음과 같다. 이 값들은 현재 9m 반경 경기장/기존 공격 반경 안에서 정한 **시각 표시 기본값**이며 보스의 설정상 실제 신장이나 콜라이더 크기가 아니다.

| Hierarchy의 기존 gameplay root | 보스 | 월드 위치 (x,y,z) | 표시 높이 상한 | 적용 uniform scale | 수평 반경 |
|---|---|---|---:|---:|---:|
| `Agnia Field Guardian` | FireBoss | (-495,90,378) | 2.6m | 1.93642 | 1.92923m |
| `Teluna Field Guardian` | WaterBoss | (545,12,-342) | 2.4m | 3.02815 | 2.87806m |
| `Zephyr Field Guardian` | WindBoss | (25,25,-142) | 2.8m | 1.82953 | 1.88226m |
| `Granite Field Guardian` | RockBoss | (-425,115,-412) | 3.1m | 1.63068 | 1.06858m |
| `Voltheim Field Guardian` | LightningBoss | (475,90,468) | 2.8m | 2.32863 | 2.70000m |

지상 4종은 측정 발바닥 평면 원점을 사용한다. 풍 보스의 rest 꼬리 최저점은 발바닥 평면보다 낮아서 바닥 기준과 구분해 기록했고, Idle의 꼬리 자세로 간격을 확보한다. 수 보스는 발바닥이 아닌 유영 몸통 원점이며, 기존 Weakness Core 높이에 맞춰 visual child를 로컬 y=1.1m에 둔다. 모델 앞(+Z)을 기존 접근/핵 방향으로 맞춘 Y=180° 회전은 visual wrapper에만 적용한다.

기존 guardian 시각물과 원소 crest 0–2만 교체하며 root의 위치·ID·Actor/Dummy/Weakness Core 참조와 콜라이더는 보존한다. Field에 저장한 아웃라인은 공용 `Assets/Orbis/Game/LookDev/Resources/LookDev/ExplorerOutline.mat`를 사용하며, 본·메시를 공유하는 별도 inverted-hull renderer가 보스당 1개씩 추가된다. 추가 submesh draw는 최대 5개다. 이 수는 전체 프레임 GPU 비용 측정 결과가 아니다.

## 원본 보호·라이선스·보류 항목

2026-09-15 문서 수집 시 실제 타깃 `.blend` 8개를 읽어 SHA256을 다시 계산했다. **활성 7종은 최초 SourceAudit SHA와 모두 일치**한다. 보류 Cosmo 파일의 현재 해시는 최초 감사와 다르므로 ‘원본 8개 전부 동일’이라고 기록하지 않는다.

| 대상 | 현재 SHA256 | 최초 감사와 비교 |
|---|---|---|
| Stella | `5c6b96a4dbc1e004c1fbb2d08ee3f7de8a7e45d1b67f1f9dd8fe1395be556c36` | 일치 |
| Polaris | `ede6750ab39e7f392990dd660c58ddb75ec4f530e88b22d8278a1000a7562051` | 일치 |
| FireBoss | `256f519eb6fbda3fa210e0fe5d2ceeac8445af693f57cc84af44ef8eb0c15612` | 일치 |
| WaterBoss | `f3d124634d5f1319d777a0948622e18c9281a16e78d3c1dcd1c31593d288203f` | 일치 |
| WindBoss | `f13cedba1f4a3931cb533af3952f4164f7fd7dd6204e14a2e7ab53ed04ea41aa` | 일치 |
| RockBoss | `501fb34c95c15618b7636bee8e0616f56e9a80521be3637ab83d7520db85d8ff` | 일치 |
| LightningBoss | `915e0b3f7c23596398cfc4a1e57c8ffbeb3f459eb4cded6109ad9b39c27f22c1` | 일치 |
| 보류 `D:/Project/ORBIS/Assets/blend/조력자.blend` | `34c51b4993c874205d0b5065b369fe868fc5531367053ca924820ef8e5c6d2a1` | 최초 `4e7d34f07300eab08ce720b22e826ff37f13d7fe67eed1e745887a9591c03f88`와 다름 |

Cosmo는 보류를 유지한다. 이번 문서 작업은 해당 파일의 경로·208,577,284bytes·해시만 읽었으며 새 파일 여부나 형상을 추측하지 않고 Blender 열기/임포트를 하지 않았다. staging의 `Assets/blend` 복사본을 검사한 것이 아니다. 일반 몬스터 15종은 사용자 원본 자료 대기이며, 이번 7종 매핑에 포함하지 않는다.

사용자가 제공한 모델·텍스처·콘셉트아트의 제작자/생성 도구/원 라이선스 증빙은 이 검사만으로 확인되지 않는다. 이를 Kenney/KayKit의 CC0로 표기하지 않는다. 기존 `Credits.md`의 CC0 자료, 프로젝트 저작 코드/모션, 사용자 제공 원본의 출처를 분리하는 추가 문단 후보는 `Tools/CharacterPipeline/PendingCredits/CharacterPipeline_Append.md`에 있다. 이번 문서 작업에는 새 다운로드가 없다.

## 최종 수동 검수 절차 초안

아래 절차는 현재 staging 바인딩과 Field runtime export를 대상으로 하는 수동 체크리스트다. 자동 검사 완료 범위는 `Field_Animation_Final.md`에 구분했으며, 이 목록 전체를 사람이 수행했다는 뜻은 아니다. 타깃 전달 뒤에는 대상 프로젝트, 커밋, 세이브 경로, 실제 화면 표시 성능을 함께 기록해야 한다.

1. **편집 상태:** `Orbis > Field > Open Field`를 누른다. `Assets/Scenes/Field.unity`가 열리고 Play를 누르기 전에도 지형·식생·건축·5개 guardian root가 Hierarchy에 있는지 확인한다. 각 root를 선택하고 F로 프레이밍해 모델, 핵, collider, outline 및 바닥 위치를 본다. `Legacy/Create…` 메뉴로 필드를 재생성하지 않는다.
2. **게임 진입:** Field에서 Play를 누르면 기존 exporter가 필요 시 갱신하고 `Assets/Orbis/Game/Scenes/Orbis_Island.unity`를 시작한다. 편집 원본 Field와 런타임 분할 씬의 차이는 정상이다. 새 테스트 세이브이면 선택 UI가 필드 위에 뜬다. 스텔라/폴라리스 카드의 `이 탐구자 선택` → `선택 확정` → `여정 시작 / 이어하기`를 누른다. 기존 선택값이 있으면 `GameSceneEntry`가 저장된 주인공으로 바로 시작한다.
3. **두 주인공 검사:** 한 세이브에서 주인공을 다시 고르는 기능은 없다. 기존 사용자 세이브를 삭제해서 시험하지 않는다. QA용으로 분리한 새 세이브 환경에서 스텔라와 폴라리스를 각각 검사한다. 저장 위치는 `Application.persistentDataPath/Orbis/m4-progress.json`이며 공개 reset UI는 확인되지 않았다. 자동 검수용 분리는 기존 `M4Session.UseProgressForTests(M4ProgressService)` 주입 지점을 사용한다. 두 번째 수동 세이브를 준비하지 못했으면 그쪽 검사를 완료 처리하지 않는다.
4. **이동·카메라:** Game View를 클릭해 입력을 잡는다. WASD 걷기, Shift+WASD 달리기, Space 점프, 마우스 카메라를 확인한다. 정지/재출발, 좌우·후진, 90°/180° 회전, 평지→경사→내리막 경계에서 발바닥 관통·과도한 미끄러짐·무릎 튐을 본다. Esc/Alt-Tab 후에는 입력이 멈춰야 하고 다시 잡는 첫 클릭이 공격으로 소비되지 않아야 한다.
5. **전투:** 왼쪽 클릭으로 3단 콤보를 이어서 눌러 각 타, 검 그립, 공격 중 이동/점프 제한을 확인한다. 각 타 시작 시 방향 전환은 기존 정책이다. G로 원소 스킬을 사용하고 회복 종료 뒤 이동으로 돌아오는지 확인한다. Tab으로 화/수/풍/암/뢰 원소를 바꿔 G를 반복한다. 주인공 일반공격은 물리이며 스킬 쿨다운은 전환으로 초기화되지 않아야 한다. Q는 기존 궁극기 **연출 시험**이다. 에너지 소비/새 궁극기 피해 시스템을 추가한 것으로 시험하지 않는다.
6. **파티·재시작:** 1은 고정 주인공, 2–4는 기존 동료다. 전환·공격·피격 후 돌아와 모델/Animator/검이 중복 생성되지 않는지, 기존 동료 모션이 유지되는지 본다. Play 종료 후 재실행하여 선택값과 기존 진행이 유지되는지 확인한다.
7. **보스:** 위 좌표의 5개 guardian을 각각 확인한다. 약 6m 내에서 기존 예고→공격→회복을 관찰하고 실제 피해 시점이 기존 예고와 맞는지 본다. 기존 Weakness Core를 약점 원소로 공략해 Exposed, 피격 Hurt, 처치 Dead 및 저장된 처치 복원 상태를 본다. 기본 약점은 화→수, 수→뢰, 풍→암, 암→화, 뢰→수다. 물은 유영, 화는 여섯 발, 풍/뢰는 네 발과 두 날개, 암은 팔·어깨 장식 변형을 관찰한다. 게임에서 보스 Move가 자동 이동으로 발동할 것을 기대하지 않는다.
8. **증거:** 같은 카메라의 정면/측면/손·발 확대 이미지와 보행/전투 영상을 남긴다. Field export/스트리밍 왕복, 저장 복원, 실제 PC 프레임 타임과 draw-call을 별도로 기록한다. 기존 관련 회귀 fixture 목록은 `Tools/CharacterPipeline/PendingBossAnimation/FinalRegressionPlan.md`를 사용한다.

코드 확인 지점: `Game/Editor/WorldWorkspaceMenu.cs`, `Game/Runtime/GameSceneEntry.cs`, `M16/Runtime/Presentation/ExplorerSelectionScreen.cs`, `M16/Runtime/Combat/ExplorerController.cs`, `M16/Runtime/Presentation/ExplorerStatusHud.cs`, `M0/Resources/M0Controls.inputactions`, `M0/Runtime/Input/M0Input.cs`, `M4/Runtime/Presentation/M4Session.cs`, `M4/Runtime/Boss/M4BossModel.cs`.

## 최신 완료 상태와 남은 승인/전달 확인

- After10의 두 주인공 경사·연속 내리막 검사와 상태 해제/재진입 lifecycle 2개는 통과했다. 평지 기존 경로 불변과 접지 좌표를 정량 검사했으며 약한 접촉 프레임의 잔여 오차는 유지해 보고한다. `PolarisGripTerrain02`와 `03`의 `Slope24` frame0157 JPEG가 동일한 점을 확인했으므로, 그 이미지 쌍은 -74.509mm→+0.0687mm 개선의 시각 증거로 사용하지 않는다. 이는 캡처 증거의 한계이며 추가 엔진 결함이나 재시험 필요로 판정한 것은 아니다. 모든 포즈의 외형 품질을 승인한 것으로 확대하지 않는다.
- 얼굴의 큰 명암 경계는 normal on/off 비교에서도 남는다. 공용 toon의 단계 명암/자기 그림자 정책과 원본 atlas 영향을 분리해서 본 `Stella_Face_Shading_Audit.md`의 한계를 유지한다. ‘원본 리그 손상’ 또는 ‘노멀맵만의 문제’로 단정하지 않는다. 스텔라 부츠 접합의 작은 띠와 장갑의 각진 형태도 남는다. 현재 결과를 원신 몬드 수준의 완성 아트라고 표현하지 않는다.
- 선택 UI의 두 ‘동일한 회색박스’ 문구는 실제 `ExplorerSelectionScreen.cs`에 수정 적용됐다. `PendingSelectionCopy`는 적용 전 준비 자료이며 현재 UI가 미적용이라는 뜻이 아니다.
- `RuntimeDeliveryDraft02.json`은 당시 초안이다. 최신 `TestResults/CharacterPipeline/HeroFinalBinding/FinalHeroes01/FinalAll01.json`은 Hero commit 및 Field export 상태·7개 출력 해시를 검증한 뒤 17개 시작 에셋의 재귀 의존성, 최신 runtime/UI, 파일·폴더 meta와 package 정보를 수집했다. `heroCommitValidated`, `fieldExportValidated`, `sourcesUnchangedDuringInventory`는 true이고 `needsOcclusionBake`는 false다. 이 목록은 전달 계획의 근거이며 **타깃 복사 완료 기록이 아니다**. 중간 후보·전체 TestResults를 무차별 복사하지 않는다.
- 기존 약점 Core 표지의 이동 위치, 사용자 제공 원본·파생물의 공개 배포 권리, Cosmo 재개, 일반 몬스터 원본은 답변/자료 대기다. 기존 Core 위치와 판정은 유지한다. 활성 7종 원본 보존 및 보류 Cosmo의 별도 해시 상태는 위 표 그대로다.
- 이번 갱신은 Docs만 수정했다. 실제 `D:/Project/ORBIS` 전달, GitHub 업로드, PC 종료는 아직 실행하지 않았다. 빌드/오프스크린 렌더 통과를 실제 화면 표시 FPS 달성으로 바꾸어 보고하지 않는다.
