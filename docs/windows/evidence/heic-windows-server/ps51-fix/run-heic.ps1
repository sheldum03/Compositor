$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false
$root = $PSScriptRoot
$run = Join-Path $root ('heic-run-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $run | Out-Null
$exe = Join-Path $root 'app\heic_probe.exe'
$results = @(); $failures = @(); $invocations = @(); $errorText = $null; $passed = $false
function Hash-File([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Invoke-Probe([string]$InputPath, [string]$Destination, [string]$Budget = '') {
    $probeArgs = @($InputPath, $Destination)
    if ($Budget) { $probeArgs += $Budget }
    $log = Join-Path $run ('native-' + $script:invocations.Count + '.log')
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $exe @probeArgs > $log 2>&1; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $oldPreference }
    $script:invocations += @{input=$InputPath; destination=$Destination; budget=$Budget; exitCode=$code; log=$log}
    return $code
}
try {
    if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or -not [Environment]::Is64BitProcess) { throw 'Windows x64 PowerShell required' }
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'files.json') | ConvertFrom-Json
    foreach ($entry in $manifest) {
        $path = Join-Path $root $entry.path
        if ((Get-Item -LiteralPath $path).Length -ne $entry.bytes -or (Hash-File $path) -ne $entry.sha256) { throw ('Package identity mismatch: ' + $entry.path) }
    }
    $env:LIBHEIF_PLUGIN_PATH = Join-Path $run 'nonexistent-plugins'
    $cases = Get-Content -Raw -LiteralPath (Join-Path $root 'fixtures\cases.json') | ConvertFrom-Json
    if ($cases.Count -ne 16) { throw 'Expected 16 frozen cases' }
    foreach ($case in $cases) {
        $destination = Join-Path $run $case.name
        $code = Invoke-Probe (Join-Path $root ('fixtures\' + $case.name + '.heic')) $destination
        if ($code -ne 0) { throw ('Decode failed: ' + $case.name + ' exit=' + $code) }
        $report = Get-Content -Raw -LiteralPath (Join-Path $destination 'decode.json') | ConvertFrom-Json
        if ($report.width -ne $case.referenceWidth -or $report.height -ne $case.referenceHeight -or $report.hasAlpha -ne $case.macHasAlpha -or $report.premultiplied) { throw ('Dimension/alpha contract: ' + $case.name) }
        $rgba = [IO.File]::ReadAllBytes((Join-Path $destination 'rgba.bin'))
        $reference = [IO.File]::ReadAllBytes((Join-Path $root ('reference-rgba\' + $case.name + '.bin')))
        if ($rgba.Length -ne $reference.Length -or $rgba.Length -ne 4 * $report.width * $report.height) { throw 'RGBA buffer length' }
        $different = 0; $maximum = 0; $sum = [long]0
        for ($offset = 0; $offset -lt $rgba.Length; $offset += 4) {
            if ($rgba[$offset+3] -ne $reference[$offset+3]) { throw ('Alpha mismatch: ' + $case.name) }
            $changed = $false
            for ($channel = 0; $channel -lt 3; $channel++) {
                $a = [Math]::Floor(($rgba[$offset+$channel] * [int]$rgba[$offset+3] + 127) / 255)
                $b = [Math]::Floor(($reference[$offset+$channel] * [int]$reference[$offset+3] + 127) / 255)
                $delta = [Math]::Abs($a - $b); $sum += $delta
                if ($delta -gt $maximum) { $maximum = $delta }
                if ($delta -ne 0) { $changed = $true }
            }
            if ($changed) { $different++ }
        }
        $results += @{case=$case.name; decode=$report; rgbaSha256=(Hash-File (Join-Path $destination 'rgba.bin')); comparison=@{differentPixels=$different; maximumChannelError=$maximum; maximumAlphaError=0; meanAbsoluteChannelError=$sum/[double]$rgba.Length}}
    }
    $unicodeRoot = Join-Path $run '路径 空格 🧪'; New-Item -ItemType Directory -Path $unicodeRoot | Out-Null
    $unicodeInput = Join-Path $unicodeRoot '方向与透明度 🌈.heic'; $unicodeOutput = Join-Path $unicodeRoot '解码 结果 🚀'
    Copy-Item -LiteralPath (Join-Path $root 'fixtures\orientation-6-alpha.heic') -Destination $unicodeInput
    if ((Invoke-Probe $unicodeInput $unicodeOutput '100000000') -ne 0) { throw 'Unicode input/output decode failed' }
    $expected = Hash-File (Join-Path $run 'orientation-6-alpha\rgba.bin')
    if ((Hash-File (Join-Path $unicodeOutput 'rgba.bin')) -ne $expected) { throw 'Unicode decoded pixels changed' }
    if ((Invoke-Probe $unicodeInput $unicodeOutput) -eq 0 -or (Hash-File (Join-Path $unicodeOutput 'rgba.bin')) -ne $expected) { throw 'Existing Unicode output not preserved' }
    if ((Hash-File $unicodeInput) -ne (Hash-File (Join-Path $root 'fixtures\orientation-6-alpha.heic'))) { throw 'Unicode source changed' }
    $truncated = Join-Path $run 'truncated.heic'; $inputBytes = [IO.File]::ReadAllBytes((Join-Path $root 'fixtures\orientation-1-opaque.heic'))
    [IO.File]::WriteAllBytes($truncated, [byte[]]$inputBytes[0..63])
    $negativeCases = @(
        @{name='truncated'; input=$truncated; budget=''},
        @{name='wrong-format'; input=(Join-Path $root 'fixtures\orientation-1-opaque-mac.png'); budget=''},
        @{name='missing'; input=(Join-Path $run 'missing.heic'); budget=''},
        @{name='budget'; input=(Join-Path $root 'fixtures\orientation-1-opaque.heic'); budget='100'}
    )
    foreach ($case in $negativeCases) {
        $destination = Join-Path $run ($case.name + '-rejected'); $code = Invoke-Probe $case.input $destination $case.budget
        if ($code -eq 0 -or (Test-Path -LiteralPath $destination)) { throw ('Negative case failed: ' + $case.name) }
        $failures += @{case=$case.name; exitCode=$code; noOutputPublished=$true}
    }
    $old = Hash-File (Join-Path $run 'orientation-1-opaque\rgba.bin')
    if ((Invoke-Probe (Join-Path $root 'fixtures\orientation-1-opaque.heic') (Join-Path $run 'orientation-1-opaque')) -eq 0 -or (Hash-File (Join-Path $run 'orientation-1-opaque\rgba.bin')) -ne $old) { throw 'Existing ASCII output not preserved' }
    foreach ($entry in $manifest) { if ((Hash-File (Join-Path $root $entry.path)) -ne $entry.sha256) { throw 'Package/source changed after execution' } }
    $passed = $true
} catch { $errorText = $_.ToString() }
$summary = @{completed=$passed; error=$errorText; windowsExecuted=([Environment]::OSVersion.Platform -eq 'Win32NT'); os=[Environment]::OSVersion.VersionString; powershell=$PSVersionTable.PSVersion.ToString(); results=$results; failureChecks=$failures; invocations=$invocations; nativeImeAccepted=$false; productAccepted=$false; imageToleranceAccepted=$false; generalIccAndHdrValidated=$false; source='same frozen 16 HEIC/PNG cases; independent Mac-decoded RGBA references'}
$summary | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $run 'summary.json')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = $run + '.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($run, $archive)
@{completed=$passed; result=$archive; bytes=(Get-Item -LiteralPath $archive).Length; sha256=(Hash-File $archive); error=$errorText} | ConvertTo-Json
if (-not $passed) { exit 1 }
exit 0
