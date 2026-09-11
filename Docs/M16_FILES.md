# M1.6 주인공 선택·원소 전환 변경/생성 파일 목록

이번 작업 시작 시점 대비 수정 22개, 신규 81개, 합계 103개. Unity 메타 파일을 포함한다. 기획서 원본은 변경하지 않았다.

기존 JSON 저장 v3 통합, 주인공 선택 화면·고정 파티 슬롯·물리 일반공격·원소 전환·공통 스킬, 9개 SO, 테스트·문서로 구성한다. 기존 파일의 작업 전 원본은 TestResults/M16-before-explorer 아래에 보관한다.

## 기존 파일 수정

- Assets/Orbis/Art/Runtime/Characters/ArtCharacterRoster.cs
- Assets/Orbis/Art/Runtime/UI/ArtHud.cs
- Assets/Orbis/M0/Runtime/Player/IPlayerTraversal.cs
- Assets/Orbis/M0/Runtime/Player/PlayerMotor.cs
- Assets/Orbis/M1/Runtime/Combat/PartyCombatBridge.cs
- Assets/Orbis/M1/Runtime/Elements/ElementalActor.cs
- Assets/Orbis/M1/Runtime/Elements/ElementalReactionManager.cs
- Assets/Orbis/M1/Runtime/Party/PartyManager.cs
- Assets/Orbis/M1/Runtime/Party/PartyMember.cs
- Assets/Orbis/M15/Tests/EditMode/Persistence/EconomyPersistenceTests.cs
- Assets/Orbis/M2/Runtime/Traversal/ExplorationMotor.cs
- Assets/Orbis/M3/Runtime/Presentation/M3Presentation.cs
- Assets/Orbis/M4/Editor/M4ProjectSetup.cs
- Assets/Orbis/M4/Runtime/Orbis.M4.Runtime.asmdef
- Assets/Orbis/M4/Runtime/Presentation/M4SceneBootstrap.cs
- Assets/Orbis/M4/Runtime/Presentation/M4Session.cs
- Assets/Orbis/M4/Runtime/Progress/M4ProgressService.cs
- Assets/Orbis/M4/Tests/EditMode/M4ContentTests.cs
- Docs/M15_구현_테스트.md
- Docs/M4_구현_테스트.md
- ProjectSettings/EditorBuildSettings.asset
- README.md

## 신규 파일

- Assets/Orbis/M16.meta
- Assets/Orbis/M16/Core.meta
- Assets/Orbis/M16/Core/Data.meta
- Assets/Orbis/M16/Core/Data/ExplorerCatalog.cs
- Assets/Orbis/M16/Core/Data/ExplorerCatalog.cs.meta
- Assets/Orbis/M16/Core/Data/ExplorerDefinition.cs
- Assets/Orbis/M16/Core/Data/ExplorerDefinition.cs.meta
- Assets/Orbis/M16/Core/Data/ExplorerSkillDefinition.cs
- Assets/Orbis/M16/Core/Data/ExplorerSkillDefinition.cs.meta
- Assets/Orbis/M16/Core/Data/WayfarerWeaponDefinition.cs
- Assets/Orbis/M16/Core/Data/WayfarerWeaponDefinition.cs.meta
- Assets/Orbis/M16/Core/Orbis.M16.Core.asmdef
- Assets/Orbis/M16/Core/Orbis.M16.Core.asmdef.meta
- Assets/Orbis/M16/Core/Save.meta
- Assets/Orbis/M16/Core/Save/ExplorerSaveData.cs
- Assets/Orbis/M16/Core/Save/ExplorerSaveData.cs.meta
- Assets/Orbis/M16/Editor.meta
- Assets/Orbis/M16/Editor/ExplorerProjectSetup.cs
- Assets/Orbis/M16/Editor/ExplorerProjectSetup.cs.meta
- Assets/Orbis/M16/Editor/Orbis.M16.Editor.asmdef
- Assets/Orbis/M16/Editor/Orbis.M16.Editor.asmdef.meta
- Assets/Orbis/M16/Resources.meta
- Assets/Orbis/M16/Resources/M16.meta
- Assets/Orbis/M16/Resources/M16/Catalog.asset
- Assets/Orbis/M16/Resources/M16/Catalog.asset.meta
- Assets/Orbis/M16/Resources/M16/Explorers.meta
- Assets/Orbis/M16/Resources/M16/Explorers/polaris.asset
- Assets/Orbis/M16/Resources/M16/Explorers/polaris.asset.meta
- Assets/Orbis/M16/Resources/M16/Explorers/stella.asset
- Assets/Orbis/M16/Resources/M16/Explorers/stella.asset.meta
- Assets/Orbis/M16/Resources/M16/Skills.meta
- Assets/Orbis/M16/Resources/M16/Skills/Fire.asset
- Assets/Orbis/M16/Resources/M16/Skills/Fire.asset.meta
- Assets/Orbis/M16/Resources/M16/Skills/Lightning.asset
- Assets/Orbis/M16/Resources/M16/Skills/Lightning.asset.meta
- Assets/Orbis/M16/Resources/M16/Skills/Rock.asset
- Assets/Orbis/M16/Resources/M16/Skills/Rock.asset.meta
- Assets/Orbis/M16/Resources/M16/Skills/Water.asset
- Assets/Orbis/M16/Resources/M16/Skills/Water.asset.meta
- Assets/Orbis/M16/Resources/M16/Skills/Wind.asset
- Assets/Orbis/M16/Resources/M16/Skills/Wind.asset.meta
- Assets/Orbis/M16/Resources/M16/WayfarersBlade.asset
- Assets/Orbis/M16/Resources/M16/WayfarersBlade.asset.meta
- Assets/Orbis/M16/Runtime.meta
- Assets/Orbis/M16/Runtime/Combat.meta
- Assets/Orbis/M16/Runtime/Combat/ExplorerController.cs
- Assets/Orbis/M16/Runtime/Combat/ExplorerController.cs.meta
- Assets/Orbis/M16/Runtime/Combat/ExplorerSkillSequence.cs
- Assets/Orbis/M16/Runtime/Combat/ExplorerSkillSequence.cs.meta
- Assets/Orbis/M16/Runtime/Orbis.M16.Runtime.asmdef
- Assets/Orbis/M16/Runtime/Orbis.M16.Runtime.asmdef.meta
- Assets/Orbis/M16/Runtime/Presentation.meta
- Assets/Orbis/M16/Runtime/Presentation/ExplorerSelectionScreen.cs
- Assets/Orbis/M16/Runtime/Presentation/ExplorerSelectionScreen.cs.meta
- Assets/Orbis/M16/Runtime/Presentation/ExplorerStatusHud.cs
- Assets/Orbis/M16/Runtime/Presentation/ExplorerStatusHud.cs.meta
- Assets/Orbis/M16/Runtime/World.meta
- Assets/Orbis/M16/Runtime/World/ExplorerJourney.cs
- Assets/Orbis/M16/Runtime/World/ExplorerJourney.cs.meta
- Assets/Orbis/M16/Scenes.meta
- Assets/Orbis/M16/Scenes/M16_CharacterSelection.unity
- Assets/Orbis/M16/Scenes/M16_CharacterSelection.unity.meta
- Assets/Orbis/M16/Tests.meta
- Assets/Orbis/M16/Tests/EditMode.meta
- Assets/Orbis/M16/Tests/EditMode/Combat.meta
- Assets/Orbis/M16/Tests/EditMode/Combat/ExplorerCombatTests.cs
- Assets/Orbis/M16/Tests/EditMode/Combat/ExplorerCombatTests.cs.meta
- Assets/Orbis/M16/Tests/EditMode/Combat/Orbis.M16.CombatTests.asmdef
- Assets/Orbis/M16/Tests/EditMode/Combat/Orbis.M16.CombatTests.asmdef.meta
- Assets/Orbis/M16/Tests/EditMode/Persistence.meta
- Assets/Orbis/M16/Tests/EditMode/Persistence/ExplorerPersistenceTests.cs
- Assets/Orbis/M16/Tests/EditMode/Persistence/ExplorerPersistenceTests.cs.meta
- Assets/Orbis/M16/Tests/EditMode/Persistence/Orbis.M16.PersistenceTests.asmdef
- Assets/Orbis/M16/Tests/EditMode/Persistence/Orbis.M16.PersistenceTests.asmdef.meta
- Assets/Orbis/M16/Tests/PlayMode.meta
- Assets/Orbis/M16/Tests/PlayMode/ExplorerJourneyTests.cs
- Assets/Orbis/M16/Tests/PlayMode/ExplorerJourneyTests.cs.meta
- Assets/Orbis/M16/Tests/PlayMode/Orbis.M16.PlayModeTests.asmdef
- Assets/Orbis/M16/Tests/PlayMode/Orbis.M16.PlayModeTests.asmdef.meta
- Docs/M16_구현_테스트.md
- Docs/M16_FILES.md

## 검증 산출물

- TestResults/M16-setup.log
- TestResults/M16-EditMode.xml
- TestResults/M16-EditMode.log
- TestResults/M16-PlayMode.xml
- TestResults/M16-PlayMode.log

TestResults는 Git 제외 대상이지만 검토용으로 전달한다. Library·Temp·빌드 캐시는 전달하지 않는다. 테스트는 GUID 임시 저장만 사용한다.
