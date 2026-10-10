$ErrorActionPreference = 'Stop'
$archive = 'C:\CompositorValidation\CompositorQtProbe-cross-build.zip'
$expected = '9505207b2611da0dac226136a8b613f3b8b7551a7d567fbb8bf464352cba88c0'
if ((Get-Item -LiteralPath $archive).Length -ne 49731077 -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Test archive identity mismatch' }
$destination = 'C:\CompositorValidation\qt-cbc28f5'
if (Test-Path -LiteralPath $destination) { throw 'Destination exists; preserve prior evidence' }
Expand-Archive -LiteralPath $archive -DestinationPath $destination
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$destination\CompositorQtProbe\run-qt.ps1"
$code = $LASTEXITCODE
[pscustomobject]@{ UTC=(Get-Date).ToUniversalTime().ToString('o'); WrapperExit=$code; Destination=$destination; FreeGiB=[Math]::Round((Get-PSDrive C).Free/1GB,2) } | ConvertTo-Json -Compress
exit $code
