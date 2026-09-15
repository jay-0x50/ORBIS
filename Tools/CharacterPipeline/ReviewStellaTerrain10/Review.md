# Stella Terrain After10 독립 읽기 검수

2026-09-15. 기존 Unity 캡처 JSON/JPEG를 읽고 검사했으며 Unity/GPU 작업, Assets·원본·이미지 수정은 하지 않았다. 이 검사는 해당 3개 지형 레시피의 회귀 확인이며 모든 Field 지형/전체 프레임 성능 승인으로 확대하지 않는다.

**판단:** 평지의 실제 관절·발바닥 좌표 회귀는 없고, 선택한 worst/crest/cross/stop 이미지에서 새 큰 메시 찢김·신발 붕괴·무릎 반전을 식별하지 못했다. After10의 경사 보정 결과를 이 제한된 범위에서 유지할 수 있다. 원래 평지 경로에 남은 접지 오차와 움직이는 발의 회복 구간은 별도 한계로 기록한다.

## 검사 대상과 고정 조건

- 신규: `TestResults/CharacterPipeline/Motion/After/Stella12TerrainAfter10/Stella/`
- 평지 기준: `TestResults/CharacterPipeline/Motion/After/Stella12After08/Stella/`
- 활성 `Assets/Orbis/M0/Runtime/Animation/HumanFootIK.cs` SHA256: `53849a2da03bf920b27d24495802c5f4f3bd225402c0927e88c5ff1a03febbae`
- 실제 Unity 6000.6.0f1 / URP / GTX 1650 SUPER / 960×720 JPEG / 30fps captureDeltaTime. 평지300 + Actions300 + Slope12/24/CrossSlopeTurns 각300, 합계1500 파일과 프레임이 존재한다.
- Source provenance 비교에서 모델 FBX, Avatar, 모션·Profile·그립 자료의 해시는 동일하다. 달라진 고유 파일은 HumanFootIK, 진단용 CharacterMotionSkinProbe, Field.unity다. 지형별 몸체 소스도 `Stella_ReducedCandidate`, 임포트 정점 284,450개로 같다.
- 프레임 번호가 동일한 평지 비교의 카메라·바닥·배율은 같다. 경사는 카메라 y offset이 3m라서 평지의 y=1.2m 화면과 픽셀 단위 Before/After 비교하지 않았다.

## 평지 결과

300프레임 전체에서 root 위치/회전, 관절 root-local 위치, 캡처한 실제 skin-sole 정점 좌표의 최대 차이는 **0**이다. gameplay state, ground flag, 공격 step/clock/parameter 및 timeScale도 동일하다. JPEG는 298/300개가 byte-identical이며 46·147번 두 JPEG만 바이트 차이가 있다. 이 두 프레임도 위 형상 측정값은 같다.

정면 run120 및 방향 전환240의 Before/After를 실제 이미지로 각각 확인했다. 얼굴·손잡이·치마와 양쪽 무릎/부츠의 기존 형태가 같다. 원본 캡처의 평지 최소 sole Y는 **−18.318mm / frame240 Left**이고 After10에서도 정확히 같다. 이 값을 ‘관통 없음’으로 바꾸어 보고하지 않는다.

## 지형별 실제 skin 접지와 제약

| 실제 레시피 | 전체 최소 거리 | 경사 보정 foot-frame의 최소 거리 | 전체 worst | 최종 발목 수직 보정 범위 | constraint infeasible |
|---|---:|---:|---|---:|---:|
| Slope12 | −10.178mm | −0.0821mm | frame232 Right | −123.779 ~ +67.680mm | 0/600 |
| Slope24 | −10.170mm | −0.2021mm | frame232 Right | −192.726 ~ +154.488mm | 0/600 |
| CrossSlopeTurns | −0.1603mm | −0.1603mm | frame139 Right | −187.332 ~ +162.636mm | 0/600 |

각 case 300프레임 모두 finite이고 표면 샘플 누락 0이다. 전체 최소는 JSON summary를 인용하는 데 그치지 않고 각 실제 정점의 signedDistances 배열에서 다시 구했다. Slope12/24의 worst232는 phase 이름이 Downhill이지만 **삼각형4, 법선(0,1,0)의 평평한 상단 면**이다. 이때 terrainAdapted=false, handoff=false, Right contact=1, 실제 IK weight≈0.468877로 원래 평지 보정 경로에 있다. 두 경사에서 같은 값이 나오는 이유를 단순히 경사 각도 효과라고 해석하면 안 된다.

경사진 표면 위에서 발생한 최저값 역시 각각 −0.0821/−0.2021/−0.1603mm다. `terrainUnresolvedClearance` 최대값은 0.00148/0.00339/0.00364mm였다. 이는 solver가 예측한 남은 clearance이며 실제 skinned mesh 오차와 동일한 지표가 아니다. 실제 표면 거리와 함께 보고한다.

기존 설정인 발목 source-Y ±0.32m slab, 최대 pelvis 0.14m를 넘는 기록은 없다. max terrain pelvis remainder는 각각 117.024/138.529/139.690mm다. 최종 reach projection이 있는 경우도 기록했고 실패 constraint는 없었다. 내부 최종 sphere는 측정 source leg length×0.9995를 사용한다. 이 수학 제약의 성공을 모든 Mecanim 근육/스킨 변형의 완전 보증이라고 설명하지 않는다.

### 피부 발바닥 측정과 캐시

- 각 600 foot-frame에서 geometryReady=true, geometryAttempts=1, geometryError 빈 문자열. Profile/Avatar 실패 재시도 루프는 이번 실제 자료에 없다.
- 실제 캡처의 측정 발바닥 정점은 Left26/Right27. 이 수는 convex hull 정점 개수와 다르다.
- 활성 코드 `CollectSole`는 `mesh.bindposes[footIndex] × sharedMesh.vertices`로 foot rest-local 점을 만들고, foot/toe 합산 가중치>0.25인 정점을 원본 측정 sole plane의 월드 4mm 띠로 제한한다. 첫 Bind 시 재생 포즈를 BakeMesh해 profile로 잘못 굳히는 방식이 아니다.
- Root/중심 ray가 평평해도 실제 측정 sole hull의 앞/뒤 끝을 검사해 toe-only 경사 진입을 찾는다. 경사 support는 pelvis를 공유하는 두 발에 적용한다. 수평으로 돌아온 뒤에는 남은 실제 pelvis 기여분 때문에 예측 발바닥이 뚫리는 경우에만 constrained handoff를 적용한다.
- 각 case의 최대 gate ray 수/프레임은 14/14/0, clearance ray 수/프레임은 31/36/36이다. gate 평균은 7.71/7.74/0이다. 코드는 finite point cache를 사용하지만 실제 단가/FPS는 이 이미지 캡처로 측정하지 않았다.

### 관통과 떠 있는 발을 구분

Slope12/24에서 −5mm 아래인 foot-frame은 각각 2개이고, contact≥0.5인 것은 각각 1개다. Cross에는 −1mm 아래 샘플도 없다. 전체 최대 양의 거리는 187.111/187.103/141.682mm이지만 swing/recovery 발도 포함하므로 이를 그대로 착지 발의 부유 높이라고 보고하지 않는다.

반대로 contact 곡선만 높다고 실제 접촉이 확정된 것도 아니다. 예를 들어 Slope24 frame71 Left는 raw contact=.809지만 Reach 해제로 effectiveContact=0이고 recovery 중이다. Cross frame48 Right도 같은 경우다. Slope24 frame262 Right는 contact≈.585인 전환 구간에서 실제 최저 샘플이 +74.23mm이며, frame263~264는 Reach recovery로 넘어간다. 이 전환부의 발 상승은 영상 검수에서 유지해야 할 한계다. 완전한 모든 프레임 밀착을 주장하지 않는다.

## 실제 이미지에서 본 변형

검수한 이미지는 생성/보정/크롭하지 않은 원본 JPEG다. 아래 경로는 각각 위 신규/기준 캡처 폴더 안에 있다.

| 위치 / 프레임 | 직접 확인한 사항 |
|---|---|
| 기준·신규 `frame_0120.jpg` | 동일 run 자세, 검 그립/치마/부츠와 무릎 실루엣 동일 |
| 기준·신규 `frame_0240.jpg` | 같은 방향전환 자세와 기존 평지 worst; 새 형상 차이 없음 |
| 신규 `Terrain/Slope24/frame_0062.jpg` | 앞발 경사 진입, 발목 반전·새 찢김 식별 안 됨 |
| 신규 `Terrain/Slope12/frame_0134.jpg` | crest run의 다리 굽힘이 유지되고 치마 연결부가 큰 구멍으로 벌어지지 않음 |
| 신규 `Terrain/Slope24/frame_0134.jpg` | 가장 굽은 Left 무릎 63.53°. 12°의 78.94°보다 많이 굽지만 무릎 방향 반전/관절 단절은 보이지 않음 |
| 신규 `Terrain/Slope24/frame_0157.jpg`, `0159.jpg` | crest 통과의 두 자세에서 양 부츠/치마 파츠의 연결 유지; 새 늘어진 삼각형 식별 안 됨 |
| 신규 `Terrain/Slope24/frame_0232.jpg` | 실제 거리 worst. 뒤쪽 카메라여서 앞면 무릎은 가리지만 부츠/후면 천에 새 큰 찢김 없음 |
| 신규 `Terrain/Slope24/frame_0263.jpg`, `0264.jpg` | descent Reach recovery의 인접 프레임. 신발 상승과 뒷단 천 중첩은 있으나 발목 접힘 붕괴/천 폭발은 보이지 않음 |
| 신규 `Terrain/CrossSlopeTurns/frame_0139.jpg` | 실제 surface worst. 네거티브 거리 규모는 −0.16mm, 부츠 옆면이 반전되지는 않음 |
| 신규 `Terrain/CrossSlopeTurns/frame_0249.jpg` | 높은 회복 발이 있는 run 자세. 무릎/갑옷 파츠의 형상 연결 유지 |
| 신규 `Terrain/CrossSlopeTurns/frame_0272.jpg` | FinalStop 초기 Right 무릎 62.36°로 깊게 굽는다. 확정된 접지발은 아니며 stop가 끝날 때까지 이 자세에 고정되는지 다음 이미지로 확인 |
| 신규 `Terrain/CrossSlopeTurns/frame_0299.jpg` | 마지막 stop에서 깊은 쪼그림이 풀리고 두 신발이 모이는 자세. knee inversion/신발 붕괴 식별 안 됨 |

900개 지형 프레임을 모두 개별 확대해서 본 것은 아니다. 이 선택은 실제 최저 거리, 최대 무릎 굽힘, crest/진입/회복/최종 stop을 기준으로 했다. 960×720 전신 이미지에서 새 큰 파손이 없다는 판단이며 피부 미세주름·atlas 경계·1m 클로즈업 품질 승인으로 확대하지 않는다.

## Actions 비교의 별도 한계

추가로 Actions300도 수치 비교했지만, 평지 이동300처럼 동일하다고 보고할 수 없다. 처음 Q 이후 frame182부터 일부 프레임의 Burst normalizedTime이 다르고 Hurt/BurstReplay 이후 영상 위상이 달라진다. 루트 이동·공격 step/clock은 동일하며 `action_checks.json`은 3콤보, Hurt의 Burst 취소, 재시작, 마지막 Finished 및 warp/release reset을 기록한다.

기존 `M3UltimateDirector.cs:71`은 `DirectorUpdateMode.UnscaledGameTime`, `M3Presentation.cs:245`는 director.time/duration을 사용한다. 따라서 고정 captureDeltaTime만으로 Q Timeline의 모든 프레임이 결정론적으로 동일하다고 볼 수 없다. 두 provenance에서 M3/전투/Profile은 같고 경사 IK는 action 시 비활성 경로다. 이 차이를 After10의 새 지형 변형 결함으로 판정할 근거는 없으며, 전체 Actions의 픽셀 동등성은 승인하지 않는다. 작업 범위를 벗어나 Timeline 시간을 수정하지 않았다.

## 산출물

- `analyze.py`, `analysis.json`: 두 flat/action300 비교 및 지형별 실제 거리·constraint/cache/recovery/무릎 집계
- `actual_bone_lengths.json`: 캡처된 hip→knee→ankle 거리 범위. 스킨/근육 solver의 최종 변형 관찰 보조 자료이며 현재 길이의 triangle inequality를 원본 뼈 불변 증명으로 사용하지 않는다.
- 이 문서. 입력 캡처, 활성 소스, 모델·모션·Profile은 읽기 전용으로 유지했다.
