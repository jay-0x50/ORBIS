$ErrorActionPreference='Stop'
$orbisStage=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$orbisUnity='C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
if(Get-Process Unity -ErrorAction SilentlyContinue){throw 'Another Unity process is active.'}
function Invoke-OrbisReview([string]$orbisName,[string[]]$orbisExtra,[bool]$orbisTest) {
    $orbisLog=Join-Path $orbisStage ('TestResults/CharacterPipeline/'+$orbisName+'.log')
    if(Test-Path -LiteralPath $orbisLog){throw ('Preserve previous '+$orbisLog)}
    $orbisArguments=@('-batchmode','-projectPath',('"'+$orbisStage+'"'),'-logFile',('"'+$orbisLog+'"'))+$orbisExtra
    if($orbisTest){$orbisResult=Join-Path $orbisStage ('TestResults/CharacterPipeline/'+$orbisName+'.xml');$orbisArguments+=@('-testResults',('"'+$orbisResult+'"'))}
    else{$orbisArguments+='-quit'}
    $orbisProcess=Start-Process -FilePath $orbisUnity -ArgumentList $orbisArguments -WorkingDirectory $orbisStage -WindowStyle Hidden -PassThru
    Write-Output ($orbisName+' PID '+$orbisProcess.Id)
    $orbisProcess.WaitForExit()
    if($orbisProcess.ExitCode -ne 0){throw ($orbisName+' exit '+$orbisProcess.ExitCode)}
    if($orbisTest){[xml]$orbisXml=Get-Content -LiteralPath $orbisResult -Raw;if($orbisXml.'test-run'.failed-ne'0'-or$orbisXml.'test-run'.passed-ne'1'){throw ($orbisName+' test failed')}}
    Write-Output ($orbisName+' FINISHED')
}
Invoke-OrbisReview 'Motion05After07' @('-runTests','-testPlatform','PlayMode','-testFilter','Orbis.Game.Tests.CharacterMotionCaptureTests',
    '-motionStage','After','-motionRun','Motion05After07','-motionCharacter','Polaris','-motionCandidateRoot','Assets/Orbis/Game/Characters/Candidates/Motion05StopsRuntime01',
    '-motionCandidateMotion','true','-motionActions','true') $true
Invoke-OrbisReview 'RockBossRuntime01' @('-runTests','-testPlatform','PlayMode','-testFilter','Orbis.Game.Tests.BossRuntimeCaptureTests',
    '-bossRuntimeFolder','Assets/Orbis/Game/Characters/Bosses/Original01/RockBoss','-bossRuntimeCaptureRun','Runtime01') $true
Invoke-OrbisReview 'PolarisGripCandidate01' @('-executeMethod','Orbis.Game.Editor.CharacterGripCandidateInstaller.InstallRequested',
    '-gripMotionPrefab','Assets/Orbis/Game/Characters/Candidates/Motion05StopsRuntime01/Polaris/Polaris.prefab',
    '-gripEvidence','TestResults/CharacterPipeline/UnityGripReview/Polaris/Transfer03/MeasuredTransfer.json','-gripInstallRun','Motion06GripRuntime01') $false
Invoke-OrbisReview 'FieldBossBindingAudit01' @('-executeMethod','Orbis.Game.Editor.FieldBossBindingAudit.Run') $false
