# STEP 4 지형·이끼 바위 스타일

`WorldGroundBuilder.ApplyStyle(Terrain[])`는 기존 지형의 재질과 TerrainLayer 표시 속성만 바꾼다. 높이맵, 구멍, TerrainCollider 참조, 레이어 순서 및 alphamap에는 쓰지 않는다. STEP 3에서 만든 콘텐츠 받침과 연결길을 유지한다.

## 흰 지면 광택 원인

`World03_After_MeadowHighland.png`에서 멀어지는 지면이 넓게 희어지고 관찰 방향에 따른 반사가 보였다. 기존 7개 레이어의 `smoothness=0`과 달리 `smoothnessSource=DiffuseAlphaChannel`이었다. 불투명 원본 이미지는 alpha=1로 샘플되므로, 설치된 URP의 `TerrainLitPasses.hlsl`은 이 값을 최종 smoothness로 사용했다. `ConstantOnly`와 0을 함께 지정해 이 모순을 수정한다. STEP 2 레이어 생성 함수에도 같은 설정을 넣어 재생성으로 문제가 돌아오지 않게 한다.

## 지형 셰이더 구조

프로젝트의 `WorldTerrainToon.shader`, `WorldTerrainAdd.shader`, `WorldTerrainBase.shader`가 동일한 `WorldTerrainLighting.hlsl`을 사용한다. 기본값은 diffuse 3단계 0.34 / 0.68 / 1.0, 경계 0.18 / 0.64, 직접광 가중치 0.78, 간접광 가중치 0.55다. 이 수치는 문서에 없는 임시 미술 기본값이다. 마른 지면에는 시점에 따라 움직이는 specular/reflection 광택을 넣지 않는다.

첫 STEP 4 실제 렌더에서는 약한 baked GI 때문에 그림자가 거의 검정으로 내려갔다. 직접광이 줄어드는 부분에 `(0.68, 0.73, 0.78) × 0.28`의 차가운 sky fill을 점진적으로 적용한다. 온전히 밝은 면에서는 0으로 줄어들며, 기존 간접광이 충분하면 더 큰 기존 값을 유지한다. Terrain과 MossRock이 같은 기준을 사용한다. 초지·이끼의 색 배수도 빨강을 약간 낮추고 파랑을 올려 원본의 황올리브 기운을 줄인다.

정점 처리, 지형 인스턴싱, splat/높이 블렌딩, terrain holes, 원거리 basemap 및 추가 레이어의 가중치/안개 처리는 설치된 URP 패키지의 Terrain include를 사용한다. 지형 프래그먼트에서 호출하는 조명 함수만 프로젝트 함수로 연결한다. 추가 패스에는 가산 블렌딩을 유지하며, 베이스맵도 동일한 툰 조명을 사용한다. 그림자·depth·normal·에디터 선택·메타 패스 및 베이스맵 생성은 공식 패스를 참조한다. 커스텀 조명은 ForwardOnly로 실행하며 별도의 PBR GBuffer를 만들지 않는다.

외부 셰이더 소스를 프로젝트에 복사하지 않는다. `Packages/com.unity.render-pipelines.universal/...` 및 Core 패키지를 include/UsePass로 참조하므로 Unity 패키지의 라이선스와 버전 관리가 그대로 적용된다. 패키지 업데이트 시 Terrain 입력 및 조명 함수 호출 시그니처 호환을 다시 검증해야 한다.

## 색과 연결

기존 알베도에 차분한 풀/이끼 녹색, 따뜻한 흙, 차가운 아이보리/슬레이트 계열 배수를 적용한다. 기존 연속 바이옴·높이·경사 마스크를 다시 칠하지 않는다. MossRock도 동일한 3단 diffuse와 직접광 가중치를 사용하며 공유 재질을 통해 절벽·바위·건축 토대에 적용된다.

`_OrbisBiomeAmbient`는 WorldAtmosphere가 미리 약하게 혼합해 공급하는 Linear 색 배수다. 셰이더에서 중복 혼합하지 않고 한 번 곱한다. 전역값이 전부 0인 초기 상태에는 중립 흰색으로 처리한다.

## 실제 실행에서 확인할 항목

- 같은 카메라의 STEP 3/4 이미지에서 흰 광택이 사라지고 흙길/풀/바위 경계가 유지되는지 확인.
- 7번째 Moss와 6번째 Trail이 추가 패스에서 사라지거나 과도하게 밝아지지 않는지 확인.
- 가까운 splat 지형과 원거리 basemap 사이에 조명 색이 갑자기 달라지지 않는지 확인.
- 지형 타일 접합, 그림자, 구멍, 발밑 TerrainCollider, 인스턴싱을 포함한 실제 Unity 렌더 확인.

이 문서는 구현과 검증 기준이다. 최종 렌더 성공은 Unity 실행 결과에서 별도로 확인한다.
