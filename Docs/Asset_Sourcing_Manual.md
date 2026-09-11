# Mixamo 수동 다운로드·반입 안내

현재 프로젝트는 KayKit Character Animations 1.1의 CC0 클립으로 실행됩니다. Mixamo 원본은 아직 도입하지 않았고, 실제 Mixamo 파일로 리타겟 결과를 검증하지 않았습니다. 아래 8개 파일이 준비되면 기존 FSM의 애니메이션만 교체하는 경로를 사용할 수 있습니다.

## 1. 같은 캐릭터에서 8개 FBX 받기

[Mixamo](https://www.mixamo.com/)에 본인의 Adobe ID로 로그인하고, 기본 이족보행 인체 캐릭터 하나를 선택한 뒤 다운로드가 끝날 때까지 같은 캐릭터를 유지합니다. Adobe 공식 안내에는 Adobe ID를 통한 무료 이용과 캐릭터 선택·애니메이션 검색·편집·다운로드 과정이 설명되어 있습니다. [공식 FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html), [공식 애니메이션 다운로드 안내](https://helpx.adobe.com/creative-cloud/help/animate-characters-mixamo.html)

아래 파일명은 프로젝트 importer가 찾는 **정확한 이름**입니다. 다운로드 버튼에서 지정한 이름과 다르면 받은 파일을 이 이름으로 변경합니다. `Assets/ImportedAssets/Mixamo/` 바로 아래에 둡니다. 표의 검색어는 원하는 동작을 찾기 위한 예시이며, Mixamo의 특정 상품명을 보장하는 목록은 아닙니다.

| 파일명 | 필요한 동작 / 검색 예시 | Skin | 기존 Animator 상태 |
| --- | --- | --- | --- |
| `Source_TPose.fbx` | 선택한 캐릭터의 T-Pose 기준 모델 | With Skin | 재생하지 않고 소스 Avatar 생성에 사용 |
| `Idle.fbx` | 한손검 대기 / Idle | Without Skin | `Idle` |
| `Walk.fbx` | 전진 걷기 / Walking | Without Skin | `Walk` |
| `Run.fbx` | 전진 달리기 / Running | Without Skin | `Run` |
| `Jump.fbx` | 짧은 점프 또는 공중 유지 자세 / Jump, Falling Idle | Without Skin | `Jump` |
| `Attack01.fbx` | 한손검 가로 베기 / Sword Slash | Without Skin | `Attack1` |
| `Attack02.fbx` | 반대 방향 또는 대각선 베기 / Sword Attack | Without Skin | `Attack2` |
| `Attack03.fbx` | 한손검 내려찍기 / Sword Overhead | Without Skin | `Attack3` |

모든 애니메이션 FBX에는 같은 소스 캐릭터의 뼈 계층이 필요합니다. `Source_TPose.fbx`만 다른 캐릭터로 받으면 안 됩니다. 현재 자동 반입기는 `mixamorig:` 접두사가 있거나 없는 Mixamo 표준 뼈 이름을 매핑하므로, 별도 커스텀 뼈 이름을 가진 리그에는 추가 매핑이 필요합니다.

다음은 이 프로젝트의 **권장 다운로드 설정**입니다. Adobe가 의무화한 형식이나 서비스의 법적 조건은 아닙니다.

- 형식: FBX Binary (`.fbx`). 현재 화면에 FBX for Unity가 제공되면 그 옵션도 사용할 수 있습니다.
- FPS: 60. Keyframe Reduction: None으로 시작합니다.
- 걷기·달리기는 In Place 옵션이 있으면 켭니다. 루트 전진 거리 대신 기존 CharacterController가 이동을 담당합니다.
- 공격은 한 동작만 포함하도록 시작·끝을 다듬고, 클립 중간 부근에 칼날 접촉이 오는 동작을 선택합니다. 한 파일에 여러 공격을 합치지 않습니다.
- 프리뷰에서 손목 꺾임, 발 미끄러짐, 몸통 회전과 무기 파지 자세를 먼저 확인합니다. 방패가 필요한 동작은 왼손에 방패가 없는 현재 모델에서도 자연스러운지 확인합니다.

선택 다운로드는 `Skill_Cast.fbx`(시전 자세), `Death.fbx`(쓰러짐)입니다. 이 두 파일은 현재 8개 필수 입력에 포함되지 않으며 자동 연결되지 않습니다. M3 Q 연출과 M4 구조/재도전 처리에 새 Animator 상태를 추가하는 작업도 이번 반입 경로에 포함되지 않습니다. 현재 탐사 동작 역시 기존 `Jump` 표현을 공유하므로 등반·수영·활공 전용 애니메이션은 별도 연결 작업이 필요합니다.

## 2. 출처 기록과 보관

다운로드한 파일 옆에 `source-manifest.json` 또는 Markdown을 두고 각 파일의 실제 Mixamo 애니메이션 이름, 선택한 소스 캐릭터, 원본 페이지/검색 경로, 날짜, Format·FPS·Skin·In Place 설정과 SHA-256을 기록합니다. 출처 화면과 그날 확인한 라이선스 안내는 `Docs/AssetLicenses/Mixamo/`에 추가합니다. 현재 이 폴더에 실제 도입 증빙을 만들거나 Mixamo 사용 완료로 표시하지 않았습니다.

Adobe FAQ는 게임을 포함한 개인·상업 프로젝트에 캐릭터·애니메이션을 로열티 없이 사용할 수 있다고 안내합니다. 이는 Mixamo 애셋을 CC0로 바꾸는 안내가 아닙니다. [Adobe Mixamo FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html)

기획서 04의 프로젝트 배포 방침에 따라 Mixamo 원본 FBX나 추출 클립을 독립 애셋 상품·공개 애셋 패키지로 재판매·재배포하지 않습니다. 게임 반입 전에 본인의 사용에 적용되는 Adobe 약관을 확인하고 증빙을 갱신합니다. 이 안내서는 Adobe의 추가 이용허락이나 별도 법률 해석을 제공하지 않습니다. Adobe 계정의 비밀번호·쿠키·인증 토큰은 프로젝트나 증빙 문서에 넣지 않습니다.

## 3. Unity Humanoid 연결

Unity의 Play를 종료하고 **Orbis > Art > Setup and Validate**를 실행합니다. 이 메뉴가 카탈로그와 캐릭터·환경 연결 자산을 생성·갱신하고 검증합니다. 자산을 다시 만들지 않고 상태만 확인하려면 **Orbis > Art > Validate Imported Assets**를 사용합니다.

프로젝트의 `Orbis.Art.Editor.ArtCharacterImport.Build(ArtAssetCatalog catalog)`는 8개 파일이 모두 있을 때 다음 처리를 수행합니다. 하나라도 없으면 경고에 누락 파일을 표시하고 KayKit 애니메이션 전체를 유지합니다. 파일이 모두 있는데 Avatar/클립이 유효하지 않으면 오류로 중단하므로 Console 내용을 확인합니다. 반입 소스를 임의로 섞어 일부 상태만 바꾸지 않습니다.

1. `Source_TPose.fbx`를 Humanoid / Create From This Model로 설정하고, Mixamo 표준 뼈 매핑으로 소스 Avatar를 생성합니다.
2. 나머지 7개 FBX를 Humanoid / Copy From Other Avatar로 설정합니다. 여기서 복사하는 것은 **같은 소스 캐릭터의 `Source_TPose` Avatar**입니다.
3. 각 애니메이션 FBX에 양의 길이를 가진 Humanoid 클립이 정확히 1개인지 확인합니다. Idle·Walk·Run만 반복하고 Jump·공격은 반복하지 않습니다. 루트 회전·Y·XZ는 pose에 bake하며 런타임 `applyRootMotion=false`를 유지합니다.
4. KayKit 5개 모델은 **각 모델에서 생성한 자기 Avatar**를 계속 사용합니다. Mixamo 소스 Avatar를 KayKit 모델의 Animator에 대신 넣지 않습니다.
5. 기존 `M0/Resources/M0/PlayerAnimator.controller`를 기반으로 생성한 캐릭터별 AnimatorOverrideController의 7개 클립만 교체합니다. `Idle`, `Walk`, `Run`, `Jump`, `Attack1`, `Attack2`, `Attack3` 상태명·파라미터·FSM 규칙은 보존합니다.

Unity의 Copy From Other Avatar는 동일한 뼈 구조의 별도 애니메이션 파일에 사용합니다. 서로 다른 Mixamo/KayKit 리그 사이의 동작 전이는 각각 유효한 Humanoid Avatar를 통해 수행합니다. [Unity Avatar 구성](https://docs.unity3d.com/6000.0/Documentation/Manual/ConfiguringtheAvatar.html), [Humanoid 리타겟](https://docs.unity3d.com/6000.0/Documentation/Manual/Retargeting.html), [AnimatorOverrideController](https://docs.unity3d.com/6000.0/Documentation/Manual/AnimatorOverrideController.html)

KayKit의 머리·몸 비율은 바꾸지 않습니다. `ArtCharacterRoster`가 시각 모델 전체를 높이 1.8m로 균일하게 맞추며, 게임플레이 pawn 위치·콜라이더·스태미나·공격 FSM은 그대로 유지합니다. 무기는 `handslot.r`에 장착하고 궤적의 끝점은 실제 검 메시 경계에서 정합니다. `Optimize Game Objects`는 끄고 소켓 및 SkinnedMeshRenderer의 뼈 Transform을 유지해야 합니다.

생성 프리팹·머티리얼·override controller는 `Assets/Orbis/Art/Resources/Art/Characters/`에 있습니다. 빌더를 재실행하면 이 파생 자산을 갱신하므로 수동 편집은 별도 복사본과 카탈로그 연결로 관리합니다. 반입 완료 후 `ArtAssetCatalog.UsesMixamo=true` 및 `AnimationSource`가 실제 선택 소스를 나타내는지 확인하고 Credits를 갱신합니다.

## 4. 반입 후 확인할 조작

**Orbis > Art > Open Asset World**를 실행하면 `Assets/Orbis/M4/Scenes/M4_Launcher.unity`가 열립니다. Play로 실행한 뒤 지역에 진입해 아래 항목을 확인합니다. Art 카탈로그가 있으면 `ArtScenePresentation`이 각 M4 지역 씬 로드 후 자동 적용되므로 다른 지역으로 이동한 뒤에도 같은 검사를 할 수 있습니다. 실제 Mixamo 파일이 없는 현재 상태에서 아래 검사가 통과했다고 간주하지 않습니다.

- 대기·걷기·달리기·점프: 발 높이, 전진 방향, 머리·손목 포즈와 착지 후 Idle 복귀를 확인합니다.
- 기본공격 3단: 기존 시간은 0.50 / 0.55 / 0.65초입니다. 공격 중 Animator의 시간은 FSM 정규화 시간으로 샘플링하므로 다운로드한 원본 클립 길이가 공격 판정 시간이나 피해량을 바꾸지 않습니다.
- 각 타의 접촉 구간: 현재 유효 판정 시간은 0.16–0.27 / 0.18–0.30 / 0.23–0.40초입니다. 칼날이 이 구간에 목표를 지나가는지 확인하고, 맞지 않으면 애니메이션의 시작·끝이나 선택 클립을 조정합니다. 피해를 주는 Animation Event를 추가하지 않습니다.
- 캐릭터 전환: 다섯 모델의 텍스처·소켓·아바타가 바뀌고 기존 퇴장 잔상, 출현 효과, 원소 실루엣과 무기 궤적이 올바른 모델을 따르는지 확인합니다.
- 공격 중 전환 제한, 등반·수영·활공 진입/이탈, Q 연출 취소와 지역 이동 후에도 캐릭터 Animator가 멈추거나 다른 모델의 Avatar를 사용하는 일이 없는지 확인합니다.

현재 KayKit 기본 매핑은 Idle_A / Walking_A / Running_A / Jump_Idle / Melee_1H_Attack_Slice_Horizontal / Melee_1H_Attack_Slice_Diagonal / Melee_1H_Attack_Chop입니다. 문제를 분리할 때 필수 Mixamo 파일을 별도 로컬 보관 폴더로 옮기고 빌더를 실행하면 이 CC0 세트로 돌아갑니다.

