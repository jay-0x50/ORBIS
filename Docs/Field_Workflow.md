# 필드 편집 및 실행

일반 작업용 씬은 **`Assets/Scenes/Field.unity` 하나**입니다. Unity 메뉴 `Orbis > Field > Open Field`로 열거나 Project 창에서 해당 파일을 더블클릭합니다. **Play를 누르지 않아도** Terrain, 나무, 건물, 바위, 물과 필드 콘텐츠가 Scene 뷰와 Hierarchy에 있습니다.

- Scene 뷰에서 Hierarchy의 지형 또는 건물을 선택하고 `F`를 누르면 해당 오브젝트로 이동합니다. 실제 저장된 오브젝트이므로 이동·복제·삭제 후 `Ctrl+S`로 씬을 저장할 수 있습니다.
- 다섯 지역의 환경 루트는 같은 씬에 있으며, 초목·건물·랜드마크를 펼쳐 개별 배치를 편집합니다. 잔디는 수십만 개의 GameObject 대신 `WorldGrassField`와 배치 데이터로 GPU 렌더링합니다. 잔디의 부모 오브젝트를 옮기면 잔디 배치도 함께 이동합니다.
- Play 직전에 현재 편집 내용을 실행용 씬으로 자동 내보내고 게임을 시작합니다. 주인공을 아직 고르지 않은 세이브에서는 필드 위에 캐릭터 선택 화면이 나옵니다. Play 중 변경은 일반 Unity 씬처럼 종료 시 되돌아가므로 배치 수정은 Edit Mode에서 저장합니다.

## 편집용 씬과 실행용 산출물

`Field.unity`가 편집 원본입니다. 실행 시에는 기존 스트리밍 구조를 유지하기 위해 원본에서 코어 씬과 다섯 지역 환경 씬을 내보냅니다.

| 경로 | 역할 |
| --- | --- |
| `Assets/Scenes/Field.unity` | 사용자가 열고 편집하는 단일 필드 원본 |
| `Assets/Orbis/Game/Scenes/Orbis_Island.unity` | 게임 실행용 코어 씬. 원본에서 생성되는 산출물 |
| `Assets/Orbis/Game/World/Scenes/Environment_*.unity` | Addressables 지역 스트리밍용 환경 산출물 |
| `Assets/Orbis/M*/Scenes/` | 기존 기능의 회귀 테스트 씬. 일반 필드 작업에는 열 필요 없음 |

이 구조는 한 대륙을 지역별로 끊어 보이게 만들지 않습니다. 편집은 하나의 필드에서 하고, 실행 중 메모리 관리를 위한 내부 로딩 경계만 별도로 유지합니다. 생성된 코어·환경 씬을 직접 수정하면 다음 내보내기와 충돌하므로 배치는 `Field.unity`에서 수정합니다. 기존 M0~M16 코드·데이터 폴더는 기능 참조와 테스트를 보존하기 위해 유지하며, 예전 제작 메뉴는 `Orbis > Development > Legacy`에서 찾을 수 있습니다.

새 NPC·퍼즐·적·트리거 등 게임플레이 오브젝트는 항상 유지되는 **World의 콘텐츠 계층**에 배치합니다. `04 Scenery - editable regions` 아래의 다섯 지역 루트에는 Renderer·LOD·잔디와 정적 충돌체 등 환경 요소를 배치합니다. 이 지역 루트는 스트리밍 대상이므로 게임플레이 컴포넌트를 넣으면 내보내기 검사가 중단됩니다. 환경 오브젝트에 붙은 충돌체는 내보내기 시 같은 월드 위치의 상주 충돌체로 분리되어, 환경 로딩 여부와 관계없이 기존 이동·등반을 유지합니다.

수동 내보내기는 `Orbis > Field > Export Runtime Scenes`를 사용합니다. 검사에 실패하면 Play를 취소하고 원본과 미저장 편집 내용을 보존하므로 Console에서 문제 오브젝트를 확인합니다.

## 확인 방법

1. Play를 끈 상태에서 `Orbis > Field > Open Field`를 실행합니다. Hierarchy에는 `Field` 씬 하나와 지형·환경·콘텐츠 오브젝트가 보여야 합니다.
2. 건물이나 나무를 선택하고 Scene 뷰에서 확인합니다. 위치를 수정했다면 `Ctrl+S` 후 씬을 다시 열어 저장을 확인합니다.
3. Play를 눌러 기존 이동·카메라·파티·필드 콘텐츠가 동작하는지 확인합니다. 원본에서 내보낸 환경은 기존 Addressables 방식으로 로딩됩니다.
4. `Orbis > Field > Capture Edit Mode Evidence`는 **게임을 시작하지 않고** URP로 마을·대륙·흑백 지형 스크린샷과 Hierarchy 검사 JSON을 `TestResults/WorldDev/Field_EditMode_*`에 기록합니다. 배치 실행 시 `-worldStep`으로 파일 접두사를 지정할 수 있습니다.

스크린샷은 저장된 필드를 실제 렌더링한 결과이며, 프레임 성능 측정값은 아닙니다. 지형·건물 배치를 크게 바꾼 뒤에는 실행용 씬을 내보내고 오클루전 데이터를 다시 베이크해야 합니다.

## 이번 정리의 검증 결과

- 편집용 Field: 씬 1개, 실제 GameObject 33,602개, Terrain 4개, 지역 환경 루트 5개. 누락 스크립트·참조·메시·머티리얼 0개.
- 나무 이동·복제·삭제 내보내기, 시각 오브젝트와 충돌체 Transform 일치, 미저장 원본 편집 보존 및 원본 파일 복원 확인.
- EditMode 299개 통과. PlayMode 71개 고유 항목 확인: 최초 70개 통과 후, 바뀐 Core 계층을 반영한 가시성 검사 1개 재실행 통과.
- 6개 실행 씬의 오클루전 재베이크 완료. Addressables를 포함한 Windows Release 빌드 성공(오류 0, 경고 2). FPS 측정은 이번 검증에 포함하지 않음.
- 최종 근거: `TestResults/WorldDev/Field_Validation.json`, `Field_EditMode_Hierarchy.json`, `Field_ExportRoundtrip.json`, `Field_Occlusion.json`, `Field_Build.json`.
