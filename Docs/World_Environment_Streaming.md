# 공통 지형과 지역 환경 스트리밍

STEP 1은 섬의 지형과 이동 충돌, 플레이어, 파티, 39개 M4 액터와 진행 상태를 `Orbis_Island.unity`에 유지하면서 지역별 시각 환경을 별도 Addressables 씬으로 분리한다. 바이옴 색상 혼합은 연속 지형에서 처리하며 아래 로딩 경계와 독립적이다. 기존 `orbis.region.*` 시연용 씬과 새 환경 주소는 구분된다.

| 지역 ID | 환경 씬 | Addressables 주소 |
|---|---|---|
| Agnia | `Assets/Orbis/Game/World/Scenes/Environment_Agnia.unity` | `orbis.world.environment.agnia` |
| Teluna | `Assets/Orbis/Game/World/Scenes/Environment_Teluna.unity` | `orbis.world.environment.teluna` |
| Zephyr | `Assets/Orbis/Game/World/Scenes/Environment_Zephyr.unity` | `orbis.world.environment.zephyr` |
| Granite | `Assets/Orbis/Game/World/Scenes/Environment_Granite.unity` | `orbis.world.environment.granite` |
| Voltheim | `Assets/Orbis/Game/World/Scenes/Environment_Voltheim.unity` | `orbis.world.environment.voltheim` |

실제 ID 철자는 `M4RegionId`와 빌더가 기준이다. 각 씬에는 해당 지역의 환경 루트만 둔다. 랜드마크에서 분리한 등반 표식과 충돌은 공통 씬에 두므로 시각 환경의 로딩 중에도 이동 지면이 사라지지 않는다. 기존 M4 퍼즐, 보스, 퀘스트 액터는 이 단계에서 스트리밍하지 않는다.

## 데이터와 실행

`WorldStreamCatalog`는 `Assets/Orbis/Game/World/Resources/World/StreamCatalog.asset`에 저장된다. 5개 지역의 ID, 주소, 월드 경계와 로딩 설정을 담는다. 문서에 없는 임시 기본값은 로드 거리 700m, 유지 거리 850m, 위치 갱신 0.25초, 중앙 전망대 반경 140m이다. 경계까지의 수평 거리로 판단하고 고도는 사용하지 않는다. 중앙 전망대에서는 인접 지역 풍경을 위해 5개를 미리 불러온다. 가시거리와 메모리 수치는 최종 성능 검토에서 조정할 대상이다.

`WorldRegionStreamer`는 한 번에 하나의 Additive 씬 작업을 수행하고 이동 중 요청을 최신 위치로 합친다. 새 지역을 먼저 로드하고 유지 범위 밖 지역을 해제한다. 객체 파괴 중 완료되는 비동기 작업도 남은 씬 핸들을 해제한다. Addressables 완료 콜백 내부에서 같은 씬을 즉시 재요청하지 않고 다음 ResourceManager 갱신에서 처리한다.

플레이 시 기존 F10 또는 메뉴의 지역 이동은 공통 지형 위로 즉시 이동한다. 근처 환경이 아직 준비되지 않았다면 화면 오른쪽 아래에 로딩 상태가 표시된다. 사진 촬영 또는 전체 검수에서는 `HoldAllRegions = true`를 설정하고 `WaitReady()` 또는 `LoadAll()`을 기다린다. 이 옵션은 일반 플레이의 기본 설정이 아니다.

## 빌드와 테스트 절차

1. 정상 테스트는 `Orbis/World/Open Full Continent for Editing`으로 시작한다. 저장된 `Orbis_Island.unity`와 환경 5개를 편집용으로 함께 열어 Hierarchy에서 확인할 수 있다. 이 메뉴는 Play 시작 씬을 본 씬으로 지정하므로, Play에서는 편집용 환경을 중복 사용하지 않고 스트리머가 Addressables 씬 핸들을 소유한다.
2. Play를 누르고, 아직 선택 기록이 없으면 주인공을 선택한다. Inspector에서 스트리머의 로드 수를 확인하면서 지역을 이동한다. 현재 지역 AABB와 700/850m 설정에서는 일반 거점 간 이동 중 5개가 계속 로드될 수 있다. 충분히 먼 유지 범위 밖으로 나가야 해제가 일어나며 Terrain과 등반 충돌, M4 진행 상태는 남는다. 테스트를 위해 예전 `01 Continuous Biomes and Streaming` 생성 단계를 다시 실행하지 않는다. 현재 지형은 후속 작업으로 7개 TerrainLayer를 사용하므로 STEP 1 재생성을 정상 진입 절차로 취급하면 안 된다.
3. Test Runner의 PlayMode에서 `Orbis.Game.Tests.WorldRegionStreamingTests`를 실행한다. 실제 등록된 환경 씬으로 가산 로딩, 원래 활성 씬과 충돌 유지, 경계 왕복, 먼 위치 이동, 언로드 중 재진입, 로드 도중 호스트 파괴 및 핸들 정리를 검사한다. 기존 테스트는 삭제하거나 대체하지 않는다.
4. 에디터 PlayMode는 기존 M4 설정과 동일하게 Fast Mode의 AssetDatabase 공급자를 사용한다. 실제 Packed 검증용 Windows 실행 파일은 `Orbis/World Performance/Build Windows Player`로 만든다. 이 경로는 기존 M4 그룹까지 포함한 Packed Addressables를 먼저 빌드한 뒤, 활성화된 기본 씬 목록으로 플레이어를 빌드한다. 번들만 갱신하려면 `Orbis/World Performance/Build Packed Addressables`를 사용한다. 환경 씬은 기본 Build Settings 씬 목록에 중복 등록하지 않는다.

## 최종 실행 결과 — 2026-09-11

`TestResults/WorldDev/PerformanceOffscreen/WorldPerformance.json`의 Windows Development 플레이어 실행은 `complete=true`, 오류 없음으로 완료됐다. 실제 Packed Addressables 환경 씬 수가 **5 → 0 → 5**로 변해 전체 로드, 먼 위치에서 해제, 재진입 로드가 통과했다. Village, Forest, Lakeside, HighlandStorm, IslandTravel의 별도 렌더 구간에서는 모두 5개가 유지됐다. 따라서 이 결과는 일반 거점 이동마다 씬이 해제된다는 뜻은 아니다.

GTX 1650 SUPER에서 1920×1080 URP `SingleCameraRequest`로 화면 밖 렌더와 5개 지점 PNG 생성을 실행했다. 창에 프레임을 표시하는 일반 플레이 방식이 아니므로, 기록된 반복 횟수를 게임 FPS로 환산하지 않는다. **1080p 60FPS 달성 여부와 실제 플레이 FPS는 아직 검증하지 않았다.** 실행 옵션과 파일은 같은 폴더의 `Launch.json`에 기록돼 있다.

공통 의존성을 별도 공유 그룹으로 묶은 뒤, 환경 5개와 공유 번들의 압축 파일 합계가 **78.61 → 36.21MiB**로 줄었다. 이 합계는 새 공동 PVS의 비용을 포함한다. 중복 객체의 추가 직렬화 데이터는 별도 지표로 116.193 → 0.091MiB가 됐다. 비교 대상 빌드, 지역별 파일 크기, 남은 중복은 [번들 감사](WorldArt/World06_BundleAudit.md)에 기록했다. 압축 용량 및 직렬화 크기 변화는 RAM·VRAM 감소량이나 FPS 개선량이 아니다.

지역 씬 해제는 인스턴스와 해당 씬만 참조하는 의존성을 해제한다. 공통 `ArtAssetCatalog`, Resources, 본 씬이 참조하는 모델·머티리얼·텍스처는 남을 수 있으므로 모든 지역 텍스처가 메모리에서 제거된다는 의미는 아니다. 공동 PVS가 번들에 존재하는 사실만으로 나무·잔디까지 GPU에서 컬링됐다고 해석하지 않는다.
