# STEP 3 건축물 — 출처와 좌표 계약

`House_Cottage`, `House_Merchant`, `House_Workshop`, `Windmill`, `StormSpire`는 오르비스용으로 로컬 Blender에서 새로 작성한 건축 메시다. [재현 스크립트](../../Tools/WorldDev/build_architecture.py)는 외부 메시를 불러오지 않는다. 원환·나침반과 아이보리·남색·금색이라는 프로젝트 모티브를 사용하며, 특정 상용 게임의 건축물을 복제하거나 Kenney/Quaternius 팩의 구성품으로 표기하지 않는다. 이번 작업에서 이 프로젝트 제작물에 CC0를 부여하지 않았다.

## 원본 FBX와 최종 프리팹

| 구분 | 위쪽 | 정면 | 위치와 회전 |
|---|---|---|---|
| Blender 작성 좌표 | +Z | -Y | 미터 단위, 건물 바닥 중심 원점 |
| Unity에서 읽은 **원본 FBX** | +Y | **-Z** | 작성 좌표 `(x,y,z)`는 원본 Unity 좌표 `(x,z,y)`에 대응 |
| `WorldLandmarkBuilder`가 만든 **최종 프리팹** | +Y | **+Z** | 각 `LOD0`/`LOD1` 자식에 Y축 180° 회전을 적용 |

[Unity 프리팹 생성 코드](../../Assets/Orbis/Game/Editor/WorldLandmarkBuilder.cs)의 `PrepareArchitecture`가 이 회전을 적용한다. 외부 배치 루트의 yaw, 현관 앞 기단·계단과 표지 배치는 정규화된 프리팹의 +Z를 기준으로 삼는다. FBX를 다시 회전하여 내보내거나 배치 루트에 180°를 중복으로 더하면 앞뒤가 다시 뒤집힌다.

풍차 `Rotor`의 원본 FBX 위치는 Unity **`(0, 13.7, -4.76)`**이다. LOD 자식 정규화 후 프리팹 루트 기준 위치는 **`(0, 13.7, +4.76)`**이다. 회전은 Rotor 자신의 로컬 Z축을 사용한다. 최종 프리팹의 정면 규칙과 Rotor 메시 자체의 로컬 회전축을 혼동하지 않는다.

## 좌표 설명을 정정한 근거

초기 매니페스트(version 1)는 `(x,z,-y)`와 원본 FBX의 `+Z front`를 가정했다. 이는 잘못된 설명이었다. 최초 Unity 마을 캡처에서 Cottage의 **후면 단일 창**이 카메라를 향했고, 정면의 두 창·문·현관은 반대편에 있었다. 정규화 전 실제 Unity 프리팹의 `LODGroup.m_LocalReferencePoint.z`도 Cottage/Merchant/Workshop에서 약 `-0.935`, StormSpire에서 약 `-0.6963`이었다. 현관 때문에 생기는 이 비대칭 중심값은 초기 매니페스트의 양수 값과 반대였고 X/Y는 일치했다.

이를 근거로 원본 FBX 정면을 -Z로 정정하고, 최종 프리팹의 LOD 자식에 180° 회전을 추가했다. 최신 `World03_Detail_VillageClose.png`와 `World03_After_Village.png`는 Unity 씬 확인 자료다. 캡처는 빌드 재실행 시 갱신되므로, 위 초기 관찰을 최신 이미지에 여전히 보이는 현상으로 해석하지 않는다.

## 검증 범위

- **Blender 재임포트 검사:** 실제 FBX 10개를 다시 읽고 메시 수, 삼각형 수, UV의 유효성, 공통 재질 이름, 크기, Rotor 원점과 파일 SHA 불변을 검사한다. `--audit`는 Unity를 실행하지 않는다. 따라서 Blender 왕복검사 통과만으로 Unity의 정면 방향을 검증했다고 주장하지 않는다.
- **Unity 확인:** 실제 ModelImporter(`bakeAxisConversion=true`), 프리팹의 비대칭 bounds, 정면/후면 특징이 보이는 게임 씬 캡처를 별도로 확인한다. `WorldLandmarkBuilder`의 LOD 회전이 최종 정면을 +Z로 만든다.

현재의 좌표 정정은 설명·매니페스트 계산 기준만 바꾼다. 기존 FBX의 메시·UV·재질·삼각형 수·해시는 바뀌지 않으며 모델 재수출도 필요하지 않다. version 2의 `boundsUnityMin`/`boundsUnityMax`는 **정규화 전 원본 FBX 좌표**이고, `normalizedPrefabBoundsUnityMin`/`normalizedPrefabBoundsUnityMax`는 **LOD Y=180° 적용 후 프리팹 좌표**다.
