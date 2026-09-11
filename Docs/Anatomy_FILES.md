# 주인공 얼굴형·전신·의상 개선 파일 목록

작업 시작 시점 대비 수정 12개, 신규 24개, 합계 36개. Unity .meta를 포함한다.

주요 변경: 얼굴형과 측면 코·입·턱, 머리 크기, 목·쇄골·어깨·흉곽·허리·골반·의상 메시 재제작, 의상 PNG 두 장, 원본/FBX/프리팹 및 전신·걷기·공격 시각 검수.

## 수정

- Assets/Orbis/Game/Editor/ExplorerArtImport.cs
- Assets/Orbis/Game/Island/Models/Polaris.fbx
- Assets/Orbis/Game/Island/Models/Polaris.fbx.meta
- Assets/Orbis/Game/Island/Models/Stella.fbx
- Assets/Orbis/Game/Island/Models/Stella.fbx.meta
- Assets/Orbis/Game/Island/Prefabs/Polaris.prefab
- Assets/Orbis/Game/Island/Prefabs/Stella.prefab
- Assets/Orbis/Game/Tests/ExplorerArtPlayMode/ExplorerArtTests.cs
- Credits.md
- README.md
- Tools/Blender/Sources/Polaris.blend
- Tools/Blender/Sources/Stella.blend

## 신규

- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_TailoredIvory.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_TailoredIvory.mat.meta
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_TailoredNavy.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_TailoredNavy.mat.meta
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Trousers.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Trousers.mat.meta
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_TailoredIvory.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_TailoredIvory.mat.meta
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_TailoredNavy.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_TailoredNavy.mat.meta
- Assets/Orbis/Game/Island/Textures/Explorers/Body_Ivory_BaseColor.png
- Assets/Orbis/Game/Island/Textures/Explorers/Body_Ivory_BaseColor.png.meta
- Assets/Orbis/Game/Island/Textures/Explorers/Body_Navy_BaseColor.png
- Assets/Orbis/Game/Island/Textures/Explorers/Body_Navy_BaseColor.png.meta
- Docs/Anatomy_구현_테스트.md
- Docs/Anatomy_FILES.md
- Docs/Anatomy_Texture_Manifest.json
- Docs/Anatomy_Texture_Prompts.md
- Tools/Blender/audit_explorer_anatomy.py
- Tools/Blender/refine_explorer_anatomy.py
- Tools/Blender/refine_explorer_head.py
- Tools/Blender/Sources/AnatomyBaseSources/Polaris.blend
- Tools/Blender/Sources/AnatomyBaseSources/Stella.blend
- Tools/Blender/tailor_explorer_body.py

## 검증 파일

- TestResults/Anatomy-Art.log
- TestResults/Anatomy-EditMode.log
- TestResults/Anatomy-EditMode.xml
- TestResults/Anatomy-PlayMode.log
- TestResults/Anatomy-PlayMode.xml
- TestResults/Anatomy-Visual-PlayMode.log
- TestResults/Anatomy-Visual-PlayMode.xml
- TestResults/Anatomy-Blender.log
- TestResults/Anatomy-Blender.err
- TestResults/Anatomy-Source-Audit.json
- TestResults/Anatomy_Stella_Model.json
- TestResults/Anatomy_Polaris_Model.json
- TestResults/AnatomyStudio_Stella_Front.png
- TestResults/AnatomyStudio_Stella_ThreeQuarter.png
- TestResults/AnatomyStudio_Stella_Profile.png
- TestResults/AnatomyStudio_Stella_Back.png
- TestResults/AnatomyStudio_Stella_Face.png
- TestResults/AnatomyStudio_Stella_FaceProfile.png
- TestResults/Anatomy_Stella_Front.png
- TestResults/Anatomy_Stella_ThreeQuarter.png
- TestResults/Anatomy_Stella_Profile.png
- TestResults/Anatomy_Stella_Back.png
- TestResults/Anatomy_Stella_Walk.png
- TestResults/Anatomy_Stella_Attack.png
- TestResults/Face_Stella_Front.png
- TestResults/Face_Stella_ThreeQuarter.png
- TestResults/Face_Stella_Profile.png
- TestResults/Island_Stella_Unity.png
- TestResults/Anatomy-before/Island_Stella_Unity.png
- TestResults/Anatomy-before/Face_Stella_Profile.png
- TestResults/AnatomyStudio_Polaris_Front.png
- TestResults/AnatomyStudio_Polaris_ThreeQuarter.png
- TestResults/AnatomyStudio_Polaris_Profile.png
- TestResults/AnatomyStudio_Polaris_Back.png
- TestResults/AnatomyStudio_Polaris_Face.png
- TestResults/AnatomyStudio_Polaris_FaceProfile.png
- TestResults/Anatomy_Polaris_Front.png
- TestResults/Anatomy_Polaris_ThreeQuarter.png
- TestResults/Anatomy_Polaris_Profile.png
- TestResults/Anatomy_Polaris_Back.png
- TestResults/Anatomy_Polaris_Walk.png
- TestResults/Anatomy_Polaris_Attack.png
- TestResults/Face_Polaris_Front.png
- TestResults/Face_Polaris_ThreeQuarter.png
- TestResults/Face_Polaris_Profile.png
- TestResults/Island_Polaris_Unity.png
- TestResults/Anatomy-before/Island_Polaris_Unity.png
- TestResults/Anatomy-before/Face_Polaris_Profile.png

수정 전 원본 백업: TestResults/Anatomy-before-refine/. Library·Temp·빌드 캐시는 전달 대상에 포함하지 않는다. 테스트는 GUID 임시 프로필을 사용한다.
