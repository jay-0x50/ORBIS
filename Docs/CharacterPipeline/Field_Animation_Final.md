# Field·캐릭터 애니메이션 통합 결과

2026-09-15. **staging의 주인공 2종·보스 5종 연결, 저장된 Field 검증, 실행용 씬 export와 Windows 빌드는 완료했다.** 아직 `D:/Project/ORBIS`에 전달하거나 GitHub에 업로드한 상태는 아니다. 아래 경로는 모두 staging `orbis-m0` 기준이다.

## 편집할 씬과 연결 결과

편집 원본은 `Assets/Scenes/Field.unity` 하나다. `Orbis > Field > Open Field`로 열면 Play 전에도 저장된 지형·식생·건축과 다섯 보스 root를 Hierarchy에서 편집할 수 있다. `Agnia / Teluna / Zephyr / Granite / Voltheim Field Guardian`에는 새 모델과 애니메이션 표현 자식이 저장됐으며, 기존 root 위치·Actor·Weakness Core·콜라이더를 유지한다. 선택된 주인공은 기존 게임 시작 절차가 스폰한다.

실행 때는 기존 `Orbis_Island.unity`와 지역 환경 5개 Addressables 씬을 사용한다. 이는 스트리밍용 출력 구조이며 별도의 M1/M2 편집 씬을 다시 구성할 필요는 없다. 원본 Field SHA256은 `bfc7347b2e49ebf07d13289463d413e666fde53f5e6c8f0adb2a98dc345103d5`다.

| 연결 대상 | 적용 내용 |
|---|---|
| 스텔라 | Integrated12의 원본 atlas + 다리 국소 atlas, 66본, 측정한 리그·검 그립, 26개 모션. `Stella12GripRuntime01/Stella/Stella.prefab` |
| 폴라리스 | 원본 atlas, 64본, 바닥 기준 Humanoid 원점과 코트 가중치, 측정한 검 그립, 26개 모션. `Motion06GripRuntime01/Polaris/Polaris.prefab` |
| 보스 5종 | 형상별 Generic 리그와 직접 저작한 Idle/Move/Attack/Hurt/Exposed/Dead. 기존 M4 피해 시계와 동기화하며 새 이동 AI는 추가하지 않음 |

주인공은 `Assets/Orbis/Art/Resources/Art/Catalog.asset`에 연결됐다. 카탈로그 SHA256은 `7a9bae62b00ccf2467b774ca358311a5e5446ee7ba5581e2987c981ba9323916`이다. 선택/저장 정체성, 공용 무기, 다른 동료 참조를 보존했고 선택 UI의 오래된 회색박스 안내도 수정했다. 전체 모델·클립·재질 경로와 원본 SHA는 `Final_Asset_Mapping_Draft.md`에 있다.

## 실제 검증

| 검사 | 확인한 결과 | 근거 (`TestResults/CharacterPipeline/` 기준) |
|---|---|---|
| 저장된 Field 직접 로드 | 1 passed / 0 failed / 0 skipped. 다섯 모델/Profile/Encounter/Core 유지, Idle 및 불 보스 공격 1회·노출 연결 | `SavedField03.xml`, `FieldFinal/SavedField03/SavedFieldBosses.json` |
| 접지/전환 | 두 주인공 After10·연속 24도 내리막 검사 통과. 상태 해제·재진입 lifecycle 2개 통과 | `Stella12TerrainAfter10.xml`, `PolarisDownhillAfter10.xml`, `StellaDownhillAfter10.xml`, 두 `TerrainLifecycle01.xml` |
| 관련 회귀 | Field 로직 29 + Island 4 + Streaming 2 + BuildPolicy 1 = **36 passed**, 실패/제외 0 | 네 `Character*01.xml` |
| 실행 씬 export | 성공. Collider 4,952개, 등반 대상 99개 추출, unmatched 0. 원래 분할 로직 유지 | `CharacterFieldExport02.log` |
| 오클루전 | 6씬, occluder 648개/occludee 1,554개, PVS 10,583,248bytes 생성·연결 | `CharacterFieldOcclusion01.json` |
| Windows Release | 성공, 553,529,152bytes, 약 100초, 오류 0. 임시 Build Settings 복원 | `CharacterReleaseBuild01.json` / `.log` |
| Packed Player 렌더 | 1920×1080, 5개 지점 각 15초 실제 URP 렌더 확인. 지역 로드→해제→재로드 `[5,0,5]`, complete=true/error 없음 | `TestResults/WorldDev/CharacterPackedSmoke01/WorldPerformance.json` |

최신 `HeroFinalBinding/FinalHeroes01/FinalAll01.json`은 Catalog commit, Field export의 source dependency hash와 7개 출력 SHA를 검증했다. 17개 시작 에셋의 재귀 의존성에 최신 runtime/UI·meta·package 정보를 포함했고 수집 중 원본 불변도 확인했다. 이 결과는 **정확한 전달 목록이며 실제 복사 결과가 아니다**.

## 실행해서 확인하는 방법

1. Field를 열고 Play 전에 다섯 `Field Guardian`을 선택해 F로 프레이밍한다. 지형과 모델이 편집 상태에서 보이는지 확인한다.
2. Play한다. 새 테스트 세이브에서는 스텔라/폴라리스 선택 화면, 기존 세이브에서는 저장된 주인공으로 시작한다. 두 선택을 시험하려고 사용자 세이브를 삭제하지 않는다.
3. WASD/Shift/Space로 걷기·달리기·점프, 마우스로 카메라를 조작한다. 정지·재출발·회전·경사 경계에서 발과 코트를 확인한다.
4. 왼쪽 클릭 3단 콤보, G 원소 스킬, Tab 원소 전환, Q 궁극기 연출 시험, 1–4 파티 전환을 확인한다. 주인공의 일반공격은 기존 물리 공격이다.
5. 다섯 보스에서 예고→공격→회복과 약점 노출·피격·사망 표현을 확인한다. 불은 여섯 다리, 물은 유영, 풍/뢰는 네 다리와 두 날개를 유지한다. 보스 `Move` 클립은 새 추적 AI 구현을 뜻하지 않는다.

## 남은 한계와 전달 상태

- Packed Player 검사는 창에 화면을 표시하지 않는 `SingleCameraRequest` 오프스크린 렌더다. 캡처 프레임 수로 게임 FPS를 산출하지 않으며, 실제 표시 환경의 목표 FPS와 몬드 수준의 완성 아트 품질을 달성했다고 주장하지 않는다.
- After10의 접지 개선은 실제 좌표 검사 근거다. `PolarisGripTerrain02/03`의 Slope24 frame0157 JPEG가 동일해 그 쌍으로 시각 개선을 입증할 수 없다. 해당 캡처는 증거 범위의 한계로 기록하며 추가 엔진 결함이나 재시험 필요로 판정한 것은 아니다. 약한 접촉 프레임의 잔차, 스텔라 부츠의 작은 띠·각진 장갑·얼굴 명암 경계도 남는다.
- SavedField의 CPU 스키닝 교차 검증 오차는 최대 0.1931mm다. 이는 측정 계약 검증이며 발이 항상 완벽히 접촉한다는 뜻은 아니다. 개별 발 분기 검사는 풍/뢰를 포함했으나 불/암은 몸체 최저점 위주로 측정했고, 저장 Field에서 공격·노출 검증은 불 1종이다. 종별 별도 전투 검수와 구분한다.
- 빌드 보고서 경고는 2개다. 로그의 기존 obsolete API·CS0252 경고와 Addressables BuildLayout 경고, 성공 후 Editor 종료 시 JobTempAlloc 경고도 남아 있다. 이전 로그에서도 확인됐지만 원인 해결이나 Player 메모리 누수 부재를 입증한 것은 아니다.
- 기존 약점 Core 표지가 몸통에 가려지는 위치 문제는 사용자 배치 선택 대기다. 현재 표지 위치와 기존 root 판정은 유지한다. Cosmo는 수정 원본 확인 전 보류, 일반 몬스터 15종은 사용자 원본 대기이며 임의 도형으로 대체하지 않았다.
- 활성 7종 사용자 원본 SHA는 최초 감사와 같다. 보류 Cosmo의 별도 해시 변화는 매핑 표에 구분했다. KayKit CC0 모션과 프로젝트 저작 동작은 출처를 기록했으며 실제 Mixamo 파일 검증은 하지 않았다. 사용자 제공 이미지·모델·파생물의 공개 배포 권리는 별도 확인이 필요하다.
- **타깃 전달·GitHub 업로드·컴퓨터 종료는 아직 실행하지 않았다.** 원본 및 중간 후보 전체를 복사하지 않고 검증된 의존성 목록으로 전달해야 한다.
