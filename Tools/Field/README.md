# Field delivery tools

이 도구는 스테이징의 검토된 Field 변경만 `D:\Project\ORBIS`에 복사합니다. 삭제·미러링·Git 작업을 하지 않습니다.

1. Unity 실행·테스트·씬 저장을 모두 끝냅니다. `GenerateDeliveryManifest.py`를 실행하며 전달할 최종 `TestResults/WorldDev/Field_*.png`, `.json`, `.xml`을 `--evidence` 인수로 하나씩 명시합니다. 로그·타깃 기준 해시는 제외합니다.
2. `Field_DeliveryReport.json`, `Field_DeliveryManifest.json`, `Docs/Field_FILES.md`를 검토합니다. 예상하지 못한 기존 파일 변경은 자동 제외하지 않고 보고 후 중단합니다. 검토하여 이번 작업에 속한다고 확인된 기존 파일에만 `--allow-existing`을 명시합니다.
3. 생성기가 출력한 매니페스트 SHA-256을 `ApplyDelivery.ps1 -ExpectedManifestSha256 <해시> -Mode Validate`에 전달합니다. 기본 모드는 읽기 전용 검증입니다.
4. 승인된 대상 경로에 대한 쓰기 권한으로 같은 SHA-256과 `-Mode Apply`를 사용합니다. 모든 파일 사전 검사 후 기존 파일부터 `WorldArtBackups/BeforeFieldAuthoring/yyyyMMdd-HHmmss`에 백업하고 해시를 검증합니다. 그 뒤 실제 변경을 복사하고 모든 최종 해시를 확인합니다.
5. `-Mode Verify`는 복사 완료된 파일과 매니페스트 해시를 다시 읽기 전용으로 검사합니다.

기존 대상 파일은 작업 시작 기준 해시와 일치해야 합니다. 새 경로는 없거나 전달할 내용과 완전히 같아야 합니다. 대상이 달라졌거나 복사가 중단되면 무조건 다시 덮어쓰지 말고 보고된 경로와 원본 백업을 먼저 검토합니다. Unity가 임포트 중 파일을 수정할 수 있으므로 복사 중 씬 저장·빌드·다른 편집 작업을 피합니다.

`Assets/Img`, `Assets/blend`, `Assets/ImportedAssets`, `.blend` 원본은 명시적으로 보호합니다. `Library`, 임시 파일, 로그, `.git`, 작업 중인 `_FieldWorking`·`_FieldMigration` 씬은 포함하지 않습니다. 두 루트와 각 파일의 절대 경로를 검사하며 링크·정션 경로도 거부합니다.

도구는 새로운 최종 검수 증거를 만들거나 테스트 성공을 판정하지 않습니다. 전달할 증거가 실제 최종 실행 결과인지 먼저 확인해야 합니다.
