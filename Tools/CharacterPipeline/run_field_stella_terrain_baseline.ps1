$ErrorActionPreference='Stop'
$orbisStage=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if(Get-Process Unity,blender -ErrorAction SilentlyContinue){throw 'Another GPU tool is active.'}
function Invoke-OrbisReview([string]$orbisName,[string[]]$orbisExtra,[bool]$orbisTest){
    $orbisLog=Join-Path $orbisStage ('TestResults/CharacterPipeline/'+$orbisName+'.log')
    if(Test-Path -LiteralPath $orbisLog){throw ('Preserve previous '+$orbisLog)}
    $orbisArgs=@('-batchmode','-projectPath',('"'+$orbisStage+'"'),'-logFile',('"'+$orbisLog+'"'))+$orbisExtra
    if($orbisTest){$orbisResult=Join-Path $orbisStage ('TestResults/CharacterPipeline/'+$orbisName+'.xml');$orbisArgs+=@('-testResults',('"'+$orbisResult+'"'))}else{$orbisArgs+='-quit'}
    $orbisProcess=Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe' -ArgumentList $orbisArgs -WorkingDirectory $orbisStage -WindowStyle Hidden -PassThru
    Write-Output ($orbisName+' PID '+$orbisProcess.Id)
    $orbisProcess.WaitForExit()
    if($orbisProcess.ExitCode -ne 0){throw ($orbisName+' exit '+$orbisProcess.ExitCode)}
    if($orbisTest){[xml]$orbisXml=Get-Content -LiteralPath $orbisResult -Raw;if($orbisXml.'test-run'.failed-ne'0'){throw ($orbisName+' test failed')}}
    Write-Output ($orbisName+' FINISHED')
}
Invoke-OrbisReview 'FieldBossVisual04Review' @('-executeMethod','Orbis.Game.Editor.FieldBossVisualInstaller.ReviewRequested','-bossFieldRun','BossVisual04') $false
Invoke-OrbisReview 'Stella12MotionInstall01' @('-executeMethod','Orbis.Game.Editor.CharacterMotionCandidateInstaller.InstallRequested','-motionManifest','Assets/Orbis/Game/Characters/MotionCandidates/Stella12Stop26/Stella/MotionManifest.json','-motionEvidence','TestResults/CharacterPipeline/MotionReview/Stella/Stella12Stop26Full/playback_review.json','-motionInstallVersion','Stella12MotionRuntime01') $false
Invoke-OrbisReview 'PolarisTerrainCameraBaseline01' @('-runTests','-testPlatform','PlayMode','-testFilter','Orbis.Game.Tests.CharacterMotionCaptureTests',
    '-motionStage','After','-motionRun','PolarisTerrainCameraBaseline01','-motionCharacter','Polaris','-motionCandidateRoot','Assets/Orbis/Game/Characters/Candidates/Motion06GripRuntime01',
    '-motionCandidateMotion','true','-motionActions','true','-motionTerrain','true') $true
