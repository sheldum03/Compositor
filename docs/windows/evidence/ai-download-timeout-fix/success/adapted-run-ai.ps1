param([string]$ModelPath = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false
$root = $PSScriptRoot
$run = Join-Path $root ('ai-run-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$out = Join-Path $run 'results'; $inputs = Join-Path $run 'inputs'
New-Item -ItemType Directory -Path $out, $inputs | Out-Null
$exe = Join-Path $root 'app\ai_probe.exe'
$invocations = @(); $failures = @(); $runtimeFiles = @(); $errorText = $null; $passed = $false; $native = $null; $unicode = $null; $download = $null
$modelHash = '309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8'
function Hash-File([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Invoke-Probe([string]$Model, [string]$Tensor, [string]$Destination) {
    $log = Join-Path $out ('native-' + $script:invocations.Count + '.log')
    $oldPreference = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & $exe $Model $Tensor $Destination > $log 2>&1; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $oldPreference }
    $script:invocations += @{model=$Model; tensor=$Tensor; destination=$Destination; exitCode=$code; log=$log}
    return $code
}
function Read-Inference([string]$Directory) {
    $report = Get-Content -Raw -LiteralPath (Join-Path $Directory 'inference.json') | ConvertFrom-Json
    if ($report.onnxruntime -ne '1.30.0' -or -not $report.repeatedPredictionsExact -or -not $report.preTerminatedRunRejected -or -not $report.sessionRecovered -or $report.activeInferenceCancellationTested -or @($report.inferenceMilliseconds).Count -ne 3) { throw 'Inference report contract' }
    $profiles = @(Get-ChildItem -LiteralPath $Directory -Filter 'cpu-profile*.json')
    if ($profiles.Count -ne 1) { throw 'One native profile required' }
    $events = Get-Content -Raw -LiteralPath $profiles[0].FullName | ConvertFrom-Json
    $providers = @{}
    foreach ($event in $events) {
        if ($event.cat -eq 'Node' -and $event.args.PSObject.Properties['provider']) {
            $name = [string]$event.args.provider
            if (-not $providers.ContainsKey($name)) { $providers[$name] = 0 }
            $providers[$name]++
        }
    }
    if ($providers.Count -ne 1 -or -not $providers.ContainsKey('CPUExecutionProvider') -or $providers['CPUExecutionProvider'] -lt 1) { throw 'Actual CPU kernel profile required' }
    $mask = Join-Path $Directory 'mask.f32'; $bytes = [IO.File]::ReadAllBytes($mask)
    if ($bytes.Length -ne 409600 -or -not [BitConverter]::IsLittleEndian) { throw 'Mask f32le shape' }
    $minimum = [double]1; $maximum = [double]0
    for ($offset = 0; $offset -lt $bytes.Length; $offset += 4) {
        $value = [BitConverter]::ToSingle($bytes, $offset)
        if ([single]::IsNaN($value) -or [single]::IsInfinity($value) -or $value -lt 0 -or $value -gt 1) { throw 'Finite sigmoid mask required' }
        if ($value -lt $minimum) { $minimum = $value }; if ($value -gt $maximum) { $maximum = $value }
    }
    if ($maximum - $minimum -le .5) { throw 'Nonconstant foreground mask required' }
    return @{inference=$report; providers=$providers; rawSha256=(Hash-File $mask); minimum=$minimum; maximum=$maximum}
}
try {
    if ([Environment]::OSVersion.Platform -ne 'Unix') { throw 'Mac-only adapted wrapper test' }
    foreach ($name in @('MSVCP140.dll','MSVCP140_1.dll','VCRUNTIME140.dll','VCRUNTIME140_1.dll')) {
        $path = Join-Path '/tmp/compositor-ai-mac-runtime-not-applicable' $name
        $record = @{name=$name; checkedLocation=$path; found=(Test-Path -LiteralPath $path)}
        if ($record.found) { $record['sha256'] = Hash-File $path; $record['version'] = (Get-Item -LiteralPath $path).VersionInfo.FileVersion }
        $runtimeFiles += $record
    }
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'files.json') | ConvertFrom-Json
    foreach ($entry in $manifest) {
        $path = Join-Path $root $entry.path
        if ((Get-Item -LiteralPath $path).Length -ne $entry.bytes -or (Hash-File $path) -ne $entry.sha256) { throw ('Package identity mismatch: ' + $entry.path) }
    }
    if (-not $ModelPath) {
        $ModelPath = Join-Path $inputs 'u2netp.onnx'; $part = $ModelPath + '.part'
        $curl = '/usr/bin/curl'
        if (-not (Test-Path -LiteralPath $curl)) { throw 'Windows system curl.exe required for bounded model download; alternatively pass -ModelPath' }
        $download = @{maximumSeconds=120; exitCode=$null; elapsedSeconds=$null; receivedBytes=0}
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $oldPreference = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        try {
            & $curl -q --fail --location --silent --show-error --connect-timeout 20 --max-time 120 --output $part 'http://127.0.0.1:50800/model' > (Join-Path $out 'download.log') 2>&1
            $download.exitCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $oldPreference; $clock.Stop(); $download.elapsedSeconds = $clock.Elapsed.TotalSeconds
            if (Test-Path -LiteralPath $part) { $download.receivedBytes = (Get-Item -LiteralPath $part).Length }
        }
        if ($download.exitCode -ne 0) { throw ('Model download failed: curl exit=' + $download.exitCode + '; inspect download.log') }
        if ((Get-Item -LiteralPath $part).Length -ne 4574861 -or (Hash-File $part) -ne $modelHash) { throw 'Downloaded model identity mismatch' }
        Move-Item -LiteralPath $part -Destination $ModelPath
    }
    $ModelPath = (Resolve-Path -LiteralPath $ModelPath).Path
    if ((Get-Item -LiteralPath $ModelPath).Length -ne 4574861 -or (Hash-File $ModelPath) -ne $modelHash) { throw 'Local model identity mismatch' }
    $tensor = Join-Path $root 'fixtures\input.f32'; $destination = Join-Path $out 'native'
    $code = Invoke-Probe $ModelPath $tensor $destination
    if ($code -ne 0) { throw ('Native inference failed: exit=' + $code + '; inspect native log and runtimeFiles') }
    $native = Read-Inference $destination
    $unicodeInputs = Join-Path $inputs '路径 空格 🧪'; New-Item -ItemType Directory -Path $unicodeInputs | Out-Null
    $unicodeModel = Join-Path $unicodeInputs '模型 🧠.onnx'; $unicodeTensor = Join-Path $unicodeInputs '输入 张量.f32'
    $unicodeOutput = Join-Path $out '推理 结果 🚀'
    Copy-Item -LiteralPath $ModelPath -Destination $unicodeModel; Copy-Item -LiteralPath $tensor -Destination $unicodeTensor
    if ((Invoke-Probe $unicodeModel $unicodeTensor $unicodeOutput) -ne 0) { throw 'Unicode native inference failed' }
    $unicode = Read-Inference $unicodeOutput
    if ($native.rawSha256 -ne $unicode.rawSha256 -or $native.providers.CPUExecutionProvider -ne $unicode.providers.CPUExecutionProvider) { throw 'Unicode inference differs' }
    if ((Invoke-Probe $unicodeModel $unicodeTensor $unicodeOutput) -eq 0 -or (Hash-File (Join-Path $unicodeOutput 'mask.f32')) -ne $native.rawSha256) { throw 'Existing Unicode output not preserved' }
    if ((Hash-File $unicodeModel) -ne $modelHash -or (Hash-File $unicodeTensor) -ne (Hash-File $tensor)) { throw 'Unicode input changed' }
    $short = Join-Path $inputs 'short.f32'; [IO.File]::WriteAllBytes($short, [byte[]](1,2,3))
    $nan = Join-Path $inputs 'nan.f32'; $nanBytes = [IO.File]::ReadAllBytes($tensor); [BitConverter]::GetBytes([single]::NaN).CopyTo($nanBytes, 0); [IO.File]::WriteAllBytes($nan, $nanBytes)
    $invalid = Join-Path $inputs 'invalid.onnx'; [IO.File]::WriteAllText($invalid, 'not an ONNX graph')
    $negativeCases = @(
        @{name='short-input'; model=$ModelPath; tensor=$short}, @{name='nan-input'; model=$ModelPath; tensor=$nan},
        @{name='missing-model'; model=(Join-Path $inputs 'missing.onnx'); tensor=$tensor}, @{name='invalid-model'; model=$invalid; tensor=$tensor}
    )
    foreach ($case in $negativeCases) {
        $destination = Join-Path $out $case.name; $code = Invoke-Probe $case.model $case.tensor $destination
        if ($code -eq 0 -or (Test-Path -LiteralPath (Join-Path $destination 'mask.f32'))) { throw ('Negative case failed: ' + $case.name) }
        $failures += @{case=$case.name; exitCode=$code; noMaskPublished=$true}
    }
    if ((Invoke-Probe $ModelPath $tensor (Join-Path $out 'native')) -eq 0 -or (Hash-File (Join-Path $out 'native\mask.f32')) -ne $native.rawSha256) { throw 'Existing ASCII output not preserved' }
    foreach ($entry in $manifest) { if ((Hash-File (Join-Path $root $entry.path)) -ne $entry.sha256) { throw 'Package changed after execution' } }
    if ((Hash-File $ModelPath) -ne $modelHash) { throw 'Model source changed' }
    $passed = $true
} catch { $errorText = $_.ToString() }
$summary = @{completed=$passed; error=$errorText; windowsExecuted=([Environment]::OSVersion.Platform -eq 'Win32NT'); os=[Environment]::OSVersion.VersionString; powershell=$PSVersionTable.PSVersion.ToString(); native=$native; unicode=$unicode; failureChecks=$failures; invocations=$invocations; runtimeFiles=$runtimeFiles; download=$download; productAccepted=$false; modelQualityAccepted=$false; weightsRedistributionApproved=$false; activeInferenceCancellationTested=$false; preprocessing='frozen Mac-derived tensor, no Windows image preprocessing/postprocessing in this runner'}
$summary | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $out 'summary.json')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = $run + '.zip'; [IO.Compression.ZipFile]::CreateFromDirectory($out, $archive)
@{completed=$passed; result=$archive; bytes=(Get-Item -LiteralPath $archive).Length; sha256=(Hash-File $archive); error=$errorText} | ConvertTo-Json
if (-not $passed) { exit 1 }; exit 0
