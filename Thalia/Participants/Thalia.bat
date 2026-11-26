@echo off
rem THALIA - the MPAI environment: the Apps of MPAI as a Service, and your User in the MMM world. Close this window to stop Thalia.
title Thalia
cd /d "%~dp0"
rem Thalia is run by the package's own copy of Microsoft's dotnet (runtime\): there is no program of ours to start, and nothing to install.
start "Thalia server" /min runtime\dotnet.exe Thalia\Thalia.dll --Profile Remote --Urls http://localhost:5199
powershell -NoProfile -Command "for ($i = 0; $i -lt 60; $i++) { try { Invoke-WebRequest http://localhost:5199/ -UseBasicParsing -TimeoutSec 2 | Out-Null; break } catch { Start-Sleep 1 } }"
start "" msedge --user-data-dir="%LOCALAPPDATA%\Thalia\edge" --no-first-run --no-default-browser-check --app=http://localhost:5199/ --start-maximized
echo Thalia is running. Close this window, and the Thalia window, when you have finished.
pause
taskkill /fi "WINDOWTITLE eq Thalia server*" /t /f >nul 2>&1
