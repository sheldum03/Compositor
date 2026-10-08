[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedSha256,

    [string]$InstallScript = "$PSScriptRoot/install-portable.ps1",

    [string]$EvidencePath
)

$ErrorActionPreference = "Stop"

$package = [IO.Path]::GetFullPath($PackagePath)
$installer = [IO.Path]::GetFullPath($InstallScript)
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Package does not exist: $package" }
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Installer does not exist: $installer" }
if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "ExpectedSha256 must be a SHA-256 value." }

$probeRoot = Join-Path ([IO.Path]::GetTempPath()) ("compositor-install-smoke-" + [Guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $probeRoot "app"
$userData = Join-Path $probeRoot "user-data"
$marker = Join-Path $userData "preserved.txt"
$report = [ordered]@{
    status = "failed"
    packageSha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    installRoot = $installRoot
    userDataPreserved = $false
    hashMismatchRejected = $false
}

try {
    New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null
    & $installer -PackagePath $package -InstallRoot $installRoot -ExpectedSha256 $ExpectedSha256 -UserDataRoot $userData | Out-Null
    if (-not (Test-Path -LiteralPath (Join-Path $installRoot "Compositor.Portable.json") -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $installRoot "Compositor.App.exe") -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $installRoot "compositor_native.dll") -PathType Leaf)) {
        throw "Installed package is missing its contract, entry point, or native DLL."
    }

    New-Item -ItemType Directory -Path $userData -Force | Out-Null
    Set-Content -LiteralPath $marker -Value "keep" -NoNewline
    & $installer -PackagePath $package -InstallRoot $installRoot -ExpectedSha256 $ExpectedSha256 -UserDataRoot $userData | Out-Null
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or
        (Get-Content -LiteralPath $marker -Raw) -ne 'keep') {
        throw "Installer replaced the user data directory."
    }
    $report.userDataPreserved = $true

    try {
        $badHash = ("0" * 64) -join ""
        & $installer -PackagePath $package -InstallRoot $installRoot -ExpectedSha256 $badHash -UserDataRoot $userData | Out-Null
    }
    catch {
        $report.hashMismatchRejected = $true
    }
    if (-not $report.hashMismatchRejected -or -not (Test-Path -LiteralPath (Join-Path $installRoot "Compositor.App.exe") -PathType Leaf)) {
        throw "Hash mismatch was not rejected without damaging the installed package."
    }

    $report.status = "passed"
    if ($EvidencePath) {
        $evidence = [IO.Path]::GetFullPath($EvidencePath)
        New-Item -ItemType Directory -Path (Split-Path -Parent $evidence) -Force | Out-Null
        $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $evidence -Encoding utf8NoBOM
    }
    Write-Output ($report | ConvertTo-Json)
}
finally {
    if (Test-Path -LiteralPath $probeRoot) { Remove-Item -LiteralPath $probeRoot -Recurse -Force }
}
