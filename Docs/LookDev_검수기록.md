# 주인공 룩 개발 검수 기록

## 보존 계약

이번 단계는 렌더링·표현 작업입니다. 스텔라·폴라리스의 원본 FBX, 52개 본, Humanoid Avatar, 기존 7개 애니메이션과 FSM은 보존합니다. 새 검은 기존 오른손 소켓에 연결하고 공격 판정·거리·밸런스는 수정하지 않습니다. 기존 원소 전환의 `_Threshold`, `_PrimaryColor`, `_SecondaryColor` 인터페이스를 유지합니다.

## 팔레트

[원본 픽셀 좌표와 HEX](LookDev_Palette.md), [팔레트 스와치](LookDev_Palette.svg). 이미지 편집본이나 추정한 HEX를 샘플로 쓰지 않았습니다. Unity의 선형 렌더링 입력과 sRGB 원화 값을 구분합니다.

## 단계별 기록

- STEP 1: [파일·동일 앵글 비교](LookDev_Step01_Files.md). Shader Graph + 3×1 Point/Clamp 램프, 3단 명암, 화면 픽셀 기준 역방향 헐. 기존의 소스 렌더러 + 아웃라인 복제 구조를 재사용합니다. Shader Graph 안에 별도의 임의 패스를 삽입한 것으로 표기하지 않습니다.
- STEP 2: [파일·동일 앵글 비교](LookDev_Step02_Files.md). 팔레트 기반 전방 키라이트, SH 기반 청록 림, 피부/금속 하이라이트, Bloom·Neutral 톤매핑·약한 대비/채도·Vignette. 얼굴에는 애니메이션된 Head 방향과 UV를 사용한 넓은 그림자 마스크를 적용합니다. 코 주변 노멀 얼룩을 피하면서 몸체의 3단 램프는 유지합니다.
- STEP 3: [파일·동일 앵글 비교](LookDev_Step03_Files.md). Kenney Mini Dungeon CC0 검의 날을 재사용하는 방식 (b). 골드 가드·청록 보석·칼날 인레이가 있는 1.06m 장검으로 교체했습니다. 한 개 메시/재질, 2,418삼각형이며 기존 오른손 소켓과 M3 궤적을 사용합니다. [Unity 검 근접 화면](../TestResults/LookDev/Step03_After_Stella_Weapon.png), [Blender 원본 메시 검토 렌더](../TestResults/LookDev/Step03_WayfarerBlade_Front.png).
- STEP 4: 물리 방식 사용자 선택 대기. Unity Cloth를 쓰려면 합쳐진 망토/앞머리를 분리해야 합니다. 대안은 본과 메시 구성을 유지하는 자체 가상 스프링과 변형 제어입니다. 아직 물리 구현으로 변경한 게임 애셋은 없습니다.
- STEP 5–7: 지정한 실행 순서를 유지하기 위해 아직 진행하지 않았습니다.

## 이미지·측정 해석

`TestResults/LookDev`의 STEP PNG는 실제 Unity 플레이 모드에서 렌더링한 이미지입니다. 고정된 캐릭터·Idle 타임 0·카메라 위치·FOV·해상도를 사용하며 PNG 옆 JSON에 조건을 저장합니다. 다음 단계의 Before는 직전 단계 After 원본을 복사해 사용하므로 JSON 내부 단계 이름은 원래 촬영 시점을 유지합니다. Before/After를 별도 그림으로 보정하지 않았습니다.

STEP 1 전후 전신 카메라의 렌더러·재질 슬롯·삼각형 수는 같습니다. 스텔라 28개 렌더러/30개 슬롯, 폴라리스 30개 렌더러/32개 슬롯이며 기존 아웃라인 복제를 포함합니다. 이 수치는 캐릭터 구조 점검입니다. 배치 모드의 전역 ProfilerRecorder 증분은 카메라별 드로콜 측정이 아니므로 0 드로콜이나 특정 FPS를 입증하는 값으로 사용하지 않습니다.

STEP 1·2·3은 각 단계에서 남녀 주인공을 대상으로 실제 씬 로드, Humanoid 이동·공격, 파티 복귀와 원소 전환을 확인하는 PlayMode 검사 2건을 통과했습니다. STEP 3 이후 전체 회귀 검사 **EditMode 288건 + PlayMode 62건 = 350건 통과**, 실패·누락 0건입니다. XML/로그는 `TestResults/LookDev/Step03_EditMode.*`, `Step03_PlayMode.*`입니다.

## 현재 결과 확인

1. Unity 6에서 `Assets/Orbis/Game/Scenes/Orbis_Island.unity`를 열고 Play합니다. 기존 선택된 주인공이 새 머티리얼과 공용 여정의 검을 사용합니다. 새 세이브에서는 기존 스텔라/폴라리스 선택 화면이 나타납니다. 이 작업으로 사용자의 세이브 선택을 초기화하지 않습니다.
2. 이동·달리기·기본공격과 파티 전환을 실행해 검 소켓을 확인합니다. 기존 Q 궁극기 연출, G 원소 스킬 시험 입력도 유지됩니다.
3. 모델·프리팹·재질을 다시 만들 때는 기존 `Orbis > Game > Import Explorer Characters`가 새 룩과 검 연결을 재적용합니다. 단계별 메뉴는 `Orbis > Look Development > 01/02/03`입니다. 02 메뉴는 저장된 섬 씬의 조명과 Volume을 갱신합니다.
4. 아직 STEP 6·7 쇼케이스 포즈/전용 환경은 없습니다. 현재 비교 이미지의 섬 배경은 기존 상태이며 최종 쇼케이스 완성본으로 표기하지 않습니다.
