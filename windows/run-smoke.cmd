@echo off
setlocal
set "RESULT=1"
if not defined DOTNET_EXE set "DOTNET_EXE=dotnet"
cd /d "%~dp0"
set "OUT=%CD%\..\production-core-smoke-%RANDOM%"
if exist "%OUT%" (
  echo Output already exists: %OUT%
  goto done
)
"%DOTNET_EXE%" --version > "%OUT%.sdk.txt" 2>&1
if errorlevel 1 goto failed
findstr /x /c:"10.0.401" "%OUT%.sdk.txt" >nul
if errorlevel 1 goto failed
"%DOTNET_EXE%" build Compositor.Smoke\Compositor.Smoke.csproj -c Release > "%OUT%.build.log" 2>&1
if errorlevel 1 goto failed
"%DOTNET_EXE%" run --project Compositor.Smoke -c Release --no-build -- ..\docs\windows\fixtures "%OUT%" > "%OUT%.run.log" 2>&1
if errorlevel 1 goto failed
findstr /b /c:"PASS: edit," "%OUT%.run.log" >nul
if errorlevel 1 goto failed
if not exist "%OUT%\Edited.comp\manifest.json" goto failed
if not exist "%OUT%\export.png" goto failed
echo PASS: %OUT%
type "%OUT%.run.log"
set "RESULT=0"
goto done
:failed
echo FAIL: %OUT%
if exist "%OUT%.build.log" type "%OUT%.build.log"
if exist "%OUT%.run.log" type "%OUT%.run.log"
:done
pause
exit /b %RESULT%
