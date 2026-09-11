param(
    [Parameter(Mandatory = $true)]
    [string] $UnityPath,
    [switch] $Headless
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$editorPath = (Resolve-Path -LiteralPath $UnityPath).Path
$resultsRoot = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

function Invoke-M0Unity([string[]] $AdditionalArguments, [string] $LogName) {
    $logPath = Join-Path $resultsRoot $LogName
    $editorArguments = @('-batchmode', '-projectPath', ('"' + $projectRoot + '"'),
        '-logFile', ('"' + $logPath + '"')) + $AdditionalArguments
    if ($Headless) { $editorArguments += '-nographics' }
    $process = Start-Process -FilePath $editorPath -ArgumentList $editorArguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        throw "Unity failed with code $($process.ExitCode). See $logPath"
    }
}

Invoke-M0Unity @('-quit', '-executeMethod', 'Orbis.M0.Editor.M0ProjectSetup.SetupAndValidate') 'setup.log'
foreach ($mode in @('EditMode', 'PlayMode')) {
    $resultPath = Join-Path $resultsRoot ($mode + '.xml')
    # A stale success report must not make a failed run appear successful.
    $startedAt = [DateTime]::UtcNow
    Invoke-M0Unity @('-runTests', '-testPlatform', $mode,
        '-testResults', ('"' + $resultPath + '"')) ($mode + '.log')
    if (!(Test-Path -LiteralPath $resultPath) -or
        (Get-Item -LiteralPath $resultPath).LastWriteTimeUtc -lt $startedAt) {
        throw "Unity did not produce a new $mode result: $resultPath"
    }
    [xml] $result = Get-Content -LiteralPath $resultPath -Raw
    $run = $result.'test-run'
    if ($run.result -ne 'Passed' -or [int] $run.failed -gt 0) {
        throw "$mode tests failed. See $resultPath"
    }
    Write-Output "$mode passed: $($run.passed) tests. Results: $resultPath"
}
