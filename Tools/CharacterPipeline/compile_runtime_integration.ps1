param([string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$output = Join-Path $taskRoot 'TestResults/CharacterPipeline/PendingIntegrationCompile'
$pending = Join-Path $taskRoot 'Tools/CharacterPipeline/PendingIntegration'
$manifest = Get-Content -LiteralPath (Join-Path $pending 'manifest.json') -Raw | ConvertFrom-Json
$editor = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor'
$dotnet = Join-Path $editor 'Data/NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $editor 'Data/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll'
$rsp = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Library/Bee/artifacts') -Recurse -Filter Orbis.Game.Editor.rsp |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $rsp) { throw 'Unity Editor response files required; this script never starts Unity.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$snapshot = Join-Path $output 'MotionRuntimeSnapshot'
New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Tools/CharacterPipeline/PendingAnimation/Runtime') -Filter '*.cs' |
    Copy-Item -Destination $snapshot
$compiled = @{}
$results = @()
Push-Location -LiteralPath $taskRoot
try {
    foreach ($assembly in @('Orbis.M0.Runtime','Orbis.M2.Runtime','Orbis.M3.Runtime','Orbis.Art.Runtime','Orbis.M16.Runtime')) {
        $arguments = @(Get-Content -LiteralPath (Join-Path $rsp.DirectoryName ($assembly + '.rsp')) | ForEach-Object {
            $line = $_
            if ($line -match '^-out:') { $line = '-out:"' + (Join-Path $output ($assembly+'.dll')).Replace('\','/') + '"' }
            elseif ($line -match '^-refout:') { $line = '-refout:"' + (Join-Path $output ($assembly+'.ref.dll')).Replace('\','/') + '"' }
            elseif ($line -match '^-r:') {
                foreach ($dependency in $compiled.Keys) {
                    if ($line -match ([regex]::Escape($dependency) + '\.ref\.dll')) {
                        $line = '-r:"' + $compiled[$dependency].Replace('\','/') + '"'
                        break
                    }
                }
            } else {
                foreach ($file in $manifest.files) {
                    if ($line.Trim('"').Replace('\','/') -eq $file.path) {
                        $line = '"' + (Join-Path $pending $file.path).Replace('\','/') + '"'
                        break
                    }
                }
            }
            $line
        })
        if ($assembly -eq 'Orbis.M0.Runtime') {
            $arguments += Get-ChildItem -LiteralPath $snapshot -Filter '*.cs' | Sort-Object Name | ForEach-Object { '"' + $_.FullName.Replace('\','/') + '"' }
        }
        $argsPath = Join-Path $output ($assembly+'.rsp')
        Set-Content -LiteralPath $argsPath -Value $arguments -Encoding utf8
        $text = & $dotnet $compiler ('@'+$argsPath) 2>&1
        $code = $LASTEXITCODE
        $text | Set-Content -LiteralPath (Join-Path $output ($assembly+'.log')) -Encoding utf8
        $results += @{ assembly=$assembly; exitCode=$code }
        if ($code -ne 0) { throw ($assembly + ' compilation failed: ' + ($text -join "`n")) }
        $compiled[$assembly] = Join-Path $output ($assembly+'.ref.dll')
    }
    @{ utc=[DateTime]::UtcNow.ToString('o'); results=$results; originalFiles=$manifest.files;
        note='Pending C# API/assembly integration only. No Unity runtime or gameplay assets changed. Motion snapshot retained.' } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding utf8
    Write-Output 'Pending integration: five runtime assemblies compiled successfully.'
}
finally { Pop-Location }
