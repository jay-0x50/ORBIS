# 선택 파일 전달 계획 도구 — 실제 복사 없음

`prepare_delivery.py`는 최종 Unity dependency JSON과 명시적으로 작성한 supplemental JSON path 배열을 읽어 정확한 파일 단위 계획을 만든다. `--apply`, copy, delete, Unity 실행 기능이 없다. 출력을 제외한 파일은 읽기만 한다. 입력·타깃의 모든 내용을 덮어쓸 권한을 주는 도구가 아니다.

## 입력

1. `--manifest`: `{ "files": [{ "path": "Assets/…", "sha256": "64 hex", "targetSha256": "64 hex 또는 null" }] }`. `HeroFinalBinding.ExportDependencyManifest`가 만드는 형식이다. Root가 최종 runtime Field export/closure를 마친 후 생성한 파일을 사용한다. path는 프로젝트 상대 파일 경로이며 디렉터리나 glob이 아니다.
2. `--supplemental`: JSON **문자열 배열**. 예: `["Docs/CharacterPipeline/Final_Asset_Mapping_Draft.md"]`. 추가할 정확한 파일만 나열한다. `[]`도 유효하다. dependency에 이미 있는 파일을 다시 적어도 dependency의 기대 source hash를 약화시키지 않는다.
3. 기본 baseline: `Tools/CharacterPipeline/ProjectBaseline.json`, 정확히 **1,793개** path→SHA256. SHA256은 `3a3190b61b936c74b8c6e1c53a4e6ffbebf924527b012b52a017eefd42868b7d`로 고정된다. CLI에는 이를 우회하거나 충돌을 무시하는 옵션이 없다.

supplemental의 hash는 명시 요청한 현재 파일에서 읽는다. 기존 파일 덮어쓰기를 승인하는 별도 근거로 사용하지 않는다. `Credits.md`는 이 1,793개 baseline에 **없으므로**, 기존 타깃 Credits와 내용이 다르면 정상적으로 `existing_target_without_baseline` 충돌이 된다. 나중의 승인된 Credits 문단 패치는 root가 별도 정확한 content guard로 다뤄야 하며, planner에 예외를 넣지 않는다.

## 판정 순서

| 상태 | 결과 |
|---|---|
| `Assets/blend/**` 또는 `Assets/blend.meta` 명시 | 무조건 conflict; 원본 트리는 계획에 못 들어감 |
| 상대 경로 이탈·drive/ADS·glob·Windows 모호 경로·symlink/junction·부모 경로가 파일 | 거절 |
| source 없음 또는 dependency source SHA와 다름 | conflict |
| 현재 source와 target bytes 동일 | skip; 복사/덮어쓰기 없음 |
| source와 다르면서 manifest의 target SHA도 현재 target과 다름 | conflict |
| 기준에 있던 target이 삭제됨 | conflict; 임의 복원 안 함 |
| 기준 target 내용이 변경됨 | conflict |
| baseline에 없는 기존 target 내용이 source와 다름 | **manifest target SHA가 맞아도 conflict** |
| target 부재 + baseline에도 없음 | 정확한 신규 파일 계획 |
| target이 baseline 그대로 + source만 변경 | 정확한 변경 파일 계획 |

1,793개 전체 target baseline을 새로 해시해 변경/삭제를 `baselineTargetAudit`에 남긴다. 선택 범위 밖의 타깃 수정은 보고하며 수정하거나 baseline으로 새로 채택하지 않는다. 이전 전달 결과처럼 target과 source가 이미 같으면 skip이므로 재실행이 불필요한 덮어쓰기를 만들지 않는다. 하나라도 conflict가 있으면 전체 report는 `blocked_conflicts`, 종료 코드는 2다. 안전한 일부 파일이 `plannedFiles`에 있더라도 그 보고서를 부분 적용하면 안 된다.

## 메타/GUID 검사의 정확한 범위

- 기본 `--meta-check selected`: 선택된 `Assets/` 파일, 해당 파일 meta, 모든 Assets 하위 조상 폴더 meta를 검사한다. 필요한 meta가 supplemental/dependency에 명시되지 않았고 target에도 동일 bytes로 존재하지 않으면 conflict다. source meta의 GUID 형식과 **이 선택 meta 집합 내부의** GUID 중복을 검사한다. Assets 밖 Docs/Tools/Packages에는 Unity meta를 요구하지 않는다.
- `--meta-check target-assets`: 위 검사에 더해 실제 타깃 `Assets/**/*.meta`를 읽어, 선택 GUID가 다른 미교체 target 경로와 충돌하는지 확인한다. 이 전체 scan은 파일을 전달 목록에 추가하지 않는다. 외부 Packages GUID는 포함하지 않는다.
- `--meta-check none`: 메타/GUID 검사 생략을 report에 명시한다. 이 상태를 GUID 검수 통과로 표현하지 않는다.

중복 검사만으로 serialized reference, Scene 게임플레이 의미, 스크립트 컴파일, Addressables catalog/build 일치를 증명하지는 않는다. Unity validation과 실제 dependency closure는 root의 별도 검수다.

## 실행 형식

아래 경로의 `DependencyFinal01.json`, `supplemental-final.json`은 **최종 입력이 준비된 후 지정할 이름 예시**다. 이 문서 작성 시 dependency 최종 export는 아직 준비 중이므로 실제 타깃 전체 계획은 실행하지 않았다.

```powershell
C:\Python314\python.exe Tools/CharacterPipeline/prepare_delivery.py --manifest TestResults/CharacterPipeline/HeroFinalBinding/FinalHeroes01/DependencyFinal01.json --supplemental Tools/CharacterPipeline/supplemental-final.json --meta-check target-assets --output Tools/CharacterPipeline/DeliveryPlans/Plan01.json
```

staging project 루트에서 실행한다. `--staging` 기본은 스크립트가 있는 프로젝트, `--target` 기본은 `D:/Project/ORBIS`다. report는 staging `Tools/CharacterPipeline/` 아래의 **새 `.json` 경로**만 허용하며 기존 보고서를 덮어쓰지 않는다. 입력이 잘못되거나 pinned baseline이 다르면 보고서 생성 전 중단한다.

report에는 입력 파일 SHA, 고정 baseline SHA/count, 현재 source/target/baseline/manifest target SHA, 실제 파일 byte/count, 정확한 `plannedFiles`, same-bytes skip, 충돌, 메타 witness, 계획 항목의 canonical SHA256이 들어 있다. `ready_for_copy_review`도 복사 승인은 아니다. 이후 copier를 구현할 때는 conflict 0과 모든 source/target/meta witness를 직전 재검사하고, 경로/원본 보호와 baseline 조건을 다시 적용해야 한다.

## 검증

`C:\Python314\python.exe Tools/CharacterPipeline/test_prepare_delivery.py`의 합성 프로젝트 테스트 **13/13 통과**. 새 파일/기준 변경/동일 bytes, 새로 기록한 target hash만으로 기존 미기준 파일 덮어쓰기 금지, 사용자 변경/삭제, stale manifest, raw blend 보호, 경로 이탈, supplemental 중복, 메타 누락/미명시, GUID 중복, target 부모가 파일인 경우를 확인했다. 테스트는 Tools 아래 임시 synthetic 프로젝트만 사용하며 실제 Assets/target에 적용하지 않는다.

초기 Python 3.14 TemporaryDirectory의 owner-only Windows ACL 때문에 fixture 생성이 실패했지만, 이후 일반 mkdir의 상속 ACL로 바꿔 13/13을 실행했다. 초기 빈 테스트 디렉터리 13개는 절대 경로와 비어 있음을 확인하고 정리했다. `--help`도 정상 종료했다. 아직 최종 dependency 입력을 사용한 production plan은 생성하지 않았다.
