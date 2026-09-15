param([string]$ProjectRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath $ProjectRoot).Path
$pending=Join-Path $taskRoot 'Tools/CharacterPipeline/PendingBossAnimation'
$output=Join-Path $taskRoot 'TestResults/CharacterPipeline/PendingBossCompile'
$editor='C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor'
$dotnet=Join-Path $editor 'Data/NetCoreRuntime/dotnet.exe'
$compiler=Join-Path $editor 'Data/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll'
$sourceRsp=Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Library/Bee/artifacts') -Recurse -Filter 'Orbis.Game.WorldEditModeTests.rsp' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if($null -eq $sourceRsp) { throw 'Existing Unity response files required.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$snapshot=Join-Path $output 'Sources'
New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
Get-ChildItem -LiteralPath $pending -Recurse -Filter '*.cs' | Copy-Item -Destination $snapshot
$results=@()
Push-Location -LiteralPath $taskRoot
try {
    foreach($assembly in @('Orbis.Game.Runtime','Orbis.Game.Editor','Orbis.Game.WorldEditModeTests')) {
        $arguments=@(Get-Content -LiteralPath (Join-Path $sourceRsp.DirectoryName ($assembly+'.rsp')) | ForEach-Object {
            $line=$_
            if($line -match '^-out:') { $line='-out:"'+(Join-Path $output ($assembly+'.dll')).Replace('\','/')+'"' }
            elseif($line -match '^-refout:') { $line='-refout:"'+(Join-Path $output ($assembly+'.ref.dll')).Replace('\','/')+'"' }
            elseif($assembly -ne 'Orbis.Game.Runtime' -and $line -match '^-r:.*Orbis.Game.Runtime.ref.dll') {
                $line='-r:"'+(Join-Path $output 'Orbis.Game.Runtime.ref.dll').Replace('\','/')+'"'
            }
            $line
        })
        if($assembly -eq 'Orbis.Game.Runtime') {
            $arguments+=Get-ChildItem -LiteralPath (Join-Path $pending 'Runtime') -Filter '*.cs' | ForEach-Object {
                '"'+(Join-Path $snapshot $_.Name).Replace('\','/')+'"'
            }
        } elseif($assembly -eq 'Orbis.Game.Editor') {
            $arguments+='"'+(Join-Path $snapshot 'BossMotionControllerBuilder.cs').Replace('\','/')+'"'
        } else {
            $arguments+='-r:"'+(Join-Path $sourceRsp.DirectoryName 'Orbis.M1.Runtime.ref.dll').Replace('\','/')+'"'
            $arguments+='"'+(Join-Path $snapshot 'BossPoseClockTests.cs').Replace('\','/')+'"'
        }
        $argsPath=Join-Path $output ($assembly+'.rsp')
        Set-Content -LiteralPath $argsPath -Value $arguments -Encoding utf8
        $messages=& $dotnet $compiler ('@'+$argsPath) 2>&1
        $code=$LASTEXITCODE
        $messages | Set-Content -LiteralPath (Join-Path $output ($assembly+'.log')) -Encoding utf8
        $results+=@{assembly=$assembly;exitCode=$code}
        if($code -ne 0) { throw ($assembly+': '+($messages -join "`n")) }
    }
    @{utc=[DateTime]::UtcNow.ToString('o');results=$results;
        sources=@(Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash);
        note='Static C# compatibility only. Unity tests and Generic animation playback have not run.'} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding utf8
    Write-Output 'Pending boss runtime and test assemblies compiled; tests not executed.'
} finally { Pop-Location }
