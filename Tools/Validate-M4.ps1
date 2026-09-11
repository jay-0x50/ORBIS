<#
.SYNOPSIS
Builds local M4 Addressable content and runs all M0-M4 regression suites.
.DESCRIPTION
Run with the Unity Editor closed for this project. The default builds local
Addressable bundles, then runs all EditMode and PlayMode tests.
No standalone player executable is built. Headless mode may skip GPU-output tests.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $UnityPath,
    [switch] $SkipContentBuild,
    [switch] $Headless
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$editorPath = (Resolve-Path -LiteralPath $UnityPath).Path
if (!(Test-Path -LiteralPath $editorPath -PathType Leaf)) {
    throw "UnityPath must point to the Unity Editor executable."
}
$resultsRoot = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

function Invoke-M4Unity([string[]] $AdditionalArguments, [string] $LogName) {
    $logPath = Join-Path $resultsRoot $LogName
    $editorArguments = @('-batchmode', '-projectPath', ('"' + $projectRoot + '"'),
        '-logFile', ('"' + $logPath + '"')) + $AdditionalArguments
    if ($Headless) { $editorArguments += '-nographics' }
    $process = Start-Process -FilePath $editorPath -ArgumentList $editorArguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Unity failed with code $($process.ExitCode). See $logPath"
    }
}

# BuildContent performs SetupAndValidate itself; avoid running prerequisite setup twice.
$setupMethod = if ($SkipContentBuild) {
    'Orbis.M4.Editor.M4ProjectSetup.SetupAndValidate'
} else {
    'Orbis.M4.Editor.M4ProjectSetup.BuildContent'
}
Invoke-M4Unity @('-quit', '-executeMethod', $setupMethod) 'M4-setup.log'

foreach ($mode in @('EditMode', 'PlayMode')) {
    $resultPath = Join-Path $resultsRoot ('M4-' + $mode + '.xml')
    # Preserve old artifacts, but require a fresh report so stale success cannot hide a failed run.
    $startedAt = [DateTime]::UtcNow
    Invoke-M4Unity @('-runTests', '-testPlatform', $mode,
        '-testResults', ('"' + $resultPath + '"')) ('M4-' + $mode + '.log')
    if (!(Test-Path -LiteralPath $resultPath) -or
        (Get-Item -LiteralPath $resultPath).LastWriteTimeUtc -lt $startedAt) {
        throw "Unity did not produce a new $mode result: $resultPath"
    }
    [xml] $result = Get-Content -LiteralPath $resultPath -Raw
    $run = $result.'test-run'
    if ($null -eq $run -or [int] $run.total -le 0 -or
        $run.result -ne 'Passed' -or [int] $run.failed -gt 0) {
        throw "$mode tests failed or no tests ran. See $resultPath"
    }
    Write-Output "$mode passed: $($run.passed) tests; skipped: $($run.skipped). Results: $resultPath"
}
