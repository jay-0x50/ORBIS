param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedManifestSha256,
    [ValidateSet('Validate','Apply','Verify')][string]$Mode = 'Validate',
    [ValidatePattern('^[0-9]{8}-[0-9]{6}$')][string]$BackupStamp = (Get-Date -Format 'yyyyMMdd-HHmmss')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$orbisSourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd('\','/')
$orbisTargetRoot = [IO.Path]::GetFullPath('D:\Project\ORBIS').TrimEnd('\','/')
$orbisManifestRelative = 'Tools/Field/Field_DeliveryManifest.json'

function Assert-NoReparsePoint([string]$AbsolutePath, [string]$Root) {
    $probe = $AbsolutePath
    while ($probe.Length -ge $Root.Length) {
        if (Test-Path -LiteralPath $probe) {
            $item = Get-Item -LiteralPath $probe -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing linked filesystem path: $probe"
            }
        }
        if ($probe.Equals($Root, [StringComparison]::OrdinalIgnoreCase)) { break }
        $parent = [IO.Path]::GetDirectoryName($probe)
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $probe) { break }
        $probe = $parent
    }
}

function Resolve-ContainedFile([string]$Root, [string]$RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or $RelativePath -match '(^/|\\|:|(^|/)\.\.?(/|$))') {
        throw "Invalid relative path: $RelativePath"
    }
    if ($RelativePath -match '(?i)^(Assets/(Img|blend|ImportedAssets)/|Library/|Temp/|Logs/|UserSettings/|\.git/)' -or
        $RelativePath -match '(?i)\.blend[0-9]*$') {
        throw "Protected or non-deliverable path: $RelativePath"
    }
    $absolute = [IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    if (-not $absolute.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolved path escapes the intended project: $absolute"
    }
    Assert-NoReparsePoint $absolute $Root
    return $absolute
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Utf8Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 12) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

$orbisManifestPath = Resolve-ContainedFile $orbisSourceRoot $orbisManifestRelative
$orbisManifestHash = Get-Sha256 $orbisManifestPath
if ($orbisManifestHash -ne $ExpectedManifestSha256.ToLowerInvariant()) { throw 'Manifest differs from the reviewed SHA-256.' }
$orbisManifest = Get-Content -LiteralPath $orbisManifestPath -Raw | ConvertFrom-Json
if ($orbisManifest.schemaVersion -ne 1 -or $orbisManifest.manifestRelativePath -ne $orbisManifestRelative -or
    [IO.Path]::GetFullPath($orbisManifest.sourceRoot).TrimEnd('\','/') -ne $orbisSourceRoot -or
    [IO.Path]::GetFullPath($orbisManifest.targetRoot).TrimEnd('\','/') -ne $orbisTargetRoot) {
    throw 'Manifest roots/schema do not match this staged project and the fixed ORBIS target.'
}
if (-not (Test-Path -LiteralPath $orbisTargetRoot -PathType Container)) { throw 'The approved target project does not exist.' }
Assert-NoReparsePoint $orbisSourceRoot $orbisSourceRoot
Assert-NoReparsePoint $orbisTargetRoot $orbisTargetRoot
$orbisSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$orbisPlan = [Collections.Generic.List[object]]::new()
foreach ($record in $orbisManifest.files) {
    if (-not $orbisSeen.Add($record.path)) { throw "Duplicate manifest path: $($record.path)" }
    if ($record.kind -notin @('changed','new') -or $record.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw "Invalid manifest entry: $($record.path)"
    }
    $source = Resolve-ContainedFile $orbisSourceRoot $record.path
    $destination = Resolve-ContainedFile $orbisTargetRoot $record.path
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Sha256 $source) -ne $record.sha256 -or
        (Get-Item -LiteralPath $source).Length -ne [long]$record.bytes) {
        throw "Staging file changed after review: $($record.path)"
    }
    $exists = Test-Path -LiteralPath $destination
    if ($exists -and -not (Test-Path -LiteralPath $destination -PathType Leaf)) { throw "Target is not a regular file: $destination" }
    $current = if ($exists) { Get-Sha256 $destination } else { $null }
    if ($Mode -eq 'Verify') {
        if ($current -ne $record.sha256) { throw "Delivered hash mismatch: $($record.path)" }
    } elseif ($record.kind -eq 'changed') {
        if ($record.baselineSha256 -notmatch '^[0-9a-f]{64}$' -or $current -ne $record.baselineSha256) {
            throw "Existing target changed since task baseline; nothing copied: $($record.path)"
        }
    } elseif ($exists -and $current -ne $record.sha256) {
        throw "New target path already contains different user data; nothing copied: $($record.path)"
    }
    $orbisPlan.Add([pscustomobject]@{ Record=$record; Source=$source; Destination=$destination; CurrentSha256=$current })
}
$orbisTargetManifest = Resolve-ContainedFile $orbisTargetRoot $orbisManifestRelative
if (Test-Path -LiteralPath $orbisTargetManifest) {
    if ((Get-Sha256 $orbisTargetManifest) -ne $orbisManifestHash) { throw 'Target delivery manifest already contains a different plan.' }
} elseif ($Mode -eq 'Verify') { throw 'Delivered manifest is missing.' }
if ($Mode -eq 'Verify') {
    [pscustomobject]@{ verified=$true; files=$orbisPlan.Count; manifestSha256=$orbisManifestHash } | ConvertTo-Json
    exit 0
}
if ($Mode -eq 'Validate') {
    [pscustomobject]@{ ready=$true; files=$orbisPlan.Count; changed=@($orbisPlan | Where-Object { $_.Record.kind -eq 'changed' }).Count;
        targetModified=$false; manifestSha256=$orbisManifestHash } | ConvertTo-Json
    exit 0
}

# Preflight succeeded for every entry. All originals are copied and verified BEFORE the first replacement.
$orbisBackupRelative = 'WorldArtBackups/BeforeFieldAuthoring/' + $BackupStamp
$orbisBackupRoot = Resolve-ContainedFile $orbisTargetRoot $orbisBackupRelative
if (Test-Path -LiteralPath $orbisBackupRoot) { throw 'Backup folder already exists; choose a fresh timestamp.' }
[IO.Directory]::CreateDirectory($orbisBackupRoot) | Out-Null
$orbisBackups = [Collections.Generic.List[object]]::new()
foreach ($item in $orbisPlan) {
    if ($item.Record.kind -ne 'changed') { continue }
    $backup = Resolve-ContainedFile $orbisBackupRoot $item.Record.path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
    Copy-Item -LiteralPath $item.Destination -Destination $backup
    if ((Get-Sha256 $backup) -ne $item.Record.baselineSha256) { throw "Backup verification failed: $($item.Record.path)" }
    $orbisBackups.Add([pscustomobject]@{path=$item.Record.path; sha256=$item.Record.baselineSha256; bytes=(Get-Item -LiteralPath $backup).Length})
}
Write-Utf8Json (Join-Path $orbisBackupRoot 'BackupManifest.json') ([pscustomobject]@{
    manifestSha256=$orbisManifestHash; createdUtc=[DateTime]::UtcNow.ToString('o'); files=$orbisBackups.ToArray()
})
# Re-check source and target hashes after backup, before writing any approved output.
foreach ($item in $orbisPlan) {
    Assert-NoReparsePoint $item.Source $orbisSourceRoot
    Assert-NoReparsePoint $item.Destination $orbisTargetRoot
    if ((Get-Sha256 $item.Source) -ne $item.Record.sha256) { throw "Source changed during backup: $($item.Record.path)" }
    $exists = Test-Path -LiteralPath $item.Destination
    $current = if ($exists) { Get-Sha256 $item.Destination } else { $null }
    if ($current -ne $item.CurrentSha256) { throw "Target changed during backup: $($item.Record.path)" }
}
foreach ($item in $orbisPlan) {
    if ($item.CurrentSha256 -eq $item.Record.sha256) { continue }
    Assert-NoReparsePoint $item.Source $orbisSourceRoot
    Assert-NoReparsePoint $item.Destination $orbisTargetRoot
    if ((Get-Sha256 $item.Source) -ne $item.Record.sha256) { throw "Source changed during delivery: $($item.Record.path)" }
    $current = if (Test-Path -LiteralPath $item.Destination) { Get-Sha256 $item.Destination } else { $null }
    if ($current -ne $item.CurrentSha256) { throw "Target changed during delivery: $($item.Record.path)" }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($item.Destination)) | Out-Null
    Copy-Item -LiteralPath $item.Source -Destination $item.Destination
    if ((Get-Sha256 $item.Destination) -ne $item.Record.sha256) { throw "Post-copy hash mismatch: $($item.Record.path)" }
}
if ((Get-Sha256 $orbisManifestPath) -ne $orbisManifestHash) { throw 'Reviewed manifest changed during delivery.' }
Assert-NoReparsePoint $orbisTargetManifest $orbisTargetRoot
if ((Test-Path -LiteralPath $orbisTargetManifest) -and (Get-Sha256 $orbisTargetManifest) -ne $orbisManifestHash) {
    throw 'Target manifest changed during delivery; it was not overwritten.'
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($orbisTargetManifest)) | Out-Null
Copy-Item -LiteralPath $orbisManifestPath -Destination $orbisTargetManifest
foreach ($item in $orbisPlan) {
    if ((Get-Sha256 $item.Destination) -ne $item.Record.sha256) { throw "Final delivery hash mismatch: $($item.Record.path)" }
}
if ((Get-Sha256 $orbisTargetManifest) -ne $orbisManifestHash) { throw 'Delivered manifest hash mismatch.' }
$orbisReceipt = [pscustomobject]@{ verified=$true; manifestSha256=$orbisManifestHash; files=$orbisPlan.Count;
    backupRoot=$orbisBackupRoot; backupFiles=$orbisBackups.Count; completedUtc=[DateTime]::UtcNow.ToString('o') }
Write-Utf8Json (Join-Path $orbisBackupRoot 'DeliveryReceipt.json') $orbisReceipt
$orbisReceipt | ConvertTo-Json
