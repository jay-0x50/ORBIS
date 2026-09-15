# 스텔라·폴라리스 이동 애니메이션 적용 및 검증

2026-09-15 기준. 스테이징 프로젝트의 주인공 카탈로그에 두 캐릭터의 Humanoid 모델, 26개 모션, 검 파지 보정 및 경사 접지 보정을 연결했다. 실제 PlayerMotor 입력으로 평지·경사·전투 상태 전환을 녹화했고, 아래 범위에서 동작을 확인했다. 남은 발 접촉 오차와 성능 미검증 항목도 함께 기록한다.

**현재 선택된 애셋**

[Art 카탈로그](../../Assets/Orbis/Art/Resources/Art/Catalog.asset)의 `Explorers` GUID와 프리팹 내부의 프로필 참조를 직접 대조했다. 폴더 이름에 `Candidates`가 남아 있지만 아래 두 항목이 현재 카탈로그에서 선택된 버전이다.

| 캐릭터 | 연결된 프리팹 | 컨트롤러·프로필 |
|---|---|---|
| 폴라리스 | [Motion06GripRuntime01 / Polaris.prefab](../../Assets/Orbis/Game/Characters/Candidates/Motion06GripRuntime01/Polaris/Polaris.prefab) | [Character.controller](../../Assets/Orbis/Characters/Animation/Motion06GripRuntime01/Polaris/Character.controller), [MotionProfile.asset](../../Assets/Orbis/Characters/Animation/Motion06GripRuntime01/Polaris/MotionProfile.asset) |
| 스텔라 | [Stella12GripRuntime01 / Stella.prefab](../../Assets/Orbis/Game/Characters/Candidates/Stella12GripRuntime01/Stella/Stella.prefab) | [Character.controller](../../Assets/Orbis/Characters/Animation/Stella12GripRuntime01/Stella/Character.controller), [MotionProfile.asset](../../Assets/Orbis/Characters/Animation/Stella12GripRuntime01/Stella/MotionProfile.asset) |

폴라리스는 바닥 원점을 교정한 V6 리그, 스텔라는 Integrated12 리그를 사용한다. 중간 후보에서 몸 중앙 원점 때문에 잘못 계산되던 Humanoid 스케일을 먼저 수정했다. 이후 실제 리그 길이와 발바닥 위치를 기준으로 모션을 저작·보정했다. 두 프로필에는 이동 주기 거리, 좌우 접지 곡선, 발바닥 기준점, 각 클립의 출처가 저장된다. 경사 보정용 발바닥 표본은 폴라리스 좌우 87/77점, 스텔라 26/27점이다. 스텔라의 표본은 하이힐의 실제 최저 접촉면이며 부츠 전체 표면 검사를 뜻하지 않는다.

**26개 모션과 제어 방식**

| 구분 | 슬롯 | 출처 |
|---|---|---|
| 대기·공중 | Idle, Air | 프로젝트 직접 저작 |
| 걷기·달리기 | Walk, Run, WalkLeft, WalkRight, WalkBack, RunLeft, RunRight, RunBack | 프로젝트 직접 저작 |
| 상체 방향 보조 | SteeringNeutral, SteeringLeft, SteeringRight | 프로젝트 직접 저작 |
| 제자리 방향 전환 | TurnLeft90, TurnRight90, TurnLeft180, TurnRight180 | 프로젝트 직접 저작 |
| 정지 디딤 | StopLeft, StopRight | 프로젝트 직접 저작 |
| 전투 | Attack1, Attack2, Attack3, Skill, Burst, Hurt, Dead | KayKit Character Animations 1.1, CC0 리타겟 |

총 19개 직접 저작 모션과 7개 CC0 모션이다. 정지 디딤 2개는 기존 슬롯 번호를 유지하며 뒤에 추가했다. 전투 클립에는 새 검을 쥐는 손가락 포즈를 반영했다. CC0 출처와 선택 파일 해시는 기존 소싱 기록 및 각 프로필의 `SourceLicense`에 남아 있다.

이동 블렌딩은 충돌 처리 후 실제 속도를 사용하고, 블렌드트리의 실제 주기 길이를 고려해 보폭과 재생 속도를 맞춘다. 이동 중 급회전은 시각 모델의 회전 지연과 방향별 모션으로 처리하며, 거의 정지한 상태에서는 별도 디딤 턴을 사용한다. 게임플레이 루트·카메라 방향과 이동 수치는 유지한다. 기본 걷기 2.5m/s, 달리기 6m/s를 애니메이션에 맞추려고 변경하지 않았다.

기존 Idle/Move/Attack/Skill/Burst/Hurt/Dead FSM이 상태를 소유한다. 기본공격의 타격 구간·콤보 입력·피해 판정은 기존 `ComboSequence` 시간이 기준이고, 새 Animator는 그 정규화 시간을 받아 포즈를 재생한다. Skill/Burst도 기존 소유자의 시간을 전달받는다. 새 드라이버는 전체 Animator 속도를 0으로 묶지 않아 다른 레이어와 복구 경로를 유지한다. 공격·스킬 중 이동 잠금, 피격 중단, 히트스톱 및 궁극기 취소 규칙도 유지했다.

**실제 검증 결과**

아래 거리는 실제 스킨 발바닥 표본과 테스트 지면 사이의 부호 있는 거리다. 음수는 지면 안쪽이다. 발목 높이만으로 관통을 판정하지 않았다. 영상은 Unity URP 960×720, 고정 30fps로 기록했다.

| 검사 | 폴라리스 | 스텔라 |
|---|---|---|
| 평지 300 + 액션 300 + 12도/24도/횡경사 각 300프레임 | `PolarisGripTerrain03`: 1,500프레임, 테스트 1 통과/0 실패 | `Stella12TerrainAfter10`: 1,500프레임, 테스트 1 통과/0 실패 |
| 실제 경사 보정이 켜진 발의 최저 거리 | 12도 −0.084mm, 24도 −0.112mm, 횡경사 −0.223mm | 12도 −0.082mm, 24도 −0.202mm, 횡경사 −0.160mm |
| 경사 검사 전체 구간의 최저 거리 — 평지 전환 포함 | 12/24도 −8.537mm, 횡경사 −0.223mm | 12도 −10.178mm, 24도 −10.170mm, 횡경사 −0.160mm |
| 별도 24도 연속 내리막 | `PolarisDownhillAfter10`: 600프레임, 1 통과/0 실패, 최저 −0.125mm | `StellaDownhillAfter10`: 600프레임, 1 통과/0 실패, 최저 −0.223mm |
| 실제 경사에서 초기화·워프·Dead·재활성화 | `PolarisTerrainLifecycle01`: 5항목 통과 | `StellaTerrainLifecycle01`: 5항목 통과 |

After10은 경사와 평지를 한 발씩 밟는 순간의 공통 골반 보정을 양발에 반영하고, 평지로 돌아올 때 필요한 만큼만 접지 목표를 넘겨준다. 폴라리스 24도 경계의 문제 프레임157은 측정 JSON에서 이전 −74.509mm 관통에서 +0.0687mm로 개선됐다. 이후158/159도 0/−0.0153mm였다. 이 개선 판정은 실제 스킨 표본의 정량 기록에 근거한다. 같은 번호의 JPG157 두 장은 파일 해시가 같아 그 이미지로 시각적 개선을 증명할 수 없다. 처음부터 평지에서 시작하는 300프레임의 기존 관절·발바닥 좌표는 After10 이전과 같았다.

기존 경사 레시피의 `Downhill`이라는 구간 이름만으로 내리막 검증을 주장하지 않았다. 별도 내리막 검사에서는 실제 24도 경사 위에서 150프레임, 5초 동안 연속 하강했다. 수평 19.5m를 이동하며 높이 8.654m가 내려갔고, 양발의 지면 샘플 누락은 두 캐릭터 모두 0이었다. 내리막에서 최대 발목 수직 보정은 폴라리스 0.217m, 스텔라 0.248m로 기존 0.32m 상한 안이었다.

Lifecycle 검사는 경사 보정이 실제로 켜진 뒤 `ResetContacts`, 경사→평지 워프, 다른 경사 위치로 워프, Dead FSM 진입/종료, 모델 비활성화/재활성화를 실행했다. 캐릭터당 13개 체크포인트에서 유효한 수치를 기록했고, 초기화 때 지형 상태·골반 잔여 보정·IK 가중치가 0으로 해제됐다. 다시 경사에 서면 양발 접지가 복원됐으며 메시 측정 시도는 계속 1회였다. 정상 재접지 체크포인트의 최저 거리는 폴라리스 +0.007~+0.112mm, 스텔라 +0.084~+0.098mm였다. 이 검사는 공개 Dead 상태 전환과 이동 잠금을 확인한 것으로, HP를 0으로 만들어 구조·부활 콘텐츠까지 시험한 것은 아니다. Dead 중에는 의도대로 IK가 꺼지므로 그때의 발 관통을 접지 성공 수치에 포함하지 않았다.

콤보의 실제 시계와 공격 포즈 파라미터, 공격 중 이동 잠금을 검증했다. 궁극기는 기존 `UnscaledGameTime` Timeline과 실시간 히트스톱 시계를 사용하므로 고정 프레임 캡처라도 실행별 종료 프레임이 조금 달라진다. 궁극기 전체 프레임을 이전 실행과 완전히 동일한 결정적 재생이라고 주장하지 않는다.

**직접 확인한 실제 캡처**

아래 첫 두 장은 같은 모델·입력·카메라 설정의 이전/이후 실행 JPG158이다. 직접 열어 보았을 때 이전 출력에서는 앞쪽 부츠가 지면에 파묻히고, After10 출력에서는 부츠 끝이 드러나는 차이가 보인다. 이 관찰은 위 JSON157의 수치와 독립적으로 기록한다. JPG157 두 장은 해시가 같으므로 동일 시점의 시각 증거가 아니다. 각 실행은 서로 다른 300개 이미지이며 JPG158/159도 실행 간에 다르다. 스킨 측정과 수동 URP 렌더의 포즈 시점 차이 또는 GPU 갱신 지연이 원인 후보지만 아직 확정하지 않았다. 테스트 지면은 접지를 읽기 위한 검사 공간이며 최종 Field 아트 화면이 아니다.

- [폴라리스 이전 실행 JPG158 — 앞쪽 부츠 관통 관찰](../../TestResults/CharacterPipeline/Motion/After/PolarisGripTerrain02/Polaris/Terrain/Slope24/frame_0158.jpg)
- [폴라리스 After10 JPG158 — 부츠 끝이 드러나는 차이 관찰](../../TestResults/CharacterPipeline/Motion/After/PolarisGripTerrain03/Polaris/Terrain/Slope24/frame_0158.jpg)
- [스텔라 실제 24도 내리막 보행, 프레임78](../../TestResults/CharacterPipeline/Motion/After/StellaDownhillAfter10/Stella/Terrain/Downhill24/frame_0078.jpg)

**남은 한계**

- 평지 전체 검사에는 약접지 오차가 남는다. 폴라리스 프레임93의 접지값 0.232에서 −16.205mm, 스텔라 프레임240의 G 스킬 진입 시 접지값 0.241에서 −18.318mm였다. 지형 수정이 모든 발 전환을 완벽하게 만든 것은 아니다.
- 경사 레시피가 평지로 돌아온 뒤의 잔차도 표에 포함했다. 폴라리스 −8.537mm는 기존에도 같았던 낮은 접지 가중치 샘플이다. 스텔라 −10.170mm는 접지 곡선이 1로 바뀐 뒤 실제 IK 가중치가 0.469로 올라오는 전환 샘플이므로 단순히 약한 접지 곡선으로 설명하지 않는다.
- 회복 동작 중에는 발이 지면에서 떨어진다. 평지 프레임94에서 발바닥 최저점의 지면 간격이 폴라리스 약 115mm, 스텔라 약 119mm였다. 보통의 스윙·달리기 비행과 회복 동작을 구분해야 하며 모든 프레임 완전 밀착 또는 발 미끄러짐 0을 보장하지 않는다. 후면은 케이프·머리카락 때문에 일부 발이 가려진다.
- Humanoid 리그와 CC0 리타겟은 실제로 검증했다. Adobe Mixamo에서 받은 완전한 입력 클립 세트는 없어 Mixamo 결과까지 검증했다고 표시하지 않는다. 현재 카탈로그도 `UsesMixamo: 0`이다.
- 고정 30fps 녹화는 실시간 30/60fps 달성 증거가 아니다. 폴라리스 경사 검사에서 두 발의 지형 확인·접지 보정 ray가 최대 231회/프레임까지 기록됐다. Field 밀도·다수 캐릭터를 포함한 실제 CPU/GPU 프로파일링과 목표 PC 프레임 검증은 별도로 필요하다.

**게임에서 확인하기**

[Assets/Scenes/Field.unity](../../Assets/Scenes/Field.unity)를 열고 Play한다. 선택 화면 또는 기존 세이브의 주인공으로 진입한 뒤 WASD로 이동, Shift로 달리기, 멈춤과 A/D 급회전, Space 점프, 기본공격, G 원소 스킬 시험, Q 궁극기 연출 시험을 확인한다. 스텔라·폴라리스는 각각의 선택값으로 따로 확인한다. 위 정량 경사·Lifecycle 검사는 Field 지형과 세이브를 바꾸지 않는 별도 opt-in PlayMode 검사이며 일반 Play마다 자동 실행되지 않는다.

전달 자료에는 [폴라리스 Lifecycle JSON](../../TestResults/CharacterPipeline/Motion/After/PolarisTerrainLifecycle01/Polaris/TerrainLifecycle/terrain_lifecycle.json), [스텔라 Lifecycle JSON](../../TestResults/CharacterPipeline/Motion/After/StellaTerrainLifecycle01/Stella/TerrainLifecycle/terrain_lifecycle.json), 선택한 MP4·캡처·분석 요약·출처 해시·NUnit XML을 포함한다. 대용량 전체 프레임과 `motion.json`·`terrain_surface.json` 원본은 스테이징 검사 폴더에 보관하며, 이번 전달본에는 전부 복사하지 않는다. 이 문서는 스테이징의 적용·검증 결과이며 GitHub 게시나 원본 프로젝트 배포 완료를 의미하지 않는다.
