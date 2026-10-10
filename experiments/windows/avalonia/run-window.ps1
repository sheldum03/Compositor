param([string]$Root = 'C:\Users\Administrator\Desktop\CompositorTest')
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $Root 'pinvoke-test\runtime\dotnet.exe'
$native = Join-Path $Root 'native-run-20260921-111947\compositor_native.dll'
foreach ($required in @($runtime, $native)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing input: $required" }
}
if ((Get-FileHash $native -Algorithm SHA256).Hash.ToLowerInvariant() -ne
    '046ad66d8da01625985f29f59fd5b39035adc0483dc43eb141d678b4e931320f') {
    throw 'Native DLL differs from the verified Windows run.'
}
foreach ($file in (Get-Content (Join-Path $PSScriptRoot 'package-manifest.json') -Raw | ConvertFrom-Json)) {
    if ((Get-FileHash (Join-Path $PSScriptRoot $file.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw "Package identity mismatch: $($file.path)"
    }
}
$session = Join-Path $Root ('window-run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $session | Out-Null
$results = Join-Path $session 'results'
Write-Host 'Test the three tabs, then close the window to collect results.'
$ErrorActionPreference = 'Continue'
$PSNativeCommandUseErrorActionPreference = $false
& $runtime (Join-Path $PSScriptRoot 'app\Compositor.AvaloniaProbe.dll') --window `
    (Join-Path $PSScriptRoot 'fixtures') $results $native 2>&1 |
    Out-File (Join-Path $session 'window-run.log') -Encoding utf8
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$report = $null
if (Test-Path (Join-Path $results 'window-report.json')) {
    $report = Get-Content (Join-Path $results 'window-report.json') -Raw | ConvertFrom-Json
}
$summary = [pscustomobject]@{
    ExitCode = $code
    WindowsExecuted = $report.windowsExecuted
    NativeWindow = $report.nativeWindow
    NativePreeditChanges = $report.nativePreeditChanges
    TextInputEvents = $report.textInputEvents
    TextExports = @($report.events | Where-Object { $_.name -eq 'text-preview-export' }).Count
    BrushSaves = @($report.events | Where-Object { $_.name -eq 'brush-save-reopen' }).Count
    CompositionSaves = @($report.events | Where-Object { $_.name -eq 'composition-save-reopen' }).Count
    ActionErrors = @($report.events | Where-Object { $_.name -eq 'action-error' }).Count
    Scope = 'Native window observations; IME candidate placement, focus, DPI and visual acceptance require manual review'
}
$summary | ConvertTo-Json | Set-Content (Join-Path $session 'summary.json') -Encoding utf8
$items = @(Get-ChildItem -LiteralPath $session -File | Select-Object -ExpandProperty FullName)
if (Test-Path -LiteralPath $results) { $items += $results }
$zip = "$session.zip"
Compress-Archive -LiteralPath $items -DestinationPath $zip
$summary | Format-List
Write-Host "Send this archive: $zip"
if ($code -ne 0 -or $summary.WindowsExecuted -ne $true -or $summary.NativeWindow -ne $true -or
    $summary.TextExports -lt 1 -or $summary.BrushSaves -lt 1 -or $summary.CompositionSaves -lt 1 -or
    $summary.ActionErrors -ne 0) { exit 1 }
exit 0
