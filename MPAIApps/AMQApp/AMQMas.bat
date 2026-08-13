@echo off
start "TERMINAL 1 - MAS SERVER" cmd /k ""%~dp0_bin\mas\Server\SciHost.exe""
echo Waiting for the server to load models...
timeout /t 8 /nobreak >nul
start "TERMINAL 2 - CLIENT" ""%~dp0_bin\mas\Client\UaUi.exe""
