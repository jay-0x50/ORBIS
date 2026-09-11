param([string]$Target='D:\Project\ORBIS')
$ErrorActionPreference='Stop'
$orbisSource=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\')
$orbisTarget=[IO.Path]::GetFullPath($Target).TrimEnd('\')
if($orbisSource -eq $orbisTarget){throw 'Delivery source and target must be different folders.'}
if(-not (Test-Path -LiteralPath (Join-Path $orbisTarget 'ProjectSettings\ProjectVersion.txt'))){throw 'Target is not the approved existing Unity project.'}
$orbisManifestPath=Join-Path $orbisSource 'Docs\WorldArt\DeliveryManifest.json'
$orbisManifest=Get-Content -LiteralPath $orbisManifestPath -Raw | ConvertFrom-Json
if([IO.Path]::GetFullPath($orbisManifest.targetRoot).TrimEnd('\') -ne $orbisTarget){throw 'Target differs from reviewed delivery manifest.'}
if(@($orbisManifest.conflicts).Count -ne 0){throw 'Resolve manifest conflicts before copying.'}
$orbisEntries=@($orbisManifest.entries)
$orbisOriginals=Get-Content -LiteralPath (Join-Path $orbisSource 'WorldArtBackups\BeforeWorldUpgrade\OriginalFiles.json') -Raw | ConvertFrom-Json
$orbisOriginalMap=@{}
foreach($orbisOriginal in $orbisOriginals){
    $orbisOriginalMap[$orbisOriginal.path]=$orbisOriginal.sha256
    $orbisBackupFile=Join-Path $orbisSource ('WorldArtBackups\BeforeWorldUpgrade\'+$orbisOriginal.path)
    if((Get-FileHash -LiteralPath $orbisBackupFile -Algorithm SHA256).Hash -ne $orbisOriginal.sha256){throw "Prepared original backup mismatch: $($orbisOriginal.path)"}
}
foreach($orbisName in @('Docs/WorldArt/DeliveryManifest.json','Docs/WorldArt/Delivery_FILES.md')){
    $orbisPath=Join-Path $orbisSource $orbisName
    $orbisExisting=Join-Path $orbisTarget $orbisName
    $orbisOldHash=if(Test-Path -LiteralPath $orbisExisting){(Get-FileHash -LiteralPath $orbisExisting -Algorithm SHA256).Hash}else{$null}
    # These two generated artifacts are omitted from their own hash list. If they already
    # exist, only an identical artifact is accepted rather than replacing an unreviewed one.
    $orbisNewHash=(Get-FileHash -LiteralPath $orbisPath -Algorithm SHA256).Hash
    if($orbisOldHash -and $orbisOldHash -ne $orbisNewHash){throw "Existing delivery artifact differs: $orbisName"}
    $orbisEntries += [pscustomobject]@{path=$orbisName;sha256=$orbisNewHash;observedTarget=$orbisOldHash;bytes=(Get-Item -LiteralPath $orbisPath).Length;kind='delivery-index'}
}
function Test-OrbisEntry($entry){
    $orbisFrom=[IO.Path]::GetFullPath((Join-Path $orbisSource $entry.path))
    $orbisTo=[IO.Path]::GetFullPath((Join-Path $orbisTarget $entry.path))
    if(-not $orbisFrom.StartsWith($orbisSource+'\',[StringComparison]::OrdinalIgnoreCase) -or -not $orbisTo.StartsWith($orbisTarget+'\',[StringComparison]::OrdinalIgnoreCase)){throw "Path escaped approved project: $($entry.path)"}
    if((Get-FileHash -LiteralPath $orbisFrom -Algorithm SHA256).Hash -ne $entry.sha256){throw "Source changed after planning: $($entry.path)"}
    $orbisCurrent=if(Test-Path -LiteralPath $orbisTo){(Get-FileHash -LiteralPath $orbisTo -Algorithm SHA256).Hash}else{$null}
    if($orbisCurrent -ne $entry.observedTarget -and $orbisCurrent -ne $entry.sha256){throw "Target changed after planning: $($entry.path)"}
}
# Preflight the entire concrete list before the first write, then recheck each entry.
foreach($orbisEntry in $orbisEntries){Test-OrbisEntry $orbisEntry}
# Preserve prepared originals in the actual project before updating its 22 existing files.
# New files are created with overwrite=false; no new-file collision can be overwritten.
$orbisEntries=@($orbisEntries | Sort-Object @{Expression={if($_.kind -eq 'original-backup'){0}elseif(-not $_.observedTarget){1}else{2}}},path)
$orbisCopied=0;$orbisReused=0;$orbisUpdated=0
foreach($orbisEntry in $orbisEntries){
    Test-OrbisEntry $orbisEntry
    $orbisFrom=Join-Path $orbisSource $orbisEntry.path
    $orbisTo=Join-Path $orbisTarget $orbisEntry.path
    if((Test-Path -LiteralPath $orbisTo) -and (Get-FileHash -LiteralPath $orbisTo -Algorithm SHA256).Hash -eq $orbisEntry.sha256){$orbisReused++;continue}
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($orbisTo)) | Out-Null
    if(-not (Test-Path -LiteralPath $orbisTo)){
        [IO.File]::Copy($orbisFrom,$orbisTo,$false)
    }else{
        if(-not $orbisEntry.baseline -or $orbisOriginalMap[$orbisEntry.path] -ne $orbisEntry.baseline){throw "No reviewed original backup for update: $($orbisEntry.path)"}
        $orbisLocalBackup=Join-Path $orbisTarget ('WorldArtBackups\BeforeWorldUpgrade\'+$orbisEntry.path)
        if((Get-FileHash -LiteralPath $orbisLocalBackup -Algorithm SHA256).Hash -ne $orbisEntry.baseline){throw "Destination original backup mismatch: $($orbisEntry.path)"}
        if((Get-FileHash -LiteralPath $orbisTo -Algorithm SHA256).Hash -ne $orbisEntry.baseline){throw "Original changed immediately before update: $($orbisEntry.path)"}
        Copy-Item -LiteralPath $orbisFrom -Destination $orbisTo -Force
        $orbisUpdated++
    }
    if((Get-FileHash -LiteralPath $orbisTo -Algorithm SHA256).Hash -ne $orbisEntry.sha256){throw "Copy verification failed: $($orbisEntry.path)"}
    $orbisCopied++
}
$orbisVerified=0
foreach($orbisEntry in $orbisEntries){
    if((Get-FileHash -LiteralPath (Join-Path $orbisTarget $orbisEntry.path) -Algorithm SHA256).Hash -ne $orbisEntry.sha256){throw "Final destination mismatch: $($orbisEntry.path)"}
    $orbisVerified++
}
$orbisAudit=[pscustomobject]@{utc=[DateTime]::UtcNow.ToString('O');source=$orbisSource;target=$orbisTarget;copied=$orbisCopied;updatedExisting=$orbisUpdated;originalBackup='WorldArtBackups/BeforeWorldUpgrade';alreadyIdentical=$orbisReused;verified=$orbisVerified;deleted=0;allDestinationHashesMatch=$true;manifestSha256=(Get-FileHash -LiteralPath $orbisManifestPath -Algorithm SHA256).Hash}
$orbisAudit | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $orbisSource 'Docs\WorldArt\DeliveryVerification.json') -Encoding utf8
$orbisAudit | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $orbisTarget 'Docs\WorldArt\DeliveryVerification.json') -Encoding utf8
$orbisAudit | ConvertTo-Json
