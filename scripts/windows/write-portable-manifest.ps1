[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Commit = "",

    [string]$RuntimeIdentifier = "win-x64",

    [string]$TargetFramework = "net10.0",

    [string]$DotnetSdk = "10.0.401",

    [string]$EntryPoint = "Compositor.App.exe",

    [Parameter(Mandatory = $true)]
    [string[]]$NativeDll,

    [string]$OutputPath = "Compositor.Portable.json"
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

$root = [IO.Path]::GetFullPath($PackageRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Package root does not exist: $root"
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is required."
}
$entry = Get-SafeRelativePath $EntryPoint
$manifest = Get-SafeRelativePath $OutputPath
$output = Join-Path $root $manifest
if (Test-Path -LiteralPath $output) {
    throw "Refusing to overwrite an existing package manifest: $output"
}

function Get-FileRecord([string]$RelativePath) {
    $safe = Get-SafeRelativePath $RelativePath
    $path = Join-Path $root $safe
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Package file is missing: $safe"
    }
    $file = Get-Item -LiteralPath $path
    [ordered]@{
        path = $safe
        bytes = [int64]$file.Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$entryRecord = Get-FileRecord $entry
$nativeNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$nativeRecords = @(
    foreach ($native in $NativeDll) {
        $safe = Get-SafeRelativePath $native
        if (-not $safe.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Native library must be a DLL: $safe"
        }
        if (-not $nativeNames.Add($safe)) {
            throw "Native library is listed more than once: $safe"
        }
        $record = Get-FileRecord $safe
        $record["architecture"] = "x64"
        $record["required"] = $true
        $record
    }
)
if ($nativeRecords.Count -eq 0) {
    throw "At least one native DLL is required."
}

$files = @(
    Get-ChildItem -LiteralPath $root -Recurse -File |
        ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
            if ($relative -ne $manifest) { Get-FileRecord $relative }
        } |
        Sort-Object { $_["path"] }
)

$document = [ordered]@{
    schema = "com.compositor.windows-portable"
    schemaVersion = 1
    product = "Compositor"
    version = $Version
    commit = $Commit
    runtimeIdentifier = $RuntimeIdentifier
    targetFramework = $TargetFramework
    dotnetSdk = $DotnetSdk
    entryPoint = $entry
    entryPointSha256 = $entryRecord.sha256
    nativeLibraries = $nativeRecords
    fileCount = $files.Count + 1
    files = $files
}

$temporary = "$output.tmp-$([Guid]::NewGuid().ToString('N'))"
try {
    $document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporary -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporary -Destination $output
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
}

Write-Output (ConvertTo-Json @{ status = "written"; path = $output; version = $Version; fileCount = $document.fileCount })
