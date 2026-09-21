param([string]$Root = 'C:\CompositorValidation\qt-cbc28f5\CompositorQtProbe')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false
if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Windows required' }
$Root = (Resolve-Path -LiteralPath $Root).Path
if ((Get-FileHash -LiteralPath (Join-Path $Root 'files.json') -Algorithm SHA256).Hash -ne '26a9fef349d3a109f13bcb339e42d01722e35868591fee453664d0041380d7af') { throw 'Expected original cbc28f5 package manifest' }
$manifest = Get-Content -LiteralPath (Join-Path $Root 'files.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $file = Join-Path $Root $entry.path
    if ((Get-Item -LiteralPath $file).Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) { throw ('Package identity mismatch: ' + $entry.path) }
}
$output = Join-Path $Root ('font-diagnostic-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $output | Out-Null
$fontDirectory = Join-Path ([Environment]::GetFolderPath('Windows')) 'Fonts'
$inventory = @()
foreach ($name in @('seguiemj.ttf', 'segoeui.ttf')) {
    $file = Join-Path $fontDirectory $name
    $item = @{name=$name; present=(Test-Path -LiteralPath $file)}
    if ($item.present) { $item.bytes=(Get-Item -LiteralPath $file).Length; $item.sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
    $inventory += $item
}
$variables = @('QT_QPA_PLATFORM','QT_PLUGIN_PATH','QT_SCALE_FACTOR','QT_QPA_FONTDIR')
$previous = @{}
foreach ($name in $variables) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$results = @()
try {
    $env:QT_QPA_PLATFORM = 'offscreen'
    $env:QT_PLUGIN_PATH = Join-Path $Root 'app'
    $env:QT_SCALE_FACTOR = '1'
    foreach ($case in @('default-fontdir', 'system-fontdir')) {
        $directory = $null
        if ($case -eq 'system-fontdir') { $directory = $fontDirectory }
        [Environment]::SetEnvironmentVariable('QT_QPA_FONTDIR', $directory, 'Process')
        $code = -1; $errorText = $null; $complete = $false; $fonts = @()
        $clock = [Diagnostics.Stopwatch]::StartNew()
        try {
            $exe = Join-Path $Root 'app\qt_probe.exe'
            $fixtures = Join-Path $Root 'fixtures\extended'
            $destination = Join-Path $output $case
            $ErrorActionPreference = 'Continue'
            try { & $exe --text $fixtures $destination *> (Join-Path $output ($case + '.log')); $code = $LASTEXITCODE }
            finally { $ErrorActionPreference = 'Stop' }
            $report = Get-Content -LiteralPath (Join-Path $destination 'text-report.json') -Raw | ConvertFrom-Json
            $complete = $report.windowsExecuted -eq $true -and @($report.results).Count -eq 12
            $fonts = @($report.results | ForEach-Object { $_.resolvedFonts } | Sort-Object -Unique)
        } catch { $complete = $false; $errorText = $_.ToString() }
        finally { $clock.Stop() }
        $results += @{case=$case; fontDirectory=$directory; exitCode=$code; reportComplete=$complete; resolvedFonts=$fonts; elapsedSeconds=$clock.Elapsed.TotalSeconds; error=$errorText}
    }
} finally {
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
$unchanged = $true
foreach ($entry in $manifest) {
    if ((Get-FileHash -LiteralPath (Join-Path $Root $entry.path) -Algorithm SHA256).Hash -ne $entry.sha256) { $unchanged = $false }
}
$completed = $results.Count -eq 2 -and @($results | Where-Object { $_.exitCode -ne 0 -or -not $_.reportComplete }).Count -eq 0 -and $unchanged
@{completed=$completed; windowsExecuted=$true; os=[Environment]::OSVersion.VersionString; powershell=$PSVersionTable.PSVersion.ToString(); packageUnchanged=$unchanged; packageManifestSha256=(Get-FileHash -LiteralPath (Join-Path $Root 'files.json') -Algorithm SHA256).Hash; fonts=$inventory; cases=$results; visualInspectionRequired=$true; productAccepted=$false; nativeImeExecuted=$false; nativeWindowsFontDatabaseTested=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = $output + '.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($output, $archive)
@{completed=$completed; result=$archive; bytes=(Get-Item -LiteralPath $archive).Length; sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash} | ConvertTo-Json
if (-not $completed) { exit 1 }
