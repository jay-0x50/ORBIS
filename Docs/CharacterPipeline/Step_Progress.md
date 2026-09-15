# 캐릭터 후처리 작업 기록

2026-09-14. 원본은 `D:/Project/ORBIS/Assets/blend`에서 읽고, 파생본은 별도 스테이징에서 검수 중이다. 이 문서는 최종 완료 보고가 아니다. 기존 Field 씬과 플레이 데이터는 변경하지 않았다.

## 확인된 범위

- 즉시 진행: 스텔라, 폴라리스, 보스 5종.
- 코스모: 사용자가 올바른 조력자 원본을 준비하기로 하여 보류.
- 일반 필드 몬스터 15종: 사용자 원본 대기. 보조 도감의 추가 9종은 참고 목록이다.
- 풍·뢰 보스: 실제 제공 메시의 네 다리와 두 날개를 보존하도록 사용자가 승인.
- 불 보스: 아래·낮은 측면 실제 렌더에서 여섯 다리를 확인했다. 이전 네발 분류는 잘못됐으며, 사용자는 여섯 다리와 말린 꼬리 보존을 승인했다.
- 심해성룡: 다리 없는 척추·꼬리·지느러미·목·턱 Generic 리그 승인.
- 애니메이션: 확인된 KayKit CC0 리타겟 + 자체 제작 모션 승인. Mixamo 다운로드/검증을 했다고 표시하지 않는다.

## STEP 1 — 메시

원본 8개 모두 조사했고, SHA-256 비교로 원본이 변경되지 않았음을 확인했다. 각 파일의 전신 정면·측면·3/4 렌더와 통계는 `TestResults/CharacterPipeline/SourceAudit`에 있다. 모두 본/가중치/클립 없는 단일 메시이며 2048² 텍스처 3개가 내부에 들어 있었다.

폴라리스 감축 후보는 **1,508,958 → 100,000 삼각형**, 49,459 정점이다. 원본 UV와 텍스처 바이트를 유지했다. 원본 정점 20,000개에서 후보 표면까지 측정한 단방향 거리는 p99 0.54mm, 최대 1.12mm이다. 이것은 전체 표면 오차의 수학적 보장이 아니며, 관절 변형 품질을 증명하지도 않는다.

동일 카메라·조명·색 변환으로 전신 3각도와 얼굴/몸통 확대를 렌더링했다. 원본과 후보의 카메라 위치·타깃·배율은 JSON에 저장되어 있다. 실제 화면 검수에서 전신 실루엣과 얼굴/의상 정체성이 유지됨을 확인했다. 작은 비정상 표면 연결은 추가 정리 중이며, 리깅 전 최종 정리본을 사용한다.

- 원본 비교: `TestResults/CharacterPipeline/Reduction/PolarisSourceReview/`
- 10만 후보: `TestResults/CharacterPipeline/Reduction/Polaris100k/Review/`

스텔라의 단순 감축 시도는 **불합격**이다. 10만 목표에 도달하지 못했고, 마지막 시도는 270,943 삼각형에서 표면 거리 p99 약49mm, 최대118mm로 커졌다. `Stella100k`, `Stella100kV2`, `Stella100kV3` 폴더의 후보는 게임이나 리그에 사용하지 않는다. 미세한 표면 연결을 재구성한 뒤 원본 색/노멀/마스크를 투영하는 별도 후보를 검증한다. 원본의 두 부츠가 붙은 부위도 확인했으며, 독립적인 발 스키닝 전에 분리해야 한다.

가중 감축의 0 가중치는 단순한 낮은 중요도가 아니라 감축 대상 제외가 될 수 있다. 방향과 비용식은 [Blender 공식 구현](https://github.com/blender/blender/blob/main/source/blender/bmesh/tools/bmesh_decimate_collapse.cc)을 확인했고, 기본 비교 후보는 균일 감축을 사용한다.

## STEP 2 — 리그

`rig_schema.json`과 `Rig_Plan.md`는 기존 52개 Humanoid 본 계약과 새 원본의 후보 관절 좌표를 분리한다. 후보 좌표를 검증된 리그로 오인하지 않는다. 폴라리스 리그·가중치·변형 시험을 준비 중이다.

## STEP 3·5·6 — 이동과 검증 준비

실제 Unity 플레이 모드에서 두 주인공의 교체 전 모습을 각각 30fps, 300프레임, 10초 캡처했다. `Motion_Before02.xml`의 테스트 두 개가 통과했다. 정지→걷기→달리기→정지→좌우 전환→이동 입력 중 스킬→정지를 동일한 입력 레시피로 기록했다. 캐릭터를 수동 포즈로 움직이거나 생성 이미지로 대체하지 않았다.

- 증거: `TestResults/CharacterPipeline/Motion/Before/Baseline02/{Stella,Polaris}/`
- 각 폴더: 원본 JPEG 300장, 본/루트/클립/레이어 측정 `motion.json`, 기술 통계 `metrics.json`, 영상과 인코딩 출처 파일.
- 기존 보행에는 이미 상당한 골반 상하 운동이 있다. 단순히 바운스를 추가하는 대신 보폭·발 접지·회전·리타겟 손실과 전환을 개선해야 한다.
- 발 측정은 본과 지면 간 거리이다. 신발 밑창 접촉/미끄러짐 검수가 끝났다는 뜻이 아니다.

새 최종 이동 모션·몬스터 모션·Animator 연결·After 영상은 아직 완료되지 않았다.

## 추가 검수 결과

폴라리스는 작은 중복 표면을 제거/분리하여 99,998 삼각형에서 경계·비정상 에지·퇴화 면이 0인 정리본을 만들었다. 에지만 정상이어도 한 정점에 두 표면이 붙은 경우가 있을 수 있어 정점 주변 면 연결도 추가 검사한다. 리그 시연에서 부츠가 케이프에 연결된 오류를 찾아 수정했고, 전용 헤어/케이프/코트 본 12개를 더한 64개 본 후보를 검증 중이다. 이 포즈들은 최종 보행 클립이 아니다.

스텔라의 미세 표면 재구성+4K 베이크 후보 `StellaBaked15`는 **시각 검수 불합격**이다. 얼굴과 머리카락에 검은 삼각형이 생기고 자수가 흐려졌으므로 리깅/게임 반입에 사용하지 않는다. 데이터상 경계 에지가 0이라는 사실만으로 시각 품질을 통과시킬 수 없다.

대안으로 원본 UV를 유지한 50만 삼각형 후보는 원본 대비 거리 p99 0.584mm, 최대2.246mm로 수치 검수를 통과했다. 이어서 40만 후보를 만들고 원본과 직접 비교 중이다. 이 수치는 **품질 검토용 LOD0 예산 후보**이며, PC 프레임 비용을 통과한 최종 예산으로 확정한 상태가 아니다. 원본 디자인 보존과 실행 성능을 함께 검증한 뒤 게임용으로 선택한다.

## 2026-09-14 추가 진행 — 정리본 및 실제 Unity 후보

스텔라의 **원본에서 직접 감축한 40만 후보**를 보수적으로 정리했다. `Processed/Stella400kDirect/Stella_repaired.blend`는 400,184 삼각형·150,152 정점이며 비매니폴드 정점/에지, 경계, 퇴화 면, 중복 삼각형이 모두 0이다. 붙어 있던 부츠의 접합 모서리 8개만 분리했고 기존 399,847면의 위치·UV·재질이 동일함을 확인했다. 원본→최종 후보 10만 점 표면 검사는 p99 0.788mm/최대2.702mm다. 원본 packed map 3개의 바이트/색공간도 유지했다.

`Processed/Stella400kDirect/Review`에 원본과 동일한 카메라의 전신 세 각도·얼굴·상체 확대 다섯 장을 만들고 직접 확인했다. 머리/브로치의 감축 각짐은 남지만 실패한 재베이크 후보의 검은 삼각 구멍과 문양 번짐은 없다. 이 후보로 별도 체형에 맞는 리깅을 시작하며, Unity 변형 품질과 PC 성능을 통과한 최종 모델이라고 표시하지 않는다.

폴라리스는 최종 연결 정리본(49,473 정점·99,998 삼각형)에 64본 리그를 연결했다. V4Final을 실제 Unity에서 300프레임 녹화한 `Motion/Before/NewRigBaseline01/Polaris`는 **모델을 교체하고 기존 Walking_A/Running_A를 그대로 사용한 진단 자료**다. 폴라리스 캡처/카탈로그 원복 검사는 통과했지만, 이어지는 제외된 스텔라 테스트의 cleanup 씬 중복으로 전체 XML은 실패다. 제외 테스트의 정리 코드를 수정했으며 별도 재검증한다. 이 오류를 숨겨 전체 실행이 통과했다고 표시하지 않는다. MP4 재디코딩 여섯 프레임의 원본 이미지 대조도 통과했다.

이 실제 영상에서 높은 손 자세와 과한 발 들림이 확인됐다. 기존 클립을 그대로 리타겟하는 것만으로 완료할 수 없어, 접지/스윙 궤적·체중 이동·방향 전환 디딤을 가진 별도 Humanoid 모션을 저작 중이다.

`RigReview/PolarisCoatV6`는 하단 코트/다리를 실제 메시 연결 관계로 분류한 가중치 수정본이다. 동일 보행 포즈에서 하단 코트와 다리의 3배 이상·10mm 이상 늘어난 모서리는 모두 0으로 줄었다. 강한 관절 시험에서는 소매 뒤 늘어남 등이 남아 최종 스키닝 검수는 계속한다. V4Final 비교 자료는 보존했다.

V6는 Unity Humanoid 임포트와 URP 셰이더 컴파일을 통과했다. 전체 원본 atlas를 사용하므로 구형 얼굴 UV 전용 색/조명 처리를 강제 적용하지 않는다. 공통 툰 그래프에 선택적 tangent normal 입력을 연결했고 기본 강도 0으로 기존 머티리얼 동작을 유지했다. 새 atlas만 원본 normal map 강도 1을 사용한다. `UnityPreview/Polaris/CoatV6Normal01`에 동일 Idle 포즈·카메라·조명으로 Normal Off/On 전신/얼굴/상체 여섯 장과 조건 JSON을 저장했다. 이 정지 화면은 모션 개선이나 성능 통과 자료가 아니다.

보스 5종의 정리 후보 수치/원본 해시/UV 보존은 `BossReduction/FinalManifest.json`에 동결했다. 삼각형 수는 불79,998 / 수79,992 / 암99,968 / 풍119,930 / 뢰119,978이다. 모두 10만 점 표면 검사와 정점·에지 연결 검사를 통과했으며, 실제 리그 포즈와 Unity 표시를 순서대로 검수한다.

새 모션 연결용 Motor/Combo/Traversal/Roster/스킬/Timeline 수정안은 `Tools/CharacterPipeline/PendingIntegration`에만 준비했다. 기존 이동 수치·피해 판정·스태미나·피티/세이브 규칙은 바꾸지 않는다. 다섯 런타임 어셈블리의 정적 컴파일을 통과했지만 아직 실제 게임 Assets에 활성화하지 않았다.

## 리타겟 기준 원점 수정 및 두 번째 리그 검수

`AvatarScale/Origin01.json`은 기존 두 주인공·동료와 폴라리스 V6의 원점 전후를 실제 Unity에서 측정한 기록이다. 몸 중앙을 원점으로 내보낸 폴라리스는 `Animator.humanScale=0.198023`이었다. 메시/리그 전체를 바닥 기준으로 평행이동해 **새 경로와 새 importer meta**로 임포트하자 `1.147440`으로 정상화됐다. 두 버전의 원래 메시 높이 1.899075m, 다리 길이 0.876248m, 발바닥 위 골반 높이 1.031117m는 동일하다. 이 차이는 체형을 고치거나 발 모션을 과장해 보정한 결과가 아니다.

- 내보내기 및 원본 불변 검사: `RigReview/PolarisV6GroundedExport/Polaris_grounded_export.json`
- 바닥 기준 FBX SHA-256: `2f973a7ecb587a039fde9c6441361f21cc49fbad60d58f6f1bd58f5ae47bba66`
- 분리된 Unity 후보: `Assets/Orbis/Game/Characters/Candidates/OriginTrial01/Polaris`
- 임포트/프리뷰 도구에 `-characterPipelineRoot`를 추가해 기존 raw 원점 후보 및 비교 자료를 보존한다.

앞선 `NewRigBaseline01`의 과한 발 들림/작은 골반 움직임에는 잘못된 Avatar 기준이 영향을 줬다. 따라서 그 자료만으로 CC0 클립 자체의 문제를 확정하지 않는다. 정상 원점의 동일 캐릭터로 기존 클립 Before를 다시 찍은 뒤 새 모션과 비교한다. 잘못된 Avatar를 대상으로 만든 `Motion01` 클립은 진단 자료로 보류하며 게임에 연결하지 않는다. 실제 host 이동 실험에서 Unity 6000.6의 `GetHumanPose.bodyPosition`은 root-relative로 관측됐으므로 이 관측을 정상 Avatar의 새 저작 기준에 반영한다.

스텔라 V2→V3는 원본과 연결된 소매/장식 성분만 해당 팔을 따라가도록 가중치를 수정했다. 같은 각도 실제 `Relaxed` 렌더에서 팔 밖으로 수평으로 남던 장식이 사라졌다. `RigReview/Stella400kV3/RenderReview`에 전신 12장과 손 기본/굽힘 4장이 있다. 본 계약·원본 메시/UV는 유지하며, V3를 바닥 원점으로 내보낸 뒤 Unity 검수한다.

불·물 보스 V2 실제 포즈 확대에서 인접 다리와 윗턱으로 가중치가 새는 문제가 확인돼 수정 중이다. 수치만으로 승인하지 않는다. `PendingBossAnimation`에 기존 M4 예고/회복 시간과 동기화하는 Generic 시각 연결 코드, 6클립 프로필, 중단/사망/히트스톱 검사를 준비했다. 런타임·테스트 어셈블리의 정적 C# 컴파일만 통과했으며 **테스트 실행/실제 보스 애니메이션 연결은 아직 전**이다.

## 실제 임포트에서 확인한 추가 문제와 유효한 증거

스텔라 V3의 바닥 원점 FBX도 Unity 6000.6 Humanoid 임포트를 통과했다. `StellaOrigin01.json`의 humanScale은 1.139280, 원래 보이는 높이 1.902879m, 바닥 위 골반 높이 1.042867m다. `UnityPreview/Stella/StellaV3Grounded01`에 동일 고정 Idle 포즈의 Normal Off/On 전신·얼굴·상체 여섯 장이 있다.

하지만 다리 표면의 검은 점을 발견해 **최종 표시 합격은 보류**했다. `StellaSurfaceDiagnostic01`에는 같은 카메라에서 outline On/Off × key shadow On/Off 16장을 기록했다. 그림자를 끄는 것만으로 사라지지 않고, outline을 끄면 갈색 얼룩은 줄지만 표면 잡티는 남는다. `SurfaceDiagnostics/Stella01/Comparison.json`에서 원본/정리본 다리의 다수 미세 접힘과 UV corner 분할을 확인했다. Blender 150,152정점이 Unity 677,291정점으로 늘어난 이유의 대부분은 원본 UV의 분할이다. 동일 topology 평활 후보는 29~58mm 변위와 접힘 잔류로 탈락했다. 얼굴·헤어·의상·현 V3를 그대로 둔 별도 다리 국소 리토폴로지/색 전사 후보를 실험 중이다. 아직 이 후보를 게임이나 모션 검수에 적용하지 않았다.

정상 원점 폴라리스 Before 재촬영 `NewRigBaseline02`는 300프레임이며 Polaris Passed / Stella 선택 제외 Ignored / failed 0이다. 기존 cleanup 실패가 재현되지 않았고 카탈로그가 원복됐다. MP4 300프레임 재디코드 검사도 통과했다. 이는 실제 플레이 모드 프레임 진행으로 녹화한 유효한 비교 기준이다.

`Motion02` 후보의 첫 Editor Quick/Full 스틸은 **애니메이션 외형 승인 자료로 사용하지 않는다**. 같은 Update에서 반복 렌더할 때 GPU skin cache가 앞선 자세를 보여 주는 문제가 있었기 때문이다. 실제 BakeMesh 측정은 움직였지만 검수 이미지가 이를 반영하지 않았다. 후보와 outline의 `forceMatrixRecalculationPerRender`를 활성화한 `Review02Recalc`에서 최대 보폭 이미지와 실제 스킨 측정이 일치하는 것을 확인했다. 이 설정은 검수 인스턴스에만 적용하며 일반 런타임 설정을 바꾸지 않았다. 근거는 [Unity 공식 API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer-forceMatrixRecalculationPerRender.html)다. 최종 After는 실제 플레이 영상으로도 검증한다.

불 V3/물 V4는 동일 관절 자세의 실제 Blender 재렌더에서 이웃 다리/윗턱 가중치 누출이 수정됐다. `BossFBXReview`의 원본 불변·왕복 변환 검사를 거쳐 `BossCandidates/Import01`에 별도 임포트했다. `UnityBossImport/Import01`에서 불30본·79,998tri·61,457 imported vertices / 물20본·79,992tri·66,646 imported vertices가 유효한 Generic Avatar로 확인됐다. 이 검사는 `-nographics` 임포트이며 Unity 최종 화면·큰 동작·게임 연결·성능을 통과했다는 뜻은 아니다. 암/풍/뢰는 실제 포즈 확대에서 남은 국소 가중치 전이를 수정 중이다.

## 보스 5종 Unity 정지 표시 검수 및 전투 시각 시간 검사

암 V4/풍 V5/뢰 V5는 실제 작은 관절 포즈 재렌더 후 FBX 왕복 검사를 마쳤다. `BossCandidates/Import01`에 모두 유효한 Generic Avatar로 임포트됐다. 암17본·99,968tri·90,618 imported vertices / 풍31본·119,930tri·123,189 vertices / 뢰31본·119,978tri·92,238 vertices다. 땅 위 보스는 실제 발바닥, 물 보스는 유영 중심을 원점 기준으로 기록했다. 풍 꼬리 끝이 발바닥보다 낮은 것은 임포트 전체를 띄워 숨기지 않고 별도 Idle 포즈에서 검수한다.

`UnityBossPreview/Rest02`는 5종 × 정면/3·4각 × outline Off/On의 실제 Unity URP 20장이다. 같은 카메라·조명·원본 atlas를 사용하고 outline만 토글했다. 각 종 3·4각을 직접 확인했고 `PixelAudit.json`으로 비어 있지 않은 이미지·1.8m 검수 높이·outline 픽셀 차이를 확인했다. 게임 내 크기를 1.8m로 변경했다는 뜻은 아니다.

앞선 `UnityBossPreview/Import01` 여덟 장은 검수 도구의 BakeMesh scale 처리 혼용으로 카메라가 far clip 밖에 놓여 빈 화면이므로 **표시 합격 자료가 아니다**. 해당 원본 기록을 보존하고 실제 지오메트리 크기 일치 검사를 추가했다. `UnityBossRest02.log`는 exit0이다.

새 `BossPoseClock` / `BossMotionProfile` / `BossAnimationPresenter` / `BossMotionControllerBuilder`는 검증용 프로젝트 Assets에 컴파일했지만 Field 오브젝트에는 아직 연결하지 않았다. `BossPoseClockTests01.xml`에서 기존 M4 공격 시점, 약점 노출 중단, 저장된 사망/신규 사망, 히트스톱, 큰 deltaTime의 공격 중복 방지 **5개 실제 Unity EditMode 검사 모두 통과**했다. 보스 6클립·큰 변형·실제 인게임 연결·성능은 다음 검수 대상이다.

주인공 PendingIntegration에는 전용 `Visual Facing` 자식을 추가해 새 드라이버에 전달하는 안을 반영했다. 이동 중 실제 루트/카메라 회전을 바꾸지 않고 시각 회전과 발 방향을 맞추기 위한 준비다. 갱신한 5개 런타임 어셈블리 정적 컴파일은 통과했으며 실제 턴 영상 합격은 아직 아니다.
# 2026-09-15 추가 실제 엔진 검증 — 작업 계속 진행 중

- 불 보스: 사용자 승인대로 여섯 다리와 말린 꼬리 보존. FootV6 rest/motion 원본을 별도 Import02 후보에 연결했다.
- 물/불/풍/뢰 보스: 기존 M4FieldBoss의 실제 Update 및 Animator로 전투 연동 테스트 각각1 passed/0 failed. 프레임 수는 물467 / 불451 / 풍421 / 뢰432다. 원래 피해 이벤트1회, 약점 노출, 피격 레이어, 히트스톱, 리셋, 저장된 사망 상태를 검사했다. 신규 이동 AI나 피해 이벤트를 넣지 않았다.
- 실제 MP4는 `TestResults/CharacterPipeline/BossRuntimePlayback/Runtime01/<Boss>/runtime.mp4`. 인코딩 후 다시 디코딩해 프레임 수/화면크기/샘플 픽셀을 확인했다. 영상은 스튜디오 검수이며 Field 배치/성능 통과를 뜻하지 않는다.
- 풍/뢰:31본 Generic, 각각119,930/119,978삼각형. FBX 원점/본/원본 맵 보존 및6클립 binding 검사, Unity42장씩 실제 포즈를 확보했다. 네 다리+두 날개 원본 형태 유지. Dead는 웅크려 정지하는 프로토타입이며 완전한 옆으로 쓰러짐이 아니다.
- 폴라리스 After05: 실제300이동+300전투 프레임,1 passed/0 failed/1선택제외. 이미 접지를 푸는 발에 두 번째 recovery arc를 덧붙이는 문제가 제거됐다. Stop 종료 발 이동은0.245mm 수준으로 유지된다. Walk→Run 재생 속도 급증은 별도 After06 후보로 검증 예정이며, 공중 스윙 거리와 접지 미끄러짐을 구분해 기록한다.
- 파지 Transfer03: 실제 Unity Humanoid 손가락20채널만 보정. 원본 직접 포즈 대비 손 기준 최대 bone 오차6.50→3.48mm, 손 표면18.95→8.36mm. 원본 메시/본/검은 불변. 아직 실제 이동·공격 중 검 부착 검수 전이며 production 카탈로그에 적용하지 않았다.
- 스텔라 Integrated06: 유효한 Humanoid,66본/2재질,274,247tri/417,072 imported vertices. Unity18장 normal/outline 분리 비교에서 중앙 다리는 개선됐으나 상하 연결 띠/원본 장식 뒷면 아웃라인 문제가 남아 추가 국소 수정 중이다.
- 바위 보스: 어깨에 섞인 다리 가중치를 수정하는 별도 후보 검수 중이다. 기존 원본/Field는 유지한다.

현재 원본 `Assets/blend`와 캐릭터 카탈로그·Field 씬의 최종 교체는 하지 않았다. 모든 파일을 원본과 함께 무조건 적용하지 말고, 최종 채택 후보/의존성 목록 및 source SHA 비교 후 한 번에 연결한다. 일반 몬스터/코스모는 사용자 원본을 기다리는 기존 보류 결정을 유지한다.

## 2026-09-15 최신 검수 상태 (위 중간 후보 상태를 갱신)

아직 최종 통합 완료가 아니다. 원본 `D:/Project/ORBIS`와 supplied `Assets/blend`는 보존한다.

- 보스 **5종 모두** 기존 M4 실제 Update/Animator 전투 검수 통과. 추가 바위 Runtime01은492프레임이며 MP4 디코딩 검증까지 완료했다. 최종 후보는 불/물 FootV6, 풍 FootV6+MotionV5, 뢰 FootV6+MotionV4, 바위 UpperV11+MotionV10이다. 삼각형 수는 이전 동결 수치와 동일. 바위 Dead의 국소 pelvis edge32.43mm 등 남은 진단 한계는 별도 리뷰에 유지한다.
- `FieldIntegration/BossVisual04`에 기존 Field 동일 카메라 Before/After20장을 확보했다. 첫 프레임 셰이더 컴파일/Volume 초기화를 보완한04에서는 Fire 원본 색이 정상이다. root Actor/Collider/Weakness Core ID와 월드 좌표가 보존된다. **아직 Field에 저장하지 않았다.** 큰 몸통이 기존 약점 표지를 가려 사용자에게 머리 위/몸 앞 위치를 질문했다. 실제 공격 판정은 수정 표지 위치와 무관하게 기존 root 전체를 사용한다.
- 폴라리스 최신 평지 After08은300이동+300액션 통과. 기존에 종료 중 다시 추가되던 발 회복 동작과 속도 전환 문제를 줄였다. 약접지 단일 프레임16.2mm 잔여 관통은 남아 있다. 검 파지 Transfer03을 적용한 별도26클립 후보 `Motion06GripRuntime01`을 사용한다.
- 실제12/24도 경사면에서는 low-contact swing 관통이 최대280mm 발생했다. 이를 합격 처리하지 않고 `PendingAfter09Terrain` 수정/독립 검토 중이다. `PolarisTerrainCameraBaseline01`은 지면 위로 높인 카메라로 같은 기존 IK를 다시300평지+300액션+900경사 프레임 기록한 유효 Before다. 기존 낮은 카메라의24도 캡처는 지면 안에 있으므로 시각 승인 근거로 사용하지 않는다. 기존 downhill은 실제 경사6프레임뿐이어서 별도 긴 내리막 검증을 준비했다.
- 스텔라 최종 표면 후보 `StellaIntegrated12`는220,138tri/66본/2재질, Unity imported284,450vertices다. Integrated11과 형상/UV/가중치/본은 동일하며12는 국소 leg atlas 정리다. Unity normal/outline 비교 및 실제26개 retarget/저작 클립61샘플씩 확인. 부츠 상단의 작은 띠/원본 장식 점은 남아 있다.
- 스텔라 자신의 측정값으로 `Stella12MotionRuntime01`, 손가락20채널만 바꾼 `Stella12GripRuntime01` 별도 후보 생성. Grip04를 실제 Unity Humanoid로 옮긴 `Stella12Transfer01`은 최종 손 기준 bone3.13mm/skin7.06mm 오차이며3각도 클로즈업을 확인했다. 원본 장갑의 각진 형태와 엄지 쪽 여유는 잔여 한계다. 완벽한 손 접촉이라고 주장하지 않는다.
- `Stella12Baseline02`는 정확한 스텔라 단일 테스트 필터로300프레임,1 passed/0 failed/0 skipped. 클래스 필터로 먼저 선택제외 캐릭터를 실행한 Baseline01은 테스트 진입에서 멈춰 root가 해당 owned Unity 프로세스만 종료했고 로그를 보존했다. 동일 모델 신규26동작의 실제 motor/terrain 검증은 다음 단계다.
- 통합 독립 검토에서 G 스킬 첫 프레임의 시각 fallback 선행을 발견했다. 기존 FSM 초기 normalized time을 TryStart 직후 전달하는1문장만 수정(피해/쿨다운/소유권 불변), 정적 컴파일 통과. 다음 실제 After에서 첫 프레임0을 확인한다.
- `DeliveryPreparation/TargetAudit01.json`: baseline1,793개 중 target 변경/누락0, staging 기존 변경8. 새 Runtime8개+각 meta가 별도 필수다. 최종 모델/클립/Field의 실제 재귀 의존성 manifest를 확정한 뒤 반영한다. 전체 후보 폴더 무차별 복사 금지.

일반 필드 몬스터 원본 및 코스모는 사용자 제공 대기. Mixamo 미제공이므로 검증된 CC0 KayKit+직접 제작 동작을 사용한다. 원본 라이선스의 사용자가 제공한 모델 출처 확인과 외부 asset license를 구분하며 Mixamo 검증을 했다고 기록하지 않는다. 완료 전 GitHub push/PC 종료를 실행하지 않는다.

## 2026-09-15 — 저장된 Field / 주인공 최종 연결 검증

이 항목이 아래 범위의 최신 상태이며 타깃 전달·GitHub 업로드 완료 보고는 아니다.

- 보스 5종: Field BossVisual05 저장 완료. `Assets/Scenes/Field.unity` SHA `bfc7347b2e49ebf07d13289463d413e666fde53f5e6c8f0adb2a98dc345103d5`. 불은 승인된 6다리+말린 꼬리, 물은 유영형, 풍·뢰는 4다리+2날개를 유지한다.
- SavedField03 실제 PlayMode 1 passed / 0 failed / 0 skipped. 5종 Actor/Core/Collider와 root 위치 보존, Idle 재생, 불 보스 실제 공격 1회·약점 노출 연결 확인. CPU 스키닝과 Bake(true) 차이는 최대 0.1931mm. 전체 발/아트/FPS 검증을 뜻하지 않는다. 앞선 SavedField02 실패는 Bake(false) 좌표 계산 오류였으며 검수 도구를 수정했다.
- 폴라리스 After10의 경사→평지 발 침투를 -74.509mm에서 +0.0687mm로 수정. 신규 경사 솔버는 평지 300프레임의 기존 관절/발바닥 좌표를 보존했다. 기존 약한 접촉의 -8.54mm 잔차는 남는다.
- 스텔라 Stella12TerrainAfter10 실제 1500프레임 검사 통과. 평지300 좌표는 After08과 같고 경사 보정 중 최저 약 -0.202mm. 기존 약한 접촉/회복발 간격은 남으며 모든 프레임 완전 밀착으로 표현하지 않는다.
- PolarisDownhillAfter10 / StellaDownhillAfter10 각각 실제600프레임 검사 통과. 24도 경사에서 5초 연속 19.5m 내리막 보행·달리기를 확인. 지형별 최저 -0.125mm/-0.223mm, 표면 측정 누락 0.
- HeroFinalBinding/FinalHeroes01 Review→Commit 성공. 카탈로그 SHA `7a9bae62b00ccf2467b774ca358311a5e5446ee7ba5581e2987c981ba9323916`. 두 주인공의 prefab/avatar/controller와 측정한 검 소켓만 연결했고 SourceId/저장 정체성/무기/다른 동료는 보존했다. 선택 안내의 오래된 ‘동일한 회색박스’ 문구 2개도 수정했다.
- 실행용 Field export: 첫 배치 호출은 별도 프로세스의 빈 시작 씬 때문에 실패했고 기존 출력이 복구됐다. 동일 프로세스 안에서 Field 열기→기존 exporter 실행하도록 batch helper를 추가해 검증 중이다. 원래 exporter의 분할/소유권 로직은 변경하지 않았다.
- 원본 7종 SHA 일치. Cosmo와 일반 몬스터는 사용자 원본 대기/보류. 공개 GitHub 저장소에는 사용자 제공 원본·파생 에셋의 출처/공개 배포권리 답변을 받기 전 업로드하지 않는다. 약점 수정 표시 위치도 사용자 답변 대기이며 현재 기존 위치를 유지한다.

현재 남은 일: 경사 상태 해제 검증, 실행용 씬 export/회귀/성능 확인, 정확한 의존성 목록에 따른 타깃 전달. GitHub와 종료는 아직 실행하지 않았다.

## 2026-09-15 — staging 통합·출력 검증 완료, 타깃 전달 전

이 항목은 위의 ‘export 검증 중 / 경사 상태 해제 대기’를 갱신한다. 이전 실패·후보 기록은 보존한다.

- 두 주인공 TerrainLifecycle01은 각각 1 passed / 0 failed / 0 skipped로 상태 해제·재진입을 확인했다. After10 및 연속 내리막 정량 검증은 완료했다. 단 `PolarisGripTerrain02/03`의 Slope24 frame0157 JPEG가 동일해 그 이미지 쌍을 경사 개선의 시각 증거로 사용하지 않는다. 수치 -74.509mm→+0.0687mm와 이미지 검증의 유효성은 구분하며, 해당 캡처는 증거 범위의 한계로 기록한다. 추가 엔진 결함이나 재시험 필요로 판정한 것은 아니다.
- `CharacterFieldExport02`는 같은 프로세스에서 원본 Field 열기→기존 exporter를 실행해 성공했다. Collider 4,952개, Climbable 99개, unmatched 0. 원본 Field SHA는 `bfc7347b2e49ebf07d13289463d413e666fde53f5e6c8f0adb2a98dc345103d5`로 유지됐다.
- Field 로직 29 / Island 런타임 4 / Streaming 2 / BuildPolicy 1, 합계 36개 관련 회귀가 모두 통과했다. 직접 저장 Field의 SavedField03 1개 및 lifecycle 2개는 이 36개와 별도다.
- `CharacterFieldOcclusion01` 성공: 6개 씬에 occluder 648 / occludee 1,554 연결, PVS 10,583,248bytes. 생성 성공만으로 FPS 개선을 주장하지 않는다.
- `CharacterReleaseBuild01`은 Windows Release 553,529,152bytes, 약100초, 오류0/BuildReport 경고2로 성공했다. Build Settings 복원도 true다. 기존 obsolete/CS0252, BuildLayout 및 Editor 종료 JobTempAlloc 로그 경고는 유지하며 원인 해결로 기록하지 않는다.
- `CharacterPackedSmoke01`은 1920×1080에서 5지점 각15초 실제 URP 렌더와 Packed 지역 `[5,0,5]` 로드/해제/재로드를 완료했다. complete=true/error 없음. 창 표시 없는 오프스크린 검사이므로 실제 게임 화면 FPS 달성 자료가 아니다.
- `HeroFinalBinding/FinalHeroes01/FinalAll01.json` 생성 완료. Hero commit과 Field export source dependency hash/7출력 SHA, 17개 시작 에셋의 재귀 의존성, 최신 runtime/UI·파일/폴더 meta·package를 검사했다. `heroCommitValidated=true`, `fieldExportValidated=true`, `sourcesUnchangedDuringInventory=true`, `needsOcclusionBake=false`. 이는 정확한 전달 목록이며 타깃 복사는 아직이다.
- 사용자용 최신 설명은 `Field_Animation_Final.md`, 정확한 원본/모델/클립 매핑은 갱신한 `Final_Asset_Mapping_Draft.md`에 정리했다. 원본 얼굴·헤어·상체·본 보존과 스텔라 국소 다리 처리의 한계는 유지한다. 현재 결과를 몬드 수준의 완성 아트라고 표현하지 않는다.

남은 일은 검증된 목록에 따른 `D:/Project/ORBIS` 전달과 실제 화면 표시 성능 검증이다. Core 표지 위치·공개 배포 권리 답변, Cosmo 및 일반 몬스터 자료는 별도 보류다. GitHub 업로드와 PC 종료는 아직 실행하지 않았다. 이번 갱신은 Docs 3개만 수정했으며 Assets/Tools/타깃을 변경하지 않았다.
