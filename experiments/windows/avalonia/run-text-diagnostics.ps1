param(
    [string]$Root = 'C:\Users\Administrator\Desktop\CompositorTest'
)
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $Root 'pinvoke-test\runtime\dotnet.exe'
$installedApp = Join-Path $Root 'render-test\app'
$fixtures = Join-Path $Root 'render-test\fixtures\extended'
$native = Join-Path $Root 'native-run-20260921-111947\compositor_native.dll'
$oldOutput = Join-Path $Root 'text-run-20260921-114831'
foreach ($required in @($runtime, $installedApp, $fixtures, $native, $oldOutput)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing input: $required" }
}
if ((Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash.ToLowerInvariant() -ne
    '046ad66d8da01625985f29f59fd5b39035adc0483dc43eb141d678b4e931320f') {
    throw 'Native DLL identity differs from the received Windows test.'
}
$manifest = Get-Content (Join-Path $PSScriptRoot 'patch-manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $manifest) {
    $path = Join-Path $PSScriptRoot $file.path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw "Diagnostic patch hash mismatch: $path"
    }
}
$session = Join-Path $Root ('text-diagnostic-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $session | Out-Null
$app = Join-Path $session 'app'
Copy-Item -LiteralPath $installedApp -Destination $app -Recurse
Copy-Item -Path (Join-Path $PSScriptRoot 'patch\*') -Destination $app -Force
$probe = Join-Path $app 'Compositor.AvaloniaProbe.dll'
$results = Join-Path $session 'results'

# Both failures must remain observable, and must not prevent collecting images.
$ErrorActionPreference = 'Continue'
$PSNativeCommandUseErrorActionPreference = $false
& $runtime $probe --verify-text-output $fixtures $oldOutput $native 2>&1 |
    Out-File (Join-Path $session 'old-output-check.log') -Encoding utf8
$oldExit = $LASTEXITCODE
& $runtime $probe --text-diagnostics $fixtures $results $native 2>&1 |
    Out-File (Join-Path $session 'diagnostic-run.log') -Encoding utf8
$diagnosticExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'

$diagnosticReport = Join-Path $results 'glyph-diagnostics.json'
$inkReport = Join-Path $results 'ordinary-ink.json'
$summary = [pscustomobject]@{
    OldFailureDetected = ($null -ne $oldExit -and $oldExit -ne 0)
    OldCheckExitCode = $oldExit
    DiagnosticExitCode = $diagnosticExit
    GlyphReportWritten = (Test-Path -LiteralPath $diagnosticReport)
    InkReportWritten = (Test-Path -LiteralPath $inkReport)
    Scope = 'Diagnostic observations; no verified Windows fix or IME acceptance'
}
$summary | ConvertTo-Json | Set-Content (Join-Path $session 'summary.json') -Encoding utf8
$items = @(Get-ChildItem -LiteralPath $session -File | Select-Object -ExpandProperty FullName)
if (Test-Path -LiteralPath $results) { $items += $results }
$zip = "$session.zip"
Compress-Archive -LiteralPath $items -DestinationPath $zip
$summary | Format-List
Write-Host "Send this archive: $zip"
