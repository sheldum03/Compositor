@echo off
setlocal
cd /d "%~dp0"
set "OUT=%CD%\..\production-core-smoke-%RANDOM%"
if exist "%OUT%" (
  echo Output already exists: %OUT%
  goto done
)
dotnet --version > "%OUT%.sdk.txt" 2>&1
if errorlevel 1 goto failed
dotnet build Compositor.Smoke\Compositor.Smoke.csproj -c Release > "%OUT%.build.log" 2>&1
if errorlevel 1 goto failed
dotnet run --project Compositor.Smoke -c Release --no-build -- ..\docs\windows\fixtures "%OUT%" > "%OUT%.run.log" 2>&1
if errorlevel 1 goto failed
echo PASS: %OUT%
type "%OUT%.run.log"
goto done
:failed
echo FAIL: %OUT%
if exist "%OUT%.build.log" type "%OUT%.build.log"
if exist "%OUT%.run.log" type "%OUT%.run.log"
:done
pause
