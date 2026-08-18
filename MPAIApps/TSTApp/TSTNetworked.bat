@echo off
start "MPAI-MAS SERVER" "%~dp0TSTServer.exe"
echo Waiting for the server to load its models...
timeout /t 10 /nobreak >nul
start "" "%~dp0TSTClient.exe"
