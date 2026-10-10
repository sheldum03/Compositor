@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $root='C:\Users\Administrator\Desktop\CompositorTest'; $zip=Join-Path $root 'CompositorWindowTest.zip'; if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne '63d489a89a1899a59cf238ae8bb6f90cd4f492c6d4023f30846ccb379137b163') { throw 'Window package hash mismatch' }; Write-Host 'Window ZIP SHA256 verified'; $folder=Join-Path $root 'window-test'; if (Test-Path -LiteralPath $folder) { throw 'window-test already exists; inspect before reuse' }; Expand-Archive -LiteralPath $zip -DestinationPath $root; & (Join-Path $folder 'run-window.ps1')"
echo Wrapper exit code: %ERRORLEVEL%
pause
