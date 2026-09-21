param(
    [Parameter(Mandatory=$true)][string]$ToolchainBin,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This diagnostic requires actual Windows' }
if (Test-Path $OutputDirectory) { throw 'Output directory already exists' }
$ToolchainBin = (Resolve-Path $ToolchainBin).Path
$rendering = (Resolve-Path "$PSScriptRoot/../../../Compositor/Rendering").Path
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
(Get-Process -Id $PID).PriorityClass = 'BelowNormal'
$originalPath = $env:PATH
$env:PATH = "$ToolchainBin;$originalPath"
function Invoke-Logged([string]$Program, [string[]]$Arguments, [string]$Log) {
    & $Program @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) { throw "$Program failed ($LASTEXITCODE); see $Log" }
}
try {
    Invoke-Logged "$ToolchainBin/clang.exe" @('--version') "$OutputDirectory/compiler.txt"
    $sources = @('AdjustPixels','BrushPixels','ContentFill','HealPixels',
        'LensPixels','LevelsPixels','NoisePixels','WandPixels') | ForEach-Object { "$rendering/$_.c" }
    $sources += "$PSScriptRoot/bridge.c"
    $objects = @()
    foreach ($source in $sources) {
        $name = [IO.Path]::GetFileNameWithoutExtension($source)
        $object = "$OutputDirectory/$name.o"
        Invoke-Logged "$ToolchainBin/clang.exe" @('-std=c17','-O2','-D_USE_MATH_DEFINES',
            "-I$PSScriptRoot","-I$rendering",'-c',$source,'-o',$object) "$OutputDirectory/$name.log"
        $objects += $object
    }
    $dll = "$OutputDirectory/compositor_native.dll"
    Invoke-Logged "$ToolchainBin/clang.exe" (@('-shared') + $objects + @(
        '-Wl,--export-all-symbols',"-Wl,--out-implib,$OutputDirectory/libcompositor_native.dll.a",
        '-o',$dll)) "$OutputDirectory/link.log"
    Invoke-Logged "$ToolchainBin/clang++.exe" @('-std=c++17','-O2',
        "$PSScriptRoot/contract_tests.cpp","-I$PSScriptRoot","-I$rendering",
        "-L$OutputDirectory",'-lcompositor_native','-o',"$OutputDirectory/native_contract_tests.exe") "$OutputDirectory/contract-build.log"
    Invoke-Logged "$OutputDirectory/native_contract_tests.exe" @() "$OutputDirectory/contract.txt"
    Invoke-Logged 'python' @("$PSScriptRoot/ffi_smoke.py",$dll) "$OutputDirectory/ctypes.json"
    Invoke-Logged "$ToolchainBin/llvm-readobj.exe" @('--coff-exports','--coff-imports',$dll) "$OutputDirectory/dll-symbols.txt"
    $exports = @(Select-String -Path "$OutputDirectory/dll-symbols.txt" -Pattern '^Export \{').Count
    if ($exports -ne 19) { throw "Expected 19 C exports, got $exports" }
    $ffi = Get-Content "$OutputDirectory/ctypes.json" -Raw | ConvertFrom-Json
    if ($ffi.system -ne 'Windows' -or $ffi.longBytes -ne 4 -or $ffi.pointerBytes -ne 8) {
        throw 'Unexpected actual ctypes ABI'
    }
    [ordered]@{
        status = 'passed'; windowsExecuted = $true; toolchain = 'LLVM-MinGW (not MSVC)'
        utc = [DateTime]::UtcNow.ToString('o'); exports = $exports
        dllSha256 = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant()
        contract = (Get-Content "$OutputDirectory/contract.txt" -Raw).Trim()
        ctypes = $ffi
    } | ConvertTo-Json -Depth 4 | Set-Content "$OutputDirectory/result.json" -Encoding UTF8
    Get-Content "$OutputDirectory/contract.txt"
    Get-Content "$OutputDirectory/ctypes.json"
} finally {
    $env:PATH = $originalPath
}
