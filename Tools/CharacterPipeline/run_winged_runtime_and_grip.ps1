$ErrorActionPreference = 'Stop'
$orbisStage = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$orbisUnity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'Another Unity process is active.' }
foreach ($orbisBoss in @('WindBoss','LightningBoss')) {
    $orbisResult = Join-Path $orbisStage ('TestResults/CharacterPipeline/' + $orbisBoss + 'Runtime01.xml')
    $orbisLog = Join-Path $orbisStage ('TestResults/CharacterPipeline/' + $orbisBoss + 'Runtime01.log')
    $orbisArguments = @('-batchmode','-projectPath',('"'+$orbisStage+'"'),'-runTests','-testPlatform','PlayMode',
        '-testFilter','Orbis.Game.Tests.BossRuntimeCaptureTests','-bossRuntimeFolder',('Assets/Orbis/Game/Characters/Bosses/Original01/'+$orbisBoss),
        '-bossRuntimeCaptureRun','Runtime01','-testResults',('"'+$orbisResult+'"'),'-logFile',('"'+$orbisLog+'"'))
    $orbisProcess = Start-Process -FilePath $orbisUnity -ArgumentList $orbisArguments -WindowStyle Hidden -PassThru
    $orbisProcess.WaitForExit()
    if ($orbisProcess.ExitCode -ne 0) { throw ($orbisBoss + ' Unity exit ' + $orbisProcess.ExitCode) }
    [xml]$orbisXml = Get-Content -LiteralPath $orbisResult -Raw
    if ($orbisXml.'test-run'.failed -ne '0' -or $orbisXml.'test-run'.passed -ne '1') { throw ($orbisBoss + ' runtime test failed.') }
    Write-Output ($orbisBoss + ' ACTUAL_RUNTIME_PASS')
}
$orbisArguments = @('-batchmode','-quit','-projectPath',('"'+$orbisStage+'"'),'-executeMethod','Orbis.Game.Editor.HeroGripTransfer.RunRequested',
    '-gripRun','Transfer02','-logFile',('"'+(Join-Path $orbisStage 'TestResults/CharacterPipeline/UnityGripTransfer02.log')+'"'))
$orbisProcess = Start-Process -FilePath $orbisUnity -ArgumentList $orbisArguments -WindowStyle Hidden -PassThru
$orbisProcess.WaitForExit()
if ($orbisProcess.ExitCode -ne 0) { throw ('Grip Unity exit ' + $orbisProcess.ExitCode) }
Write-Output 'GRIP_TRANSFER02_CAPTURE_FINISHED'
