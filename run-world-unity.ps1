param([string]$Method='', [string]$Step='', [string]$Platform='PlayMode', [string]$Filter='Orbis.Game.Tests.WorldVisualTests')
$ErrorActionPreference='Stop'
$orbisProject=(Get-Location).Path
$orbisLog=Join-Path $orbisProject ('TestResults\WorldDev\'+$Step+'.log')
$orbisArgs='-batchmode -projectPath "'+$orbisProject+'" -logFile "'+$orbisLog+'"'
if($Method){$orbisArgs+=' -quit -executeMethod '+$Method}
else{
 $orbisArgs+=' -runTests -testPlatform '+$Platform+' -testResults "'+(Join-Path $orbisProject ('TestResults\WorldDev\'+$Step+'.xml'))+'"'
 if($Filter){$orbisArgs+=' -testFilter "'+$Filter+'"'}
 if($Step){$orbisArgs+=' -worldStep '+$Step}
}
$orbisProcess=Start-Process -FilePath 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' -ArgumentList $orbisArgs -WindowStyle Hidden -PassThru
Write-Output ('Unity '+$Step+' PID '+$orbisProcess.Id)
$orbisProcess.WaitForExit()
Write-Output ('Unity '+$Step+' exit '+$orbisProcess.ExitCode)
if($orbisProcess.ExitCode -ne 0){Get-Content -LiteralPath $orbisLog -Tail 65;exit $orbisProcess.ExitCode}
