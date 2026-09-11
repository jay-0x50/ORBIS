# 무료 애셋 라이선스 증빙

기획서 04의 원본 다운로드 링크·라이선스 화면 보관 항목을 위해 작성했습니다. 확인일은 2026-09-10입니다. 7개 공식 페이지에서 HTML과 실제 Chrome 화면을 저장했고, 각 화면에 해당 팩 제목과 CC0 표기가 보이는지 확인했습니다. 캡처는 1440×2200 PNG 원본이며 합성·재작성하지 않았습니다.

## 보관 목록

| 폴더 / 공식 출처 | 도입 버전 | 선별 원본 파일 수 | 바이트 |
| --- | --- | ---: | ---: |
| [Kenney_NatureKit](Kenney_NatureKit/) · [공식 페이지](https://kenney.nl/assets/nature-kit) | 2.1 | 42 | 1,278,571 |
| [Kenney_CastleKit](Kenney_CastleKit/) · [공식 페이지](https://kenney.nl/assets/castle-kit) | 2.0 | 29 | 615,113 |
| [Kenney_ModularDungeonKit](Kenney_ModularDungeonKit/) · [공식 페이지](https://kenney.nl/assets/modular-dungeon-kit) | 2.1 | 16 | 1,331,783 |
| [Kenney_MiniDungeon](Kenney_MiniDungeon/) · [공식 페이지](https://kenney.nl/assets/mini-dungeon) | 2.0 | 17 | 1,177,758 |
| [Kenney_UIPack](Kenney_UIPack/) · [공식 페이지](https://kenney.nl/assets/ui-pack) | 2.0 | 41 | 29,439 |
| [KayKit_Adventurers](KayKit_Adventurers/) · [공식 페이지](https://kaylousberg.itch.io/kaykit-adventurers) | 2.0 FREE | 13 | 2,634,012 |
| [KayKit_CharacterAnimations](KayKit_CharacterAnimations/) · [공식 페이지](https://kaylousberg.itch.io/kaykit-character-animations) | 1.1 | 9 | 21,922,124 |
| **합계** | | **167** | **28,988,800** |

파일 수에는 각 팩의 `License.txt` 1개가 포함됩니다. 이 목록은 `Assets/ImportedAssets/`에 선별 반입한 FBX/PNG/라이선스 원본 전체이며, 그중 실제 카탈로그가 참조하는 파일은 [환경·UI 매핑](../Art_Environment_Mapping.md)과 캐릭터 카탈로그로 좁혀집니다. 모든 반입 애니메이션이 현재 FSM에서 재생된다는 뜻은 아닙니다. Unity가 만든 `.meta`와 파생 프리팹·머티리얼은 원본 해시 목록에 포함하지 않습니다.

Nature Kit 공식 페이지는 업데이트 1.0을 표시하지만 다운로드 내부 `License.txt`는 2.1입니다. Modular Dungeon Kit의 ZIP 파일명은 1.0, 내부 라이선스와 공식 페이지는 2.1입니다. 내부 라이선스 기준으로 위 버전을 기록했습니다.

## 각 폴더의 증빙

- `source.json`: 공식 URL, 원본 다운로드 URL/엔드포인트, ZIP 이름·크기·SHA-256, 획득 시각. 원본 ZIP은 개발용 다운로드 폴더에만 두고 이 문서 폴더에는 복제하지 않았습니다.
- `License.txt`: 배포 압축에 포함된 라이선스를 원문 바이트 그대로 보존한 사본입니다.
- `official-page.html`: 확인일에 공식 URL에서 내려받은 HTML입니다. 외부 이미지·CSS까지 오프라인 미러링하지는 않았으므로 화면 증빙은 PNG를 확인합니다.
- `official-page.png`: 별도 임시 Chrome 프로필로 공식 페이지를 직접 연 실제 브라우저 화면입니다.
- `capture.json`: URL, 캡처 UTC 시각, Chrome 종료 코드, HTML/PNG 저장 결과, PNG SHA-256입니다. `capture.stdout.txt`/`capture.stderr.txt`에는 실행 로그를 보존했습니다.
- `selected-files.sha256.csv`(Kenney) 또는 `.json`(KayKit): 압축 내부 경로, 프로젝트 반입 경로, 크기, SHA-256입니다.
- `verification.json`: 현재 반입 파일과 선별 목록의 해시·바이트 수를 비교한 결과, 캡처 해시 대조 결과, 라이선스와 HTML의 SHA-256입니다.

[전체 캡처 결과](capture-manifest.json)는 7/7 성공이고 [전체 원본 대조 결과](verification-manifest.json)는 167/167 일치입니다. 기존 사용자 브라우저의 프로필·프로세스를 사용하지 않았습니다. 캡처 시각은 UTC로 저장되며 한국 시각은 UTC+9입니다.

CC0의 공식 법률 문서도 [원문 HTML](CC0-1.0-legalcode.en.html)로 보존했습니다. 원본 주소는 [Creative Commons CC0 1.0 Universal Legal Code](https://creativecommons.org/publicdomain/zero/1.0/legalcode.en)입니다. 각 팩 저자의 `License.txt`와 공식 팩 페이지를 함께 읽어 해당 애셋의 적용 범위를 확인합니다.

## 다시 확인하는 방법

PowerShell을 프로젝트 루트에서 열어 Kenney 목록은 다음처럼 대조합니다. 해시 값이 다르면 원본 파일 변경 여부와 다운로드 출처를 확인하고 새 증빙을 남깁니다.

```powershell
Import-Csv 'Docs/AssetLicenses/Kenney_NatureKit/selected-files.sha256.csv' | ForEach-Object {
    [pscustomobject]@{
        Path = $_.Imported
        Matches = (Get-FileHash -LiteralPath $_.Imported -Algorithm SHA256).Hash -eq $_.SHA256
    }
}
```

KayKit JSON에서는 `destination`과 `sha256` 필드를 같은 방식으로 사용합니다. 웹페이지 자체는 확인일 이후 바뀔 수 있으므로 기존 증빙을 소급 수정하지 말고 갱신 시각을 구분해 보관합니다.

Mixamo, 외부 SFX/BGM, 외부 오픈소스 툰 셰이더를 이 7팩의 일부로 표시하지 않습니다. 현재 Mixamo 상태와 프로젝트/Unity VFX 출처는 [Credits](../../Credits.md)를 참고합니다.
