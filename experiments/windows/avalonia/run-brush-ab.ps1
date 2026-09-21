param([string]$Root = 'C:\Users\Administrator\Desktop\CompositorTest')
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $Root 'pinvoke-test\runtime\dotnet.exe'
$baselineApp = Join-Path $Root 'render-test\app'
$baseline = Join-Path $baselineApp 'Compositor.AvaloniaProbe.dll'
$native = Join-Path $Root 'native-run-20260921-111947\compositor_native.dll'
$fixtures = Join-Path $Root 'render-test\fixtures\brush'
foreach ($required in @($runtime, $baseline, $native, $fixtures)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing input: $required" }
}
if ((Get-FileHash $baseline -Algorithm SHA256).Hash.ToLowerInvariant() -ne
    'caa0bb2411539a8bf3885229a5a65f5242fb67d26da99d79a73660c65d068f7a') {
    throw 'Baseline differs from the originally delivered rendering probe.'
}
if ((Get-FileHash $native -Algorithm SHA256).Hash.ToLowerInvariant() -ne
    '046ad66d8da01625985f29f59fd5b39035adc0483dc43eb141d678b4e931320f') {
    throw 'Native DLL differs from the verified Windows run.'
}
foreach ($file in (Get-Content (Join-Path $PSScriptRoot 'patch-manifest.json') -Raw | ConvertFrom-Json)) {
    if ((Get-FileHash (Join-Path $PSScriptRoot $file.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw "Candidate identity mismatch: $($file.path)"
    }
}
$session = Join-Path $Root ('brush-ab-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $session | Out-Null
$candidateApp = Join-Path $session 'candidate-app'
Copy-Item -LiteralPath $baselineApp -Destination $candidateApp -Recurse
Copy-Item -Path (Join-Path $PSScriptRoot 'patch\*') -Destination $candidateApp -Force
$candidate = Join-Path $candidateApp 'Compositor.AvaloniaProbe.dll'
$resultsRoot = Join-Path $session 'results'
New-Item -ItemType Directory -Path $resultsRoot | Out-Null
$runs = @()
$rows = @()
$order = @('baseline', 'candidate', 'candidate', 'baseline')
for ($i = 0; $i -lt $order.Count; $i++) {
    $kind = $order[$i]
    $name = '{0}-{1}' -f ($i + 1), $kind
    $output = Join-Path $resultsRoot $name
    $probe = if ($kind -eq 'baseline') { $baseline } else { $candidate }
    Write-Host "Run $($i + 1)/4: $kind"
    $ErrorActionPreference = 'Continue'
    $PSNativeCommandUseErrorActionPreference = $false
    & $runtime $probe --brush $fixtures $output $native 2>&1 |
        Out-File (Join-Path $resultsRoot "$name.log") -Encoding utf8
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $reportFile = Join-Path $output 'brush-report.json'
    $complete = $code -eq 0 -and (Test-Path -LiteralPath $reportFile)
    $runs += [pscustomobject]@{ Name = $name; ExitCode = $code; Completed = $complete }
    if ($complete) {
        $report = Get-Content $reportFile -Raw | ConvertFrom-Json
        foreach ($stroke in $report.timings) {
            $rows += [pscustomobject]@{
                Run = $name; Stroke = $stroke.stroke; AppendP95 = $stroke.appendP95
                PreviewP95 = $stroke.previewP95; UpdateAndPreviewP95 = $stroke.updateAndPreviewP95
                CommitMilliseconds = $stroke.commitMilliseconds
            }
        }
    }
}
$identical = $false
$comparisons = @()
if (@($runs | Where-Object { -not $_.Completed }).Count -eq 0) {
    $reference = Join-Path $resultsRoot '1-baseline'
    $files = @(Get-ChildItem $reference -Recurse -File | Where-Object { $_.Extension -eq '.png' -or $_.Name -eq 'manifest.json' })
    foreach ($run in $runs | Select-Object -Skip 1) {
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($reference.Length + 1)
            $other = Join-Path (Join-Path $resultsRoot $run.Name) $relative
            $same = (Test-Path -LiteralPath $other) -and
                ((Get-FileHash $file.FullName -Algorithm SHA256).Hash -eq (Get-FileHash $other -Algorithm SHA256).Hash)
            $comparisons += [pscustomobject]@{ Run = $run.Name; File = $relative; Equal = $same }
        }
    }
    $identical = $files.Count -eq 9 -and @($comparisons | Where-Object { -not $_.Equal }).Count -eq 0
}
$summary = [pscustomobject]@{
    Scope = 'ABBA fresh-process observations; two 40%-opacity strokes; not S02 acceptance'
    ArtifactBytesEqual = $identical; Runs = $runs; Timings = $rows; Comparisons = $comparisons
}
$summary | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $resultsRoot 'ab-summary.json') -Encoding utf8
$zip = "$session.zip"
Compress-Archive -LiteralPath $resultsRoot -DestinationPath $zip
$rows | Format-Table -AutoSize
Write-Host "ArtifactBytesEqual: $identical"
Write-Host "Send this archive: $zip"
