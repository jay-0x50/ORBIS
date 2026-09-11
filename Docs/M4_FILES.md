# M4 변경·생성 파일 목록

M4 작업 시작 시점과 비교한 소스 목록이다. 수정 13개, 신규 101개, 합계 114개이며 Unity 메타 파일을 포함한다. 기획서 01~05는 변경하지 않았다.

지역별 4인 구성은 기존 캐릭터를 재사용한다. M1 피해 배율 연결과 M2 퍼즐·도전의 선택적 원소/반응·재연습 설정은 기존 호출의 기본 동작을 유지한다.

## 기존 파일 수정

- `.gitignore`
- `Assets/Orbis/M1/Runtime/Elements/ElementalActor.cs`
- `Assets/Orbis/M1/Runtime/Elements/ElementalReactionManager.cs`
- `Assets/Orbis/M1/Runtime/Presentation/M1SceneBootstrap.cs`
- `Assets/Orbis/M2/Runtime/Content/ChallengeRoom.cs`
- `Assets/Orbis/M2/Runtime/Content/ChallengeRoomModel.cs`
- `Assets/Orbis/M2/Runtime/Content/FieldElementPuzzle.cs`
- `Assets/Orbis/M2/Runtime/Content/OrderedFirePuzzleModel.cs`
- `Assets/Orbis/M2/Runtime/Presentation/M2SceneBootstrap.cs`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `ProjectSettings/EditorBuildSettings.asset`
- `README.md`

## 신규 파일

- `Assets/AddressableAssetsData.meta`
- `Assets/AddressableAssetsData/AddressableAssetSettings.asset`
- `Assets/AddressableAssetsData/AddressableAssetSettings.asset.meta`
- `Assets/AddressableAssetsData/AssetGroups.meta`
- `Assets/AddressableAssetsData/AssetGroups/Default Local Group.asset`
- `Assets/AddressableAssetsData/AssetGroups/Default Local Group.asset.meta`
- `Assets/AddressableAssetsData/AssetGroups/ORBIS M4 Regions.asset`
- `Assets/AddressableAssetsData/AssetGroups/ORBIS M4 Regions.asset.meta`
- `Assets/AddressableAssetsData/AssetGroups/Schemas.meta`
- `Assets/AddressableAssetsData/AssetGroups/Schemas/Default Local Group_BundledAssetGroupSchema.asset`
- `Assets/AddressableAssetsData/AssetGroups/Schemas/Default Local Group_BundledAssetGroupSchema.asset.meta`
- `Assets/AddressableAssetsData/AssetGroups/Schemas/Default Local Group_ContentUpdateGroupSchema.asset`
- `Assets/AddressableAssetsData/AssetGroups/Schemas/Default Local Group_ContentUpdateGroupSchema.asset.meta`
- `Assets/AddressableAssetsData/AssetGroups/Schemas/ORBIS M4 Regions_BundledAssetGroupSchema.asset`
- `Assets/AddressableAssetsData/AssetGroups/Schemas/ORBIS M4 Regions_BundledAssetGroupSchema.asset.meta`
- `Assets/AddressableAssetsData/AssetGroups/Schemas/ORBIS M4 Regions_ContentUpdateGroupSchema.asset`
- `Assets/AddressableAssetsData/AssetGroups/Schemas/ORBIS M4 Regions_ContentUpdateGroupSchema.asset.meta`
- `Assets/AddressableAssetsData/AssetGroupTemplates.meta`
- `Assets/AddressableAssetsData/AssetGroupTemplates/Packed Assets.asset`
- `Assets/AddressableAssetsData/AssetGroupTemplates/Packed Assets.asset.meta`
- `Assets/AddressableAssetsData/DataBuilders.meta`
- `Assets/AddressableAssetsData/DataBuilders/BuildScriptFastMode.asset`
- `Assets/AddressableAssetsData/DataBuilders/BuildScriptFastMode.asset.meta`
- `Assets/AddressableAssetsData/DataBuilders/BuildScriptPackedMode.asset`
- `Assets/AddressableAssetsData/DataBuilders/BuildScriptPackedMode.asset.meta`
- `Assets/AddressableAssetsData/DataBuilders/BuildScriptPackedPlayMode.asset`
- `Assets/AddressableAssetsData/DataBuilders/BuildScriptPackedPlayMode.asset.meta`
- `Assets/AddressableAssetsData/DefaultObject.asset`
- `Assets/AddressableAssetsData/DefaultObject.asset.meta`
- `Assets/AddressableAssetsData/ProfileDataSourceSettings.asset`
- `Assets/AddressableAssetsData/ProfileDataSourceSettings.asset.meta`
- `Assets/Orbis/M4.meta`
- `Assets/Orbis/M4/Editor.meta`
- `Assets/Orbis/M4/Editor/M4ProjectSetup.cs`
- `Assets/Orbis/M4/Editor/M4ProjectSetup.cs.meta`
- `Assets/Orbis/M4/Editor/Orbis.M4.Editor.asmdef`
- `Assets/Orbis/M4/Editor/Orbis.M4.Editor.asmdef.meta`
- `Assets/Orbis/M4/Runtime.meta`
- `Assets/Orbis/M4/Runtime/Boss.meta`
- `Assets/Orbis/M4/Runtime/Boss/M4BossModel.cs`
- `Assets/Orbis/M4/Runtime/Boss/M4BossModel.cs.meta`
- `Assets/Orbis/M4/Runtime/Boss/M4FieldBoss.cs`
- `Assets/Orbis/M4/Runtime/Boss/M4FieldBoss.cs.meta`
- `Assets/Orbis/M4/Runtime/Boss/M4PlayerVitals.cs`
- `Assets/Orbis/M4/Runtime/Boss/M4PlayerVitals.cs.meta`
- `Assets/Orbis/M4/Runtime/Orbis.M4.Runtime.asmdef`
- `Assets/Orbis/M4/Runtime/Orbis.M4.Runtime.asmdef.meta`
- `Assets/Orbis/M4/Runtime/Presentation.meta`
- `Assets/Orbis/M4/Runtime/Presentation/M4Launcher.cs`
- `Assets/Orbis/M4/Runtime/Presentation/M4Launcher.cs.meta`
- `Assets/Orbis/M4/Runtime/Presentation/M4RegionContent.cs`
- `Assets/Orbis/M4/Runtime/Presentation/M4RegionContent.cs.meta`
- `Assets/Orbis/M4/Runtime/Presentation/M4RegionRouter.cs`
- `Assets/Orbis/M4/Runtime/Presentation/M4RegionRouter.cs.meta`
- `Assets/Orbis/M4/Runtime/Presentation/M4SceneBootstrap.cs`
- `Assets/Orbis/M4/Runtime/Presentation/M4SceneBootstrap.cs.meta`
- `Assets/Orbis/M4/Runtime/Presentation/M4Session.cs`
- `Assets/Orbis/M4/Runtime/Presentation/M4Session.cs.meta`
- `Assets/Orbis/M4/Runtime/Progress.meta`
- `Assets/Orbis/M4/Runtime/Progress/M4ProgressService.cs`
- `Assets/Orbis/M4/Runtime/Progress/M4ProgressService.cs.meta`
- `Assets/Orbis/M4/Runtime/World.meta`
- `Assets/Orbis/M4/Runtime/World/M4RegionCatalog.cs`
- `Assets/Orbis/M4/Runtime/World/M4RegionCatalog.cs.meta`
- `Assets/Orbis/M4/Runtime/World/M4RegionGeometry.cs`
- `Assets/Orbis/M4/Runtime/World/M4RegionGeometry.cs.meta`
- `Assets/Orbis/M4/Runtime/World/M4RegionId.cs`
- `Assets/Orbis/M4/Runtime/World/M4RegionId.cs.meta`
- `Assets/Orbis/M4/Runtime/World/M4RegionLayout.cs`
- `Assets/Orbis/M4/Runtime/World/M4RegionLayout.cs.meta`
- `Assets/Orbis/M4/Scenes.meta`
- `Assets/Orbis/M4/Scenes/M4_Agnia.unity`
- `Assets/Orbis/M4/Scenes/M4_Agnia.unity.meta`
- `Assets/Orbis/M4/Scenes/M4_Granite.unity`
- `Assets/Orbis/M4/Scenes/M4_Granite.unity.meta`
- `Assets/Orbis/M4/Scenes/M4_Launcher.unity`
- `Assets/Orbis/M4/Scenes/M4_Launcher.unity.meta`
- `Assets/Orbis/M4/Scenes/M4_Teluna.unity`
- `Assets/Orbis/M4/Scenes/M4_Teluna.unity.meta`
- `Assets/Orbis/M4/Scenes/M4_Voltheim.unity`
- `Assets/Orbis/M4/Scenes/M4_Voltheim.unity.meta`
- `Assets/Orbis/M4/Scenes/M4_Zephyr.unity`
- `Assets/Orbis/M4/Scenes/M4_Zephyr.unity.meta`
- `Assets/Orbis/M4/Tests.meta`
- `Assets/Orbis/M4/Tests/EditMode.meta`
- `Assets/Orbis/M4/Tests/EditMode/M4BossTests.cs`
- `Assets/Orbis/M4/Tests/EditMode/M4BossTests.cs.meta`
- `Assets/Orbis/M4/Tests/EditMode/M4ContentTests.cs`
- `Assets/Orbis/M4/Tests/EditMode/M4ContentTests.cs.meta`
- `Assets/Orbis/M4/Tests/EditMode/M4ProgressTests.cs`
- `Assets/Orbis/M4/Tests/EditMode/M4ProgressTests.cs.meta`
- `Assets/Orbis/M4/Tests/EditMode/Orbis.M4.EditModeTests.asmdef`
- `Assets/Orbis/M4/Tests/EditMode/Orbis.M4.EditModeTests.asmdef.meta`
- `Assets/Orbis/M4/Tests/PlayMode.meta`
- `Assets/Orbis/M4/Tests/PlayMode/M4IntegrationTests.cs`
- `Assets/Orbis/M4/Tests/PlayMode/M4IntegrationTests.cs.meta`
- `Assets/Orbis/M4/Tests/PlayMode/Orbis.M4.PlayModeTests.asmdef`
- `Assets/Orbis/M4/Tests/PlayMode/Orbis.M4.PlayModeTests.asmdef.meta`
- `Docs/M4_구현_테스트.md`
- `Docs/M4_FILES.md`
- `Tools/Validate-M4.ps1`

## 검증 산출물

TestResults는 Git에서 제외되지만 검토를 위해 프로젝트 폴더에 함께 제공한다. Library·Temp·로컬 빌드 캐시와 생성된 Addressables 상태 파일은 전달 소스에 포함하지 않는다. 로컬 번들은 Orbis > M4 > Build Local Addressable Content로 재생성한다.

- `TestResults/M4-setup.log`
- `TestResults/M4-EditMode.xml`
- `TestResults/M4-EditMode.log`
- `TestResults/M4-PlayMode.xml`
- `TestResults/M4-PlayMode.log`
- `TestResults/M4-AddressablesBuild.log`
- `TestResults/M4_Teluna.png`
- `TestResults/M4_Zephyr.png`
- `TestResults/M4_Granite.png`
- `TestResults/M4_Voltheim.png`
- `TestResults/M4_Boss_Telegraph.png`
