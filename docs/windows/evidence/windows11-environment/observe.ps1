$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root ('host-observation-' + (Get-Date -Format yyyyMMdd-HHmmss) + '.json')
if (Test-Path $out) { throw 'Refusing to overwrite an observation' }
$info = [ordered]@{
 utc = [DateTime]::UtcNow.ToString('o')
 scope = 'Read-only post-run environment observation; does not prove identical conditions during previous measurements'
 os = Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber
 cpu = @(Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors)
 system = Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer,Model,TotalPhysicalMemory,HypervisorPresent
 video = @(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate)
 languages = @(Get-WinUserLanguageList | Select-Object LanguageTag,InputMethodTips)
 powerScheme = ((powercfg /getactivescheme) -join "`n")
}
$info | ConvertTo-Json -Depth 6 | Set-Content $out -Encoding UTF8
[pscustomobject]@{path=$out;bytes=(Get-Item $out).Length;sha256=(Get-FileHash $out).Hash} | ConvertTo-Json
