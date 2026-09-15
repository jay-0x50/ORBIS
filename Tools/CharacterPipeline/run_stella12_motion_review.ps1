$ErrorActionPreference='Stop'
$orbisStage=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if(Get-Process Unity -ErrorAction SilentlyContinue){throw 'Another Unity process is active.'}
Copy-Item -LiteralPath (Join-Path $orbisStage 'Tools/CharacterPipeline/PendingStella12Preview/StellaLegCandidate12Preview.cs') -Destination (Join-Path $orbisStage 'Assets/Orbis/Game/Editor/StellaLegCandidate12Preview.cs')
function Invoke-OrbisReview([string]$orbisName,[string[]]$orbisExtra){
    $orbisLog=Join-Path $orbisStage ('TestResults/CharacterPipeline/'+$orbisName+'.log')
    if(Test-Path -LiteralPath $orbisLog){throw ('Preserve previous '+$orbisLog)}
    $orbisArgs=@('-batchmode','-quit','-projectPath',('"'+$orbisStage+'"'),'-logFile',('"'+$orbisLog+'"'))+$orbisExtra
    $orbisProcess=Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe' -ArgumentList $orbisArgs -WorkingDirectory $orbisStage -WindowStyle Hidden -PassThru
    Write-Output ($orbisName+' PID '+$orbisProcess.Id)
    $orbisProcess.WaitForExit()
    if($orbisProcess.ExitCode -ne 0){throw ($orbisName+' exit '+$orbisProcess.ExitCode)}
    Write-Output ($orbisName+' FINISHED')
}
Invoke-OrbisReview 'Stella12FrontKey01' @('-executeMethod','Orbis.Game.Editor.StellaLegCandidate12Preview.CaptureRequested','-stellaLegRun','Integrated12FrontKey01','-stellaKeyYaw','145')
Invoke-OrbisReview 'Stella12Base24Author' @('-executeMethod','Orbis.Game.Editor.CharacterMotionAuthor.AuthorRequested','-motionAuthorCharacter','Stella','-motionAuthorRoot','Assets/Orbis/Game/Characters/Candidates/StellaIntegrated12','-motionAuthorVersion','Stella12Base24','-motionAuthorTangents','Linear','-motionAuthorRate','180')
# Base24 references stay unchanged when Stop2 is appended; review the complete26 once below.
Invoke-OrbisReview 'Stella12Stop26Author' @('-executeMethod','Orbis.Game.Editor.CharacterMotionAuthor.AppendStopsRequested','-motionAppendStopsFrom','Assets/Orbis/Game/Characters/MotionCandidates/Stella12Base24/Stella/MotionManifest.json','-motionAuthorVersion','Stella12Stop26','-motionAuthorTangents','Linear','-motionAuthorRate','180')
Invoke-OrbisReview 'Stella12Stop26Review' @('-executeMethod','Orbis.Game.Editor.CharacterMotionReview.ReviewRequested','-motionManifest','Assets/Orbis/Game/Characters/MotionCandidates/Stella12Stop26/Stella/MotionManifest.json','-motionReviewRun','Stella12Stop26Full')
