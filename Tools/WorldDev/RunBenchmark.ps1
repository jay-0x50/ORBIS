param(
    [ValidateSet('Development','Release')][string]$Kind='Development',
    [string]$Name='PerformanceBaseline',
    [int]$Seconds=60,[int]$Warmup=15,[int]$InitialWarmup=30,
    [string]$Station='',[switch]$NoOcclusion,[switch]$Screenshots,[switch]$Offscreen,[switch]$Visible
)
$ErrorActionPreference='Stop'
if($Name -notmatch '^[A-Za-z0-9_-]+$'){throw 'Name must be a simple evidence folder name.'}
$orbisRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$orbisExe=Join-Path $orbisRoot ('Builds\WorldBenchmark\'+$Kind+'\Orbis.exe')
$orbisOutput=Join-Path $orbisRoot ('TestResults\WorldDev\'+$Name)
if(Test-Path -LiteralPath (Join-Path $orbisOutput 'WorldPerformance.json')){throw 'Use a fresh evidence name to preserve previous measurements.'}
New-Item -ItemType Directory -Force -Path $orbisOutput | Out-Null
$orbisArgs='-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -force-d3d11 -orbisWorldBenchmark -benchmarkOutput "'+$orbisOutput+'" -benchmarkSeconds '+$Seconds+' -benchmarkWarmup '+$Warmup+' -benchmarkInitialWarmup '+$InitialWarmup+' -logFile "'+(Join-Path $orbisOutput 'Player.log')+'"'
if($NoOcclusion){$orbisArgs+=' -benchmarkNoOcclusion'}
if($Screenshots){$orbisArgs+=' -benchmarkScreenshots'}
if($Offscreen){$orbisArgs+=' -benchmarkOffscreen'}
if($Station){if($Station -notmatch '^[A-Za-z]+$'){throw 'Invalid station.'};$orbisArgs+=' -benchmarkStation '+$Station}
$orbisMetadata=@{utc=[DateTime]::UtcNow.ToString('O');executable=$orbisExe;sha256=(Get-FileHash -LiteralPath $orbisExe).Hash;arguments=$orbisArgs;visible=[bool]$Visible;offscreen=[bool]$Offscreen;note='runInBackground=true. Other user applications are not closed. Visible requires the caller to deliberately select -Visible; hidden normal cameras may be skipped by Unity.'}
$orbisMetadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $orbisOutput 'Launch.json') -Encoding utf8
$orbisWindowStyle=if($Visible){'Normal'}else{'Hidden'}
$orbisProcess=Start-Process -FilePath $orbisExe -ArgumentList $orbisArgs -WindowStyle $orbisWindowStyle -PassThru -WorkingDirectory $orbisRoot
Write-Output ('ORBIS Player PID '+$orbisProcess.Id+' | '+$Name)
$orbisProcess.WaitForExit()
Write-Output ('ORBIS Player exit '+$orbisProcess.ExitCode)
if($orbisProcess.ExitCode -ne 0){Get-Content -LiteralPath (Join-Path $orbisOutput 'Player.log') -Tail 35;exit $orbisProcess.ExitCode}
