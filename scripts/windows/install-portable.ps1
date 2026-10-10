[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$InstallRoot,

    [string]$ExpectedSha256,

    [string]$EntryPoint = "Compositor.App.exe",

    [string]$ManifestFile = "Compositor.Portable.json",

    [string]$UserDataRoot
)

$ErrorActionPreference = "Stop"

function Get-SafeRelativePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) {
        throw "Path must be a non-empty relative path: $Path"
    }
    $normalized = $Path.Replace('\', '/')
    $segments = $normalized.Split('/')
    if ($normalized.Contains(':') -or ($segments | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' })) {
        throw "Path contains an unsafe segment: $Path"
    }
    return $normalized
}

$package = [IO.Path]::GetFullPath($PackagePath)
$root = [IO.Path]::GetFullPath($InstallRoot)
$entry = Get-SafeRelativePath $EntryPoint
$manifestName = Get-SafeRelativePath $ManifestFile
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
    throw "Package does not exist: $package"
}
if ([string]::IsNullOrWhiteSpace($ExpectedSha256)) {
    throw "ExpectedSha256 is required; refuse to install an unverified package."
}
if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$') {
    throw "ExpectedSha256 must be a 64-character SHA-256 value."
}

$actualHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
if (-not [string]::Equals($actualHash, $ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package SHA-256 does not match the expected value."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $archivePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $archive.Entries) {
        $archivePath = $item.FullName.TrimEnd('/', '\')
        if ($archivePath.Length -eq 0) { continue }
        $safeArchivePath = Get-SafeRelativePath $archivePath
        if (-not $archivePaths.Add($safeArchivePath)) {
            throw "Package contains a duplicate path: $safeArchivePath"
        }
    }
}
finally {
    $archive.Dispose()
}

$processName = [IO.Path]::GetFileNameWithoutExtension($entry)
if (Get-Process -Name $processName -ErrorAction SilentlyContinue) {
    throw "Close the running $processName process before installing."
}

$parent = Split-Path -Parent $root
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$stage = Join-Path $parent (".compositor-stage-" + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $parent (".compositor-backup-" + [Guid]::NewGuid().ToString('N'))
$movedOld = $false
$movedNew = $false

try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Expand-Archive -LiteralPath $package -DestinationPath $stage -Force
    $entryPath = Join-Path $stage $entry
    if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf)) {
        throw "Package does not contain the required entry point: $entry"
    }

    $manifestPath = Join-Path $stage $manifestName
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Package manifest is missing: $manifestName"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schema -ne "com.compositor.windows-portable" -or
        $manifest.schemaVersion -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$manifest.version) -or
        $manifest.runtimeIdentifier -ne "win-x64" -or
        $manifest.entryPoint -ne $entry) {
        throw "Package manifest is invalid or does not match the requested Windows package."
    }
    $manifestFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $manifestFiles.Add($manifestName) | Out-Null
    $fileRecords = @{}
    foreach ($record in @($manifest.files)) {
        $safe = Get-SafeRelativePath ([string]$record.path)
        if (-not $manifestFiles.Add($safe)) { throw "Package manifest contains a duplicate path: $safe" }
        $path = Join-Path $stage $safe
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Manifest file is missing: $safe" }
        $file = Get-Item -LiteralPath $path
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ([int64]$record.bytes -ne $file.Length -or
            -not [string]::Equals($record.sha256, $hash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Package manifest hash mismatch: $safe"
        }
        $fileRecords[$safe] = $record
    }
    if (-not $manifestFiles.Contains($entry)) {
        throw "Package entry point is not in the manifest file list: $entry"
    }
    $entryRecord = $fileRecords[$entry]
    if ($null -eq $entryRecord -or
        -not [string]::Equals($manifest.entryPointSha256, [string]$entryRecord.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package entry point hash is inconsistent with the manifest."
    }
    $nativeCount = 0
    $nativeNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($native in @($manifest.nativeLibraries)) {
        $safe = Get-SafeRelativePath ([string]$native.path)
        if (-not $safe.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -or
            -not $manifestFiles.Contains($safe) -or
            $native.architecture -ne 'x64' -or
            $native.required -ne $true) {
            throw "Package native library is not in the manifest file list: $safe"
        }
        $record = $fileRecords[$safe]
        if (-not $nativeNames.Add($safe) -or $null -eq $record -or
            $native.bytes -ne $record.bytes -or
            -not [string]::Equals($native.sha256, $record.sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Package native library hash is inconsistent: $safe"
        }
        $nativeCount++
    }
    if ($nativeCount -eq 0) {
        throw "Package manifest contains no native DLL."
    }
    $actualFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $safe = Get-SafeRelativePath ([IO.Path]::GetRelativePath($stage, $file.FullName))
        $actualFiles.Add($safe) | Out-Null
    }
    $unexpectedFiles = @($actualFiles | Where-Object { -not $manifestFiles.Contains($_) })
    if ($actualFiles.Count -ne $manifestFiles.Count -or $unexpectedFiles.Count -gt 0) {
        throw "Package contains files outside its manifest."
    }
    if ([int]$manifest.fileCount -ne $manifestFiles.Count) {
        throw "Package manifest file count is inconsistent."
    }

    if ($UserDataRoot) {
        New-Item -ItemType Directory -Path ([IO.Path]::GetFullPath($UserDataRoot)) -Force | Out-Null
    }
    if (Test-Path -LiteralPath $root) {
        Move-Item -LiteralPath $root -Destination $backup
        $movedOld = $true
    }
    Move-Item -LiteralPath $stage -Destination $root
    $movedNew = $true
    if ($movedOld) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
    Write-Output (ConvertTo-Json @{ status = "installed"; installRoot = $root; packageSha256 = $actualHash })
}
catch {
    if ($movedNew -and (Test-Path -LiteralPath $root)) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
    if ($movedOld -and (Test-Path -LiteralPath $backup)) {
        Move-Item -LiteralPath $backup -Destination $root
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
