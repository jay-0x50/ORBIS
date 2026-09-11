# 오픈월드 게임 씬 변경/생성 파일 목록

이번 작업 시작 시점 대비 수정 11개, 신규 51개, 합계 62개. Unity 메타 파일을 포함한다. 기획서 원본은 변경하지 않았다.

편집 가능한 아그니아 게임 씬, 실제 지형·프롭·콘텐츠 배치, 기존 이동·전투·주인공의 씬 참조 연결과 테스트·문서로 구성한다. 기존 파일의 작업 전 원본은 TestResults/Game-before-authored-world 아래에 보관한다.

## 기존 파일 수정

- Assets/Orbis/Art/Runtime/ArtOutlineSync.cs
- Assets/Orbis/Art/Runtime/ArtScenePresentation.cs
- Assets/Orbis/Art/Runtime/World/ArtWorldPresentation.cs
- Assets/Orbis/M16/Runtime/Presentation/ExplorerSelectionScreen.cs
- Assets/Orbis/M16/Runtime/World/ExplorerJourney.cs
- Assets/Orbis/M4/Runtime/Presentation/M4RegionContent.cs
- Assets/Orbis/M4/Runtime/Presentation/M4RegionRouter.cs
- Assets/Orbis/M4/Runtime/Presentation/M4SceneBootstrap.cs
- Docs/M16_구현_테스트.md
- ProjectSettings/EditorBuildSettings.asset
- README.md

## 신규 파일

- Assets/Orbis/Game.meta
- Assets/Orbis/Game/Editor.meta
- Assets/Orbis/Game/Editor/OpenWorldPresentationFixes.cs
- Assets/Orbis/Game/Editor/OpenWorldPresentationFixes.cs.meta
- Assets/Orbis/Game/Editor/OpenWorldSceneBuilder.cs
- Assets/Orbis/Game/Editor/OpenWorldSceneBuilder.cs.meta
- Assets/Orbis/Game/Editor/Orbis.Game.Editor.asmdef
- Assets/Orbis/Game/Editor/Orbis.Game.Editor.asmdef.meta
- Assets/Orbis/Game/Generated.meta
- Assets/Orbis/Game/Generated/Materials.meta
- Assets/Orbis/Game/Generated/Materials/AgniaStone.mat
- Assets/Orbis/Game/Generated/Materials/AgniaStone.mat.meta
- Assets/Orbis/Game/Generated/Materials/FireGold.mat
- Assets/Orbis/Game/Generated/Materials/FireGold.mat.meta
- Assets/Orbis/Game/Generated/Materials/GuardianWaterWeakness.mat
- Assets/Orbis/Game/Generated/Materials/GuardianWaterWeakness.mat.meta
- Assets/Orbis/Game/Generated/Materials/LakeSand.mat
- Assets/Orbis/Game/Generated/Materials/LakeSand.mat.meta
- Assets/Orbis/Game/Generated/Materials/MeadowCliffStone.mat
- Assets/Orbis/Game/Generated/Materials/MeadowCliffStone.mat.meta
- Assets/Orbis/Game/Generated/Materials/MeadowGrass.mat
- Assets/Orbis/Game/Generated/Materials/MeadowGrass.mat.meta
- Assets/Orbis/Game/Generated/Materials/MeadowGrassSoftVariation.mat
- Assets/Orbis/Game/Generated/Materials/MeadowGrassSoftVariation.mat.meta
- Assets/Orbis/Game/Generated/Materials/MeadowLakeSand.mat
- Assets/Orbis/Game/Generated/Materials/MeadowLakeSand.mat.meta
- Assets/Orbis/Game/Generated/Materials/MeadowRidgeStone.mat
- Assets/Orbis/Game/Generated/Materials/MeadowRidgeStone.mat.meta
- Assets/Orbis/Game/Generated/Materials/WarmTrail.mat
- Assets/Orbis/Game/Generated/Materials/WarmTrail.mat.meta
- Assets/Orbis/Game/Generated/Meshes.meta
- Assets/Orbis/Game/Generated/Meshes/AgniaShore.asset
- Assets/Orbis/Game/Generated/Meshes/AgniaShore.asset.meta
- Assets/Orbis/Game/Runtime.meta
- Assets/Orbis/Game/Runtime/GameSceneEntry.cs
- Assets/Orbis/Game/Runtime/GameSceneEntry.cs.meta
- Assets/Orbis/Game/Runtime/Orbis.Game.Runtime.asmdef
- Assets/Orbis/Game/Runtime/Orbis.Game.Runtime.asmdef.meta
- Assets/Orbis/Game/Scenes.meta
- Assets/Orbis/Game/Scenes/Orbis_OpenWorld.unity
- Assets/Orbis/Game/Scenes/Orbis_OpenWorld.unity.meta
- Assets/Orbis/Game/Tests.meta
- Assets/Orbis/Game/Tests/PlayMode.meta
- Assets/Orbis/Game/Tests/PlayMode/AuthoredGameSceneTests.cs
- Assets/Orbis/Game/Tests/PlayMode/AuthoredGameSceneTests.cs.meta
- Assets/Orbis/Game/Tests/PlayMode/Orbis.Game.PlayModeTests.asmdef
- Assets/Orbis/Game/Tests/PlayMode/Orbis.Game.PlayModeTests.asmdef.meta
- Assets/Orbis/M4/Runtime/World/M4AuthoredRegion.cs
- Assets/Orbis/M4/Runtime/World/M4AuthoredRegion.cs.meta
- Docs/Game_게임씬_테스트.md
- Docs/Game_FILES.md

## 검증 산출물

- TestResults/Game-Setup.log
- TestResults/Game-Presentation.log
- TestResults/Game-EditMode.xml
- TestResults/Game-EditMode.log
- TestResults/Game-PlayMode.xml
- TestResults/Game-PlayMode.log
- TestResults/Game_Overview.png
- TestResults/Game_Player.png

TestResults는 Git 제외 대상이지만 검토용으로 전달한다. Library·Temp·빌드 캐시는 전달하지 않는다. 테스트는 GUID 임시 저장만 사용한다.
