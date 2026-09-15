# Final character / saved Field validation commands

Prepared from the current source on 2026-09-15. This document did not run Unity or edit Assets. It refines `PendingBossAnimation/FinalRegressionPlan.md`; commands are for the staging project, one Unity/GPU process at a time. Do not invoke old milestone/world generators or `FieldExportVerification.Run`: the latter deliberately moves/clones source objects for an exporter roundtrip test and is unnecessary when only the existing exporter is being reused.

## Evidence to reuse, and the remaining sequence

- `SavedField03.xml`: actual saved Field passed 1 / failed 0 / skipped 0. `PendingFieldFinalTest/SavedField03_Findings.md` records CPU skin agreement, retained five encounters, the single actual Fire attack, ground coverage and its limits. Reuse if Field/core positions/profiles/presenters have not changed since its recorded hash. A later approved Core presentation move requires a fresh run.
- `BossPoseClockTests01.xml`: 5 passed. Five species' `WaterRuntime01`, `FireRuntime01`, `WindBossRuntime01`, `LightningBossRuntime01`, `RockBossRuntime01` each passed 1. They use the actual final Generic prefab/profile in an isolated encounter. Do not recapture unchanged species. Re-run the affected species only if its prefab, clips, profile or shared Presenter changes.
- Latest hero terrain/motion/grip runs are owned by the motion integration queue. Keep the exact candidate/profile/runtime source hash with each evidence record. Do not relabel older Polaris-only evidence as Stella validation, or an earlier IK version as the final terrain result.
- Remaining normal sequence after freezing the candidate: export final Field → focused EditMode regression → exported island and real streaming PlayMode checks → product build-policy restoration check → fresh occlusion bake when invalidated → packed Release build → appropriate real player measurement. Do not run the whole legacy test suite merely because it exists.

## PowerShell setup and serial launcher

Run from the staging project. Every evidence name below is intentionally fresh; if it exists, select a new name rather than overwriting evidence. The function uses hidden Unity windows and waits only for its own process. An agent should poll the returned process in its orchestration instead of blocking user updates for a long bake/build.

```powershell
$orbisStage = 'C:/Users/Mirim/.codex/visualizations/2026/09/10/01a08932-ad4d-7dd1-99a5-ab8a6115569e/orbis-m0'
$orbisUnity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
Set-Location -LiteralPath $orbisStage
function Invoke-OrbisFinal {
    param([string]$Name, [string[]]$UnityArgs, [int]$ExpectedPassed = 0)
    if (Get-Process Unity,blender -ErrorAction SilentlyContinue) { throw 'Coordinate the existing Unity/Blender process first.' }
    $orbisLog = Join-Path $orbisStage ('TestResults/CharacterPipeline/' + $Name + '.log')
    $orbisXmlPath = Join-Path $orbisStage ('TestResults/CharacterPipeline/' + $Name + '.xml')
    if ((Test-Path -LiteralPath $orbisLog) -or (Test-Path -LiteralPath $orbisXmlPath)) { throw 'Preserve prior evidence; choose a fresh Name.' }
    $orbisArgs = @('-batchmode', '-projectPath', $orbisStage, '-logFile', $orbisLog) + $UnityArgs
    if ($ExpectedPassed -gt 0) { $orbisArgs += @('-testResults', $orbisXmlPath) }
    $orbisQuoted = foreach ($orbisArg in $orbisArgs) {
        if ($orbisArg.Contains('"')) { throw 'Unexpected quote in the fixed argument list.' }
        '"' + $orbisArg + '"'
    }
    $orbisProcess = Start-Process -FilePath $orbisUnity -ArgumentList ($orbisQuoted -join ' ') -WorkingDirectory $orbisStage -WindowStyle Hidden -PassThru
    Write-Output ($Name + ' PID ' + $orbisProcess.Id)
    $orbisProcess.WaitForExit()
    if ($orbisProcess.ExitCode -ne 0) { throw ($Name + ' exit ' + $orbisProcess.ExitCode) }
    if ($ExpectedPassed -gt 0) {
        [xml]$orbisXml = Get-Content -LiteralPath $orbisXmlPath -Raw
        $orbisRun = $orbisXml.'test-run'
        if ([int]$orbisRun.failed -ne 0 -or [int]$orbisRun.passed -ne $ExpectedPassed -or [int]$orbisRun.skipped -ne 0) {
            throw ($Name + ' has unexpected passed/failed/skipped counts; inspect XML.')
        }
    }
}
```

Do not add `-quit` to `-runTests`; Test Runner owns completion. Do not use `-nographics` for visual/actual render checks. The installed test framework's `CommandLineOption.SplitStringToArray` explicitly accepts semicolon-separated `-testFilter` names; the whole string stays one quoted argument.

## 1. Export the approved saved source

Finish source/candidate edits first. Opening Field and exporting must happen **in the same Unity process**. Actual `CharacterFieldExport01` failed when Open and Export were separate processes: the second batch process started with an untitled scene, and Unity rejected additive scene creation. The original exporter rolled back; source/state hashes were unchanged. The root's thin `CharacterPipelineBatch` helper calls the existing Open and Export sequentially without changing the exporter. `EnsureExported(bool force=false)` itself is not a zero-argument CLR entry point.

```powershell
$orbisFieldBefore = (Get-FileHash -LiteralPath 'Assets/Scenes/Field.unity' -Algorithm SHA256).Hash
Invoke-OrbisFinal -Name 'FinalFieldExport02' -UnityArgs @('-executeMethod', 'Orbis.Game.Editor.CharacterPipelineBatch.OpenAndExportField', '-quit')
if ((Get-FileHash -LiteralPath 'Assets/Scenes/Field.unity' -Algorithm SHA256).Hash -ne $orbisFieldBefore) { throw 'Export changed the approved source Field.' }
$orbisExport = Get-Content -LiteralPath 'Assets/Orbis/Game/World/Authoring/FieldExportState.json' -Raw | ConvertFrom-Json
if ($orbisExport.outputs.Count -ne 7 -or [string]::IsNullOrWhiteSpace($orbisExport.sourceDependencyHash)) { throw 'Incomplete export state.' }
foreach ($orbisDigest in $orbisExport.outputs) {
    if ((Get-FileHash -LiteralPath $orbisDigest.path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $orbisDigest.sha256) { throw ('Stale export: ' + $orbisDigest.path) }
}
```

`WorldWorkspaceMenu.Export` deliberately forces one export: core `Orbis_Island.unity`, five `Environment_*` scenes and stream catalog. `FieldSceneAuthoring` obtains the real Unity source dependency hash, preserves the source, and writes the seven output SHAs. A plain source-file SHA is **not** a substitute for Unity's dependency hash. Preserve the export log's `ORBIS_FIELD_EXPORTED` result and a copy of the state alongside final evidence. Do not call the forced export again after the final occlusion bake: it clears generated PVS bindings and marks `needsOcclusionBake=true` even when the relevant geometry is unchanged.

## 2. Only the relevant logical and runtime regression

This first set is 24 boss-rule cases + four selected save cases + one actual Addressables/product-entry case = **29 cases**. It does not add unrelated daily/gacha tests.

```powershell
$orbisEditFilter = @(
    'Orbis.M4.Tests.M4BossTests',
    'Orbis.M4.Tests.M4ProgressTests.WeeklyBossRespawnsAtMondayBoundaryOnlyAndEachRegionPaysOnce',
    'Orbis.M4.Tests.M4ProgressTests.WeeklyBossProofCompletesCommissionBeforeOrAfterAccepting',
    'Orbis.M4.Tests.M4ProgressTests.ReloadPreservesCommissionStatesCoinsAndWeeklyBosses',
    'Orbis.M4.Tests.M4ContentTests.FiveAddressableScenesAreDistinctAndProductOrLegacyEntryIsValid'
) -join ';'
Invoke-OrbisFinal -Name 'FinalFieldLogic01' -ExpectedPassed 29 -UnityArgs @('-runTests', '-testPlatform', 'EditMode', '-testFilter', $orbisEditFilter)
Invoke-OrbisFinal -Name 'FinalIslandRuntime01' -ExpectedPassed 4 -UnityArgs @('-runTests', '-testPlatform', 'PlayMode', '-testFilter', 'Orbis.M4.Tests.M4IslandRuntimeTests')
Invoke-OrbisFinal -Name 'FinalStreaming01' -ExpectedPassed 2 -UnityArgs @('-runTests', '-testPlatform', 'PlayMode', '-testFilter', 'Orbis.Game.Tests.WorldRegionStreamingTests')
Invoke-OrbisFinal -Name 'FinalBuildPolicy01' -ExpectedPassed 1 -UnityArgs @('-runTests', '-testPlatform', 'EditMode', '-testFilter', 'Orbis.M4.Tests.FieldBuildPolicyTests.RegressionSceneScopeRestoresProductBuildAndPlayStartScene')
```

| Check | What it establishes | What it does not establish |
|---|---|---|
| Focused EditMode | Existing damage/weakness/exposure/reset/reward/save rules; actual regional addresses and normal entry registration | Art appearance or live Field hierarchy |
| `M4IslandRuntimeTests` | The **fresh exported core** retains five regions, one pawn/camera/party and F10 travel state; remote puzzle/challenge events and weekly resets remain local to their intended region | New source Field serialization by itself; every boss's visual quality |
| `WorldRegionStreamingTests` | Actual registered environment scenes load/unload additively, rapid requests do not duplicate them, destroyed host releases handles and resident terrain stays | Packed player IO/performance; independent source Field's in-editor scenery duplication |
| `FieldBuildPolicyTests` | Test scope restores normal build/Play start; only exported Island is enabled for the product | Export freshness without the preceding hash checks |

`Field.unity` is intentionally not an enabled player/build-list scene. The direct Field fixture uses `EditorSceneManager.LoadSceneAsyncInPlayMode` and then the normal entry flow; do not change the product build list to make a source-scene test load. The saved source's five editable scenery roots and runtime streaming have different ownership from the exported product; direct Field fixture success is not a streaming performance claim.

Conditional reruns only:

```powershell
# Only after a later approved source/profile/presenter change invalidates SavedField03:
Invoke-OrbisFinal -Name 'SavedFieldFinal04' -ExpectedPassed 1 -UnityArgs @('-runTests', '-testPlatform', 'PlayMode', '-testFilter', 'Orbis.Game.Tests.FieldBossSavedSceneTests.SavedFieldKeepsFiveBossBindingsAndPlaysIdleAttackAndExposure', '-fieldBossFinalRun', 'SavedFieldFinal04')
# Only when BossPoseClock/Presenter changes after its recorded passing version:
Invoke-OrbisFinal -Name 'FinalBossClock02' -ExpectedPassed 5 -UnityArgs @('-runTests', '-testPlatform', 'EditMode', '-testFilter', 'Orbis.Game.Tests.BossPoseClockTests')
# Example only if Rock prefab/profile/clips changed after RockBossRuntime01:
Invoke-OrbisFinal -Name 'FinalRockRuntime02' -ExpectedPassed 1 -UnityArgs @('-runTests', '-testPlatform', 'PlayMode', '-testFilter', 'Orbis.Game.Tests.BossRuntimeCaptureTests.OriginalGenericRigTracksExistingM4Encounter', '-bossRuntimeFolder', 'Assets/Orbis/Game/Characters/Bosses/Original01/RockBoss', '-bossRuntimeCaptureRun', 'FinalRockRuntime02')
```

No automatic `AuthoredGameSceneTests` run: it loads the obsolete `Orbis_OpenWorld`. Add `FieldGeometryBindingTests` only if exporter/resident-geometry ownership code changed; add the two named legacy M4 integration methods in `FinalRegressionPlan.md` only if their save/event/query code actually changed. Old milestones are not the final product scene.

## 3. PVS and packed Release build

The export invalidates generated PVS; if `needsOcclusionBake` is true, bake the six generated scenes jointly. This reuses the existing opaque architecture/rock policy and does not turn foliage/characters into static occluders. The asynchronous entry point must **not** receive `-quit`.

```powershell
$orbisExport = Get-Content -LiteralPath 'Assets/Orbis/Game/World/Authoring/FieldExportState.json' -Raw | ConvertFrom-Json
if ($orbisExport.needsOcclusionBake) {
    Invoke-OrbisFinal -Name 'FinalFieldOcclusion01' -UnityArgs @('-executeMethod', 'Orbis.Game.Editor.CharacterPipelineBatch.OpenAndBakeOcclusion', '-worldOcclusionReport', 'TestResults/CharacterPipeline/FinalFieldOcclusion01.json')
}
$orbisExport = Get-Content -LiteralPath 'Assets/Orbis/Game/World/Authoring/FieldExportState.json' -Raw | ConvertFrom-Json
if ($orbisExport.needsOcclusionBake) { throw 'Occlusion output is still marked stale.' }
foreach ($orbisDigest in $orbisExport.outputs) {
    if ((Get-FileHash -LiteralPath $orbisDigest.path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $orbisDigest.sha256) { throw ('Stale baked output: ' + $orbisDigest.path) }
}
# RunBenchmark.ps1 uses this exact path. It did not exist at this source review.
# If it now exists, preserve that build and select an explicit fresh output/launcher path.
if (Test-Path -LiteralPath 'Builds/WorldBenchmark/Release/Orbis.exe') { throw 'Preserve prior Release build; do not overwrite blindly.' }
Invoke-OrbisFinal -Name 'FinalFieldReleaseBuild01' -UnityArgs @('-executeMethod', 'Orbis.Game.Editor.CharacterPipelineBatch.OpenAndBuildWindows', '-worldBuildKind', 'Release', '-worldBuildOutput', 'Builds/WorldBenchmark/Release/Orbis.exe', '-worldBuildReport', 'TestResults/CharacterPipeline/FinalFieldReleaseBuild01.json')
```

The project target must already be Windows x64. `BuildWindows` checks it, performs **one** Packed Addressables build itself, temporarily enables frame timings, uses the normal product scene list and restores its build settings. Do not additionally run `BuildAddressables`, which would repeat work. Check JSON `result=Succeeded`, `errors=0`, `restoredBuildSettings=true`, one enabled Island entry, packed build output and layout report paths. Warnings must be read rather than assumed harmless. The PVS report must show one shared valid GUID across six scenes and a real payload. A PVS file or reduced bundle size is not measured FPS improvement.

The older `TestResults/WorldDev/Field_Build.json` was built on 2026-09-14 before these final character/boss changes. `PerformanceFinalSmoke` is a 2026-09-11 **offscreen Development** run. Neither is final-candidate performance evidence.

## 4. Actual player performance, and its limits

The existing launcher owns a fresh temporary save, records executable SHA/arguments in `Launch.json`, and guards evidence reuse. It launches hidden by default. A hidden normal window has previously produced **zero camera frames**; that run is invalid. An offscreen render is useful as a packed-render smoke check but is not normal presented-game FPS.

```powershell
# Optional hidden packed-render smoke only; no FPS claim from this mode:
& './Tools/WorldDev/RunBenchmark.ps1' -Kind Release -Name FinalPackedSmoke01 -Station Village -Seconds 5 -Warmup 2 -InitialWarmup 5 -Offscreen -Screenshots

# For a deliberately approved visible interactive performance window only:
# The first full route is sufficient; repeat/NoOcclusion only if an unresolved comparison requires it.
& './Tools/WorldDev/RunBenchmark.ps1' -Kind Release -Name FinalVisiblePerformance01 -Seconds 60 -Warmup 15 -InitialWarmup 30 -Visible
```

The visible command is explicit because it opens a 1920×1080 game window. This preparation step does not supply additional user permission or silently execute it. If a visible run is not currently authorized, retain the pending status and report that the target frame rate is unverified. Do not substitute a hidden/offscreen number for it.

For the full visible route require `complete=true`, no `error`, `development=false`, normal visible rendering mode, resolution 1920×1080, five station names exactly `Village/Forest/Lakeside/HighlandStorm/IslandTravel`, each `renderingConfirmed=true` and `renderedFrames>0`, and packed scene cycle `[5,0,5]`. A misspelled station filter could select no stations, so the expected station list must be checked independently.

Use actual `Wall frame time` and supported CPU/GPU timing mean/p95/p99/max in milliseconds, together with available draw/SetPass/triangle/GC/memory counters and the GPU/CPU/quality settings. FrameTiming GPU value 0 or absent means unavailable, not 0 ms. PNG/readback/warm-up occur outside timed intervals. The driver already stops remaining stations if mean wall time exceeds 50 ms; do not weaken that guard to finish a severe regression run.

The existing documented 1080p/Ultra/60 fps target on i7-10700 + GTX 1650 SUPER is a provisional assumption, not a user-confirmed hardware contract. For that provisional target compare actual frame times to 16.67 ms and state p95/p99 hitches separately. Do not estimate FPS from Blender duration, fixed 30 fps clips, frame-count capture tests, triangle counts or Editor batch rendering. No comparison is meaningful while another Unity/Blender GPU workload is running.

This benchmark follows five environment camera routes with a temporary **Stella** selection and normal world initialization. It does not execute boss combat, move the playable character along every route, benchmark Polaris separately, or prove worst-case close-up skinned-character/ultimate cost. Those are explicit limitations, not silently covered by a passing environment route. Only add a focused live encounter profiling run if needed to substantiate a final combat performance claim.

## Source anchors

- `Assets/Orbis/Game/Editor/FieldSceneAuthoring.cs`: transactional export, dependency/output hash state, PVS invalidation and `RecordBakedOutputs`.
- `Assets/Orbis/Game/Editor/WorldWorkspaceMenu.cs`: existing no-argument Field Open/Export entry points.
- `Assets/Orbis/Game/Editor/CharacterPipelineBatch.cs`: same-process Open + export/bake/build orchestration; rejects dirty scenes instead of saving/discarding them.
- `Assets/Orbis/EditorSupport/FieldSceneBuildPolicy.cs` and `TestRunner/FieldSceneTestRunScope.cs`: product vs test scene ownership.
- `Assets/Orbis/Game/Editor/WorldPerformanceBuilder.cs`: async bake, packed player build and restored settings.
- `Assets/Orbis/Game/Runtime/World/WorldPlayerBenchmark.cs`: native wall/frame timing, station/camera scope, zero-render and severe-cost guards.
- `Tools/WorldDev/RunBenchmark.ps1`: executable path, explicit visible/offscreen options, fresh evidence/save and launch hash.
