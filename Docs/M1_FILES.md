# M1 변경·생성 파일 목록

대상: `D:\Project\ORBIS`. 이번 M1 작업을 시작하기 직전의 파일 해시를 기준으로 구분했습니다. `.meta`는 Unity 참조 안정성을 위해 함께 포함합니다.

| 상태 | 경로 |
|---|---|
| 수정 | `Assets/Orbis/M0/Runtime/Combat/BasicAttackCombo.cs` |
| 수정 | `Assets/Orbis/M0/Runtime/Player/PlayerMotor.cs` |
| 수정 | `Assets/Orbis/M0/Runtime/Presentation/M0SceneBootstrap.cs` |
| 생성 | `Assets/Orbis/M1.meta` |
| 생성 | `Assets/Orbis/M1/Editor.meta` |
| 생성 | `Assets/Orbis/M1/Editor/M1ProjectSetup.cs` |
| 생성 | `Assets/Orbis/M1/Editor/M1ProjectSetup.cs.meta` |
| 생성 | `Assets/Orbis/M1/Editor/Orbis.M1.Editor.asmdef` |
| 생성 | `Assets/Orbis/M1/Editor/Orbis.M1.Editor.asmdef.meta` |
| 생성 | `Assets/Orbis/M1/Runtime.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Combat.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Combat/PartyCombatBridge.cs` |
| 생성 | `Assets/Orbis/M1/Runtime/Combat/PartyCombatBridge.cs.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Elements.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Elements/ElementalActor.cs` |
| 생성 | `Assets/Orbis/M1/Runtime/Elements/ElementalActor.cs.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Elements/ElementalReactionManager.cs` |
| 생성 | `Assets/Orbis/M1/Runtime/Elements/ElementalReactionManager.cs.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Elements/ElementTypes.cs` |
| 생성 | `Assets/Orbis/M1/Runtime/Elements/ElementTypes.cs.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Orbis.M1.Runtime.asmdef` |
| 생성 | `Assets/Orbis/M1/Runtime/Orbis.M1.Runtime.asmdef.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Party.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Party/PartyManager.cs` |
| 생성 | `Assets/Orbis/M1/Runtime/Party/PartyManager.cs.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Party/PartyMember.cs` |
| 생성 | `Assets/Orbis/M1/Runtime/Party/PartyMember.cs.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Presentation.meta` |
| 생성 | `Assets/Orbis/M1/Runtime/Presentation/M1SceneBootstrap.cs` |
| 생성 | `Assets/Orbis/M1/Runtime/Presentation/M1SceneBootstrap.cs.meta` |
| 생성 | `Assets/Orbis/M1/Scenes.meta` |
| 생성 | `Assets/Orbis/M1/Scenes/M1_CrystallizePrototype.unity` |
| 생성 | `Assets/Orbis/M1/Scenes/M1_CrystallizePrototype.unity.meta` |
| 생성 | `Assets/Orbis/M1/Scenes/M1_PartyPrototype.unity` |
| 생성 | `Assets/Orbis/M1/Scenes/M1_PartyPrototype.unity.meta` |
| 생성 | `Assets/Orbis/M1/Tests.meta` |
| 생성 | `Assets/Orbis/M1/Tests/EditMode.meta` |
| 생성 | `Assets/Orbis/M1/Tests/EditMode/ElementalReactionTests.cs` |
| 생성 | `Assets/Orbis/M1/Tests/EditMode/ElementalReactionTests.cs.meta` |
| 생성 | `Assets/Orbis/M1/Tests/EditMode/Orbis.M1.EditModeTests.asmdef` |
| 생성 | `Assets/Orbis/M1/Tests/EditMode/Orbis.M1.EditModeTests.asmdef.meta` |
| 생성 | `Assets/Orbis/M1/Tests/PlayMode.meta` |
| 생성 | `Assets/Orbis/M1/Tests/PlayMode/M1IntegrationTests.cs` |
| 생성 | `Assets/Orbis/M1/Tests/PlayMode/M1IntegrationTests.cs.meta` |
| 생성 | `Assets/Orbis/M1/Tests/PlayMode/Orbis.M1.PlayModeTests.asmdef` |
| 생성 | `Assets/Orbis/M1/Tests/PlayMode/Orbis.M1.PlayModeTests.asmdef.meta` |
| 생성 | `Docs/M1_구현_테스트.md` |
| 생성 | `Docs/M1_FILES.md` |
| 수정 | `ProjectSettings/EditorBuildSettings.asset` |
| 수정 | `README.md` |
| 생성 | `Tools/Test-M1.ps1` |

소스·설정·문서 합계: **51개**.

추가 검증 결과(버전 관리 제외):

- `TestResults/M1-setup.log`
- `TestResults/M1-EditMode.xml`, `TestResults/M1-EditMode.log`
- `TestResults/M1-PlayMode.xml`, `TestResults/M1-PlayMode.log`
- `TestResults/M1_Prototype.png`

기획서 01~05, Packages, M0 씬·입력 에셋·애니메이션·재질은 변경하지 않았습니다. 기존 소스 수정은 위 M0 런타임 3개 파일의 연결 지점 추가에 한정됩니다.
