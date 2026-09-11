# M1.5 재화·뽑기 변경/생성 파일 목록

이번 작업 시작 시점 대비 수정 8개, 신규 107개, 합계 115개. Unity 메타 파일을 포함한다. 기획서 원본은 변경하지 않았다.

기존 JSON 저장 v2 통합과 루멘 표시, M1.5 코어·매니저·22개 SO·시연 씬·테스트·문서로 구성한다. 기존 파일의 작업 전 원본은 TestResults/M15-before-economy 아래에 보관한다.

## 기존 파일 수정

- Assets/Orbis/Art/Runtime/UI/ArtHud.cs
- Assets/Orbis/M4/Runtime/Orbis.M4.Runtime.asmdef
- Assets/Orbis/M4/Runtime/Presentation/M4SceneBootstrap.cs
- Assets/Orbis/M4/Runtime/Progress/M4ProgressService.cs
- Assets/Orbis/M4/Tests/EditMode/M4ProgressTests.cs
- Docs/M4_구현_테스트.md
- ProjectSettings/EditorBuildSettings.asset
- README.md

## 신규 파일

- Assets/Orbis/M15.meta
- Assets/Orbis/M15/Core.meta
- Assets/Orbis/M15/Core/Data.meta
- Assets/Orbis/M15/Core/Data/CharacterDefinition.cs
- Assets/Orbis/M15/Core/Data/CharacterDefinition.cs.meta
- Assets/Orbis/M15/Core/Data/GachaBanner.cs
- Assets/Orbis/M15/Core/Data/GachaBanner.cs.meta
- Assets/Orbis/M15/Core/Data/GachaCatalog.cs
- Assets/Orbis/M15/Core/Data/GachaCatalog.cs.meta
- Assets/Orbis/M15/Core/Data/GachaRules.cs
- Assets/Orbis/M15/Core/Data/GachaRules.cs.meta
- Assets/Orbis/M15/Core/Data/GachaTypes.cs
- Assets/Orbis/M15/Core/Data/GachaTypes.cs.meta
- Assets/Orbis/M15/Core/Gacha.meta
- Assets/Orbis/M15/Core/Gacha/GachaEngine.cs
- Assets/Orbis/M15/Core/Gacha/GachaEngine.cs.meta
- Assets/Orbis/M15/Core/Orbis.M15.Core.asmdef
- Assets/Orbis/M15/Core/Orbis.M15.Core.asmdef.meta
- Assets/Orbis/M15/Core/Save.meta
- Assets/Orbis/M15/Core/Save/EconomySaveData.cs
- Assets/Orbis/M15/Core/Save/EconomySaveData.cs.meta
- Assets/Orbis/M15/Editor.meta
- Assets/Orbis/M15/Editor/M15ProjectSetup.cs
- Assets/Orbis/M15/Editor/M15ProjectSetup.cs.meta
- Assets/Orbis/M15/Editor/Orbis.M15.Editor.asmdef
- Assets/Orbis/M15/Editor/Orbis.M15.Editor.asmdef.meta
- Assets/Orbis/M15/Resources.meta
- Assets/Orbis/M15/Resources/M15.meta
- Assets/Orbis/M15/Resources/M15/Banners.meta
- Assets/Orbis/M15/Resources/M15/Banners/Limited_Maris.asset
- Assets/Orbis/M15/Resources/M15/Banners/Limited_Maris.asset.meta
- Assets/Orbis/M15/Resources/M15/Banners/Standard_Companions.asset
- Assets/Orbis/M15/Resources/M15/Banners/Standard_Companions.asset.meta
- Assets/Orbis/M15/Resources/M15/Catalog.asset
- Assets/Orbis/M15/Resources/M15/Catalog.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters.meta
- Assets/Orbis/M15/Resources/M15/Characters/aura.asset
- Assets/Orbis/M15/Resources/M15/Characters/aura.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/bella.asset
- Assets/Orbis/M15/Resources/M15/Characters/bella.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/celine.asset
- Assets/Orbis/M15/Resources/M15/Characters/celine.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/dorin.asset
- Assets/Orbis/M15/Resources/M15/Characters/dorin.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/finn.asset
- Assets/Orbis/M15/Resources/M15/Characters/finn.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/grom.asset
- Assets/Orbis/M15/Resources/M15/Characters/grom.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/ignis.asset
- Assets/Orbis/M15/Resources/M15/Characters/ignis.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/joy.asset
- Assets/Orbis/M15/Resources/M15/Characters/joy.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/kai.asset
- Assets/Orbis/M15/Resources/M15/Characters/kai.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/kyren.asset
- Assets/Orbis/M15/Resources/M15/Characters/kyren.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/loren.asset
- Assets/Orbis/M15/Resources/M15/Characters/loren.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/marco.asset
- Assets/Orbis/M15/Resources/M15/Characters/marco.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/maris.asset
- Assets/Orbis/M15/Resources/M15/Characters/maris.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/mila.asset
- Assets/Orbis/M15/Resources/M15/Characters/mila.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/noah.asset
- Assets/Orbis/M15/Resources/M15/Characters/noah.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/ria.asset
- Assets/Orbis/M15/Resources/M15/Characters/ria.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/sparkle.asset
- Assets/Orbis/M15/Resources/M15/Characters/sparkle.asset.meta
- Assets/Orbis/M15/Resources/M15/Characters/torvan.asset
- Assets/Orbis/M15/Resources/M15/Characters/torvan.asset.meta
- Assets/Orbis/M15/Resources/M15/Rules.asset
- Assets/Orbis/M15/Resources/M15/Rules.asset.meta
- Assets/Orbis/M15/Runtime.meta
- Assets/Orbis/M15/Runtime/Economy.meta
- Assets/Orbis/M15/Runtime/Economy/CurrencyManager.cs
- Assets/Orbis/M15/Runtime/Economy/CurrencyManager.cs.meta
- Assets/Orbis/M15/Runtime/Orbis.M15.Runtime.asmdef
- Assets/Orbis/M15/Runtime/Orbis.M15.Runtime.asmdef.meta
- Assets/Orbis/M15/Runtime/Presentation.meta
- Assets/Orbis/M15/Runtime/Presentation/M15GachaDemo.cs
- Assets/Orbis/M15/Runtime/Presentation/M15GachaDemo.cs.meta
- Assets/Orbis/M15/Scenes.meta
- Assets/Orbis/M15/Scenes/M15_GachaDemo.unity
- Assets/Orbis/M15/Scenes/M15_GachaDemo.unity.meta
- Assets/Orbis/M15/Tests.meta
- Assets/Orbis/M15/Tests/EditMode.meta
- Assets/Orbis/M15/Tests/EditMode/Core.meta
- Assets/Orbis/M15/Tests/EditMode/GachaEngineTests.cs
- Assets/Orbis/M15/Tests/EditMode/GachaEngineTests.cs.meta
- Assets/Orbis/M15/Tests/EditMode/Orbis.M15.CoreTests.asmdef
- Assets/Orbis/M15/Tests/EditMode/Orbis.M15.CoreTests.asmdef.meta
- Assets/Orbis/M15/Tests/EditMode/Persistence.meta
- Assets/Orbis/M15/Tests/EditMode/Persistence/EconomyPersistenceTests.cs
- Assets/Orbis/M15/Tests/EditMode/Persistence/EconomyPersistenceTests.cs.meta
- Assets/Orbis/M15/Tests/EditMode/Persistence/Orbis.M15.PersistenceTests.asmdef
- Assets/Orbis/M15/Tests/EditMode/Persistence/Orbis.M15.PersistenceTests.asmdef.meta
- Assets/Orbis/M15/Tests/PlayMode.meta
- Assets/Orbis/M15/Tests/PlayMode/GachaDemoTests.cs
- Assets/Orbis/M15/Tests/PlayMode/GachaDemoTests.cs.meta
- Assets/Orbis/M15/Tests/PlayMode/Orbis.M15.PlayModeTests.asmdef
- Assets/Orbis/M15/Tests/PlayMode/Orbis.M15.PlayModeTests.asmdef.meta
- Assets/Orbis/M4/Tests/EditMode/M4IoRecoveryTests.cs
- Assets/Orbis/M4/Tests/EditMode/M4IoRecoveryTests.cs.meta
- Docs/M15_구현_테스트.md
- Docs/M15_FILES.md

## 검증 산출물

- TestResults/M15-setup.log
- TestResults/M15-EditMode.xml
- TestResults/M15-EditMode.log
- TestResults/M15-PlayMode.xml
- TestResults/M15-PlayMode.log

TestResults는 Git 제외 대상이지만 검토용으로 전달한다. Library·Temp·빌드 캐시는 전달하지 않는다. 테스트는 GUID 임시 저장만 사용한다.
