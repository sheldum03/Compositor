[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$InstallRoot,

    [string]$ExpectedSha256,

    [string]$EntryPoint = "Compositor.App.exe",

    [string]$UserDataRoot
)

$ErrorActionPreference = "Stop"

function Get-SafeRelativePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) {
        throw "Path must be a non-empty relative path: $Path"
    }
    $normalized = $Path.Replace('\', '/')
    if ($normalized.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }) {
        throw "Path contains an unsafe segment: $Path"
    }
    return $normalized
}

$package = [IO.Path]::GetFullPath($PackagePath)
$root = [IO.Path]::GetFullPath($InstallRoot)
$entry = Get-SafeRelativePath $EntryPoint
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
