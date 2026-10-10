param([string]$Root = 'C:\Users\Administrator\Desktop\CompositorTest')
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $Root 'pinvoke-test\runtime\dotnet.exe'
$installedApp = Join-Path $Root 'render-test\app'
$fixtures = Join-Path $Root 'render-test\fixtures\extended'
$native = Join-Path $Root 'native-run-20260921-111947\compositor_native.dll'
foreach ($required in @($runtime, $installedApp, $fixtures, $native)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing input: $required" }
}
if ((Get-FileHash $native -Algorithm SHA256).Hash.ToLowerInvariant() -ne
    '046ad66d8da01625985f29f59fd5b39035adc0483dc43eb141d678b4e931320f') {
    throw 'Native DLL differs from the verified Windows run.'
}
foreach ($file in (Get-Content (Join-Path $PSScriptRoot 'patch-manifest.json') -Raw | ConvertFrom-Json)) {
    if ((Get-FileHash (Join-Path $PSScriptRoot $file.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw "Patch identity mismatch: $($file.path)"
    }
}
$session = Join-Path $Root ('text-fix-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $session | Out-Null
$app = Join-Path $session 'app'
Copy-Item -LiteralPath $installedApp -Destination $app -Recurse
Copy-Item -Path (Join-Path $PSScriptRoot 'patch\*') -Destination $app -Force
$results = Join-Path $session 'results'
$ErrorActionPreference = 'Continue'
$PSNativeCommandUseErrorActionPreference = $false
& $runtime (Join-Path $app 'Compositor.AvaloniaProbe.dll') --text $fixtures $results $native 2>&1 |
    Out-File (Join-Path $session 'text-run.log') -Encoding utf8
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$report = $null
$ink = $null
if (Test-Path (Join-Path $results 'text-report.json')) {
    $report = Get-Content (Join-Path $results 'text-report.json') -Raw | ConvertFrom-Json
}
if (Test-Path (Join-Path $results 'ordinary-ink.json')) {
    $ink = Get-Content (Join-Path $results 'ordinary-ink.json') -Raw | ConvertFrom-Json
}
$summary = [pscustomobject]@{
    ExitCode = $code
    WindowsExecuted = $report.windowsExecuted
    NativeImeExecuted = $report.nativeImeExecuted
    SampleCount = if ($null -ne $report) { @($report.results).Count } else { $null }
    OrdinaryInkStatus = $ink.status
    OrdinaryInkPassed = if ($null -ne $ink) { @($ink.results | Where-Object { $_.Passed }).Count } else { $null }
    PreviewExportMismatches = if ($null -ne $report) { @($report.results | Where-Object { $_.previewExport.DifferentPixels -ne 0 }).Count } else { $null }
    Scope = 'Grayscale text coverage fix; visual review and native IME acceptance remain separate'
}
$summary | ConvertTo-Json | Set-Content (Join-Path $session 'summary.json') -Encoding utf8
$items = @(Get-ChildItem -LiteralPath $session -File | Select-Object -ExpandProperty FullName)
if (Test-Path -LiteralPath $results) { $items += $results }
$zip = "$session.zip"
Compress-Archive -LiteralPath $items -DestinationPath $zip
$summary | Format-List
Write-Host "Send this archive: $zip"

if ($code -ne 0 -or $summary.WindowsExecuted -ne $true -or $summary.SampleCount -ne 12 -or
    $summary.OrdinaryInkStatus -ne 'passed' -or $summary.OrdinaryInkPassed -ne 12 -or
    $summary.PreviewExportMismatches -ne 0) { exit 1 }
exit 0
