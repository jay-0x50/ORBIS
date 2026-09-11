# M3 아트 자산과 편집 계약

M3의 셰이더, VFX Graph, Timeline은 기획서 03의 연출을 확인하기 위한 프로토타입 자산이다. 피해량·원소 반응·쿨다운 같은 게임 규칙은 이 자산에서 변경하지 않는다. 기획서에 없는 노이즈 빈도, 가장자리 폭, Fresnel 지수 등은 코드 주석에 표시한 시각적 기본값이다.

## Shader Graph 5종

실제 `.shadergraph` 파일은 `Assets/Orbis/M3/Resources/M3/Shaders/`에 있다. 각각 Blackboard 속성, 좌표/정점 입력 노드, 효과 수식을 담은 Custom Function 노드, URP Unlit 출력 블록으로 연결된다. 독립적인 수기 `.shader` 파일로 대체하지 않았다.

| Material Resources 경로 | 용도와 입력 계약 |
| --- | --- |
| `M3/Materials/Dissolve` | 객체 좌표의 노이즈로 출현·소멸 및 잔상을 표현한다. `_Progress=0`은 표시, `1`은 소멸이며, 출현은 반대로 재생한다. |
| `M3/Materials/Outline` | 객체 좌표의 법선 방향으로 확장한 뒷면을 그려 실루엣을 만든다. `_Thickness=0.03`이 기본값이다. |
| `M3/Materials/WeaponTrail` | UV0.y의 폭 가장자리와 정점 색/alpha를 사용한다. TrailRenderer의 정점 RGB를 흰색으로 두면 원소 팔레트를 유지한다. |
| `M3/Materials/SwirlRing` | UV0의 중앙을 기준으로 고리와 회전하는 띠를 만든다. 고리의 실제 확대는 런타임 Transform이 담당한다. |
| `M3/Materials/CrystalShield` | 월드 법선·시선의 Fresnel과 위치 미분으로 메시의 면을 강조한다. 낮은 분할 구체에서 결정 면이 뚜렷해진다. |

모든 소재는 `_PrimaryColor`/`_SecondaryColor`(Color)와 `_Progress`(0~1)를 노출한다. `_PrimaryColor.a`는 전체 불투명도이며, Outline/Trail/Ring/Shield의 `_Progress`는 끝으로 갈수록 alpha를 줄인다. 모든 그래프는 투명 alpha 블렌딩이며 그림자를 만들지 않는다. Outline만 뒷면 전용이고 나머지는 양면이다. 객체 좌표의 두께이므로 Outline의 월드 두께는 메시 Transform의 scale에 영향을 받는다.

기획서 03의 팔레트는 다음과 같다. 기본 소재 미리보기는 Fire이며 런타임의 MaterialPropertyBlock이 실제 원소 색을 적용한다.

| 원소 | Primary | Secondary |
| --- | --- | --- |
| Fire | `#FF5A1F` | `#FFD166` |
| Water | `#1FA2FF` | `#A7E8FF` |
| Wind | `#6CFFB8` | `#E8FFF3` |
| Rock | `#D4A93B` | `#7A5C2E` |
| Lightning | `#B26CFF` | `#F0D9FF` |

## 생성과 직접 편집

Unity Project에서 `.shadergraph`를 더블클릭하면 노드와 Blackboard를 직접 편집할 수 있다. Custom Function 노드의 String Body에서 프로젝트 전용 수식을 수정할 수 있다. 런타임 연결을 유지하려면 위 Reference 이름과 `_Progress`의 의미를 보존한다.

`Orbis/M3/Build Effect Materials` 메뉴 또는 `Orbis.M3.Editor.M3ShaderBuilder.Build()`는 커밋된 그래프를 동기 임포트하고 소재를 생성·갱신한 뒤 각 소재의 모든 shader pass를 컴파일한다. 기존 소재의 셰이더/기본 색/Progress/Thickness도 기본값으로 갱신한다. 그래프 내용은 재작성하지 않는다. 편집 후 검증만 하려면 `Orbis/M3/Validate Effect Shaders` 또는 `M3ShaderBuilder.Validate()`를 사용한다. 컴파일 오류나 필수 속성 누락은 실패로 보고한다.

`Assets/Orbis/M3/Editor/ShaderGenerateGraphs.py`는 최초 그래프를 재현하는 선택적 개발 도구다. 사용자 실행이나 프로젝트 열기에 Python은 필요 없다. **이 스크립트를 재실행하면 같은 이름의 5개 `.shadergraph`가 덮어써져 직접 편집한 노드/수식이 사라진다.** 재생성 전에 변경 내용을 커밋하거나 별도 그래프로 보관한다. 평소 그래프 편집에는 이 스크립트를 실행할 필요가 없다.

## 원본과 출처

5개 그래프의 구성과 Custom Function HLSL 수식은 이 프로젝트용으로 작성했다. 외부 텍스처·유료 효과·외부 셰이더 코드를 가져오지 않았다. 직렬화 구조와 URP 타깃 설정은 설치된 Unity 6000.6.0f1의 Shader Graph/URP 17.6 패키지 소스 및 `com.unity.visualeffectgraph/ShaderGraph/0_VFXGraph Unlit.shadergraph`의 구조를 참고했다. 원본 템플릿의 효과 코드를 복제하지 않았으며, 생성 결과는 URP Unlit 타깃만 사용한다.

이들 Unity 패키지의 `LICENSE.md`에는 Unity Technologies ApS 저작권 및 Unity Companion License가 명시되어 있다. 설치 패키지의 라이선스와 메타데이터는 그대로 유지한다.

Timeline은 `M3TimelineBuilder.Build()`가 Unity Timeline의 SignalTrack/SignalEmitter/SignalReceiver API로 프로젝트 전용 자산을 생성한다. 외부 Timeline을 복사하지 않는다. 기존 `.playable`/`.signal`은 유지하며 누락된 트랙·마커·자산만 추가하고, 검증 시 5단계 시각이 계약과 다르면 실패로 보고한다.

VFX Graph 8종은 Unity 17.6.0 `com.unity.visualeffectgraph/Editor/Templates/Simple_Burst.vfx`의 컨텍스트 골격을 기반으로 한다. 시스템 구성, 단발 방출량, 속도·중력·크기·색상 곡선은 이 프로젝트에서 작성했다. 입자 마스크 PNG 5개도 수학식으로 자체 생성했으며 외부 이미지를 가져오지 않았다. 정확한 출처와 Unity 라이선스 사본은 `Assets/Orbis/M3/Resources/M3/Effects/PROVENANCE.md` 및 `Unity_VFX_LICENSE.txt`에 있다.

`M3VfxBuilder.Build()`/`Validate()`는 `PrimaryColor`/`SecondaryColor`(Vector4), `Scale`(float)의 VFX 계약을 사용한다. 계약을 갖춘 기존 그래프는 보존하며 누락되었거나 아직 템플릿 상태인 그래프만 생성한다. 기존 마스크 PNG도 덮어쓰지 않는다. VFX Graph를 직접 편집할 때도 이 노출 속성을 유지한다. 이 VFX 속성에는 Shader Graph 속성과 달리 앞쪽 밑줄이 없다.
