# 주인공 얼굴·머리카락 개선 파일 목록

작업 시작 시점 대비 수정 31개, 신규 19개, 합계 50개. Unity .meta를 포함한다.

주요 변경: 스텔라·폴라리스 얼굴 연속 곡면과 UV, 금발 헤어, 실제 얼굴·헤어 PNG 세 장, 주인공 피부·명암·윤곽선, Blender 원본/FBX/프리팹 및 검증·출처 문서.

## 수정

- Assets/Orbis/Art/Resources/Art/Shaders/UnifiedToon.shader
- Assets/Orbis/Art/Runtime/ArtOutlineSync.cs
- Assets/Orbis/Game/Editor/ExplorerArtImport.cs
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_CapeLining.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Gem.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Gold.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Hair.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Ivory.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Leather.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Navy.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Skin.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_CapeLining.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_Gem.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_Gold.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_Hair.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_Ivory.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_Leather.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_Navy.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_Skin.mat
- Assets/Orbis/Game/Island/Models/Polaris.fbx
- Assets/Orbis/Game/Island/Models/Polaris.fbx.meta
- Assets/Orbis/Game/Island/Models/Stella.fbx
- Assets/Orbis/Game/Island/Models/Stella.fbx.meta
- Assets/Orbis/Game/Island/Prefabs/Polaris.prefab
- Assets/Orbis/Game/Island/Prefabs/Stella.prefab
- Assets/Orbis/Game/Tests/ExplorerArtPlayMode/ExplorerArtTests.cs
- Credits.md
- Docs/Island_통합섬_주인공_테스트.md
- README.md
- Tools/Blender/Sources/Polaris.blend
- Tools/Blender/Sources/Stella.blend

## 신규

- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Face.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Polaris_EX_Face.mat.meta
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_Face.mat
- Assets/Orbis/Game/Island/Materials/Explorers/Stella_EX_Face.mat.meta
- Assets/Orbis/Game/Island/Textures/Explorers.meta
- Assets/Orbis/Game/Island/Textures/Explorers/AshBlond_Hair_BaseColor.png
- Assets/Orbis/Game/Island/Textures/Explorers/AshBlond_Hair_BaseColor.png.meta
- Assets/Orbis/Game/Island/Textures/Explorers/Polaris_Face_BaseColor.png
- Assets/Orbis/Game/Island/Textures/Explorers/Polaris_Face_BaseColor.png.meta
- Assets/Orbis/Game/Island/Textures/Explorers/Stella_Face_BaseColor.png
- Assets/Orbis/Game/Island/Textures/Explorers/Stella_Face_BaseColor.png.meta
- Docs/Face_얼굴개선_테스트.md
- Docs/Face_FILES.md
- Docs/Face_Texture_Manifest.json
- Docs/Face_Texture_Prompts.md
- Tools/Blender/audit_face_polish.py
- Tools/Blender/polish_explorers.py
- Tools/Blender/Sources/FaceV1Sources/Polaris.blend
- Tools/Blender/Sources/FaceV1Sources/Stella.blend

## 검증 파일

- TestResults/Face-Art.log
- TestResults/Face-EditMode.log
- TestResults/Face-EditMode.xml
- TestResults/Face-PlayMode.log
- TestResults/Face-PlayMode.xml
- TestResults/Face-Visual-PlayMode.log
- TestResults/Face-Visual-PlayMode.xml
- TestResults/FacePolish-Blender.log
- TestResults/FacePolish-Blender.err
- TestResults/FacePolish-Source-Audit.json
- TestResults/FacePolish_Stella_Model.json
- TestResults/FacePolish_Polaris_Model.json
- TestResults/FacePolish_Stella_Face.png
- TestResults/FacePolish_Stella_Face_ThreeQuarter.png
- TestResults/FacePolish_Stella_Full.png
- TestResults/FacePolish_Polaris_Face.png
- TestResults/FacePolish_Polaris_Face_ThreeQuarter.png
- TestResults/FacePolish_Polaris_Full.png
- TestResults/Face_Stella_Front.png
- TestResults/Face_Stella_ThreeQuarter.png
- TestResults/Face_Stella_Profile.png
- TestResults/Face_Polaris_Front.png
- TestResults/Face_Polaris_ThreeQuarter.png
- TestResults/Face_Polaris_Profile.png
- TestResults/Island_Stella_Unity.png
- TestResults/Island_Polaris_Unity.png
- TestResults/Face-before/Face_Stella_Front.png
- TestResults/Face-before/Face_Stella_ThreeQuarter.png
- TestResults/Face-before/Face_Stella_Profile.png
- TestResults/Face-before/Face_Polaris_Front.png
- TestResults/Face-before/Face_Polaris_ThreeQuarter.png
- TestResults/Face-before/Face_Polaris_Profile.png

수정 전 원본 백업: TestResults/Face-before-polish/. Library·Temp·빌드 캐시는 전달 대상에 포함하지 않는다. 테스트는 GUID 임시 프로필을 사용한다.
