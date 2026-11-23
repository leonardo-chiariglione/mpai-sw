@echo off
call "%~dp0..\_support\_settings.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\_support\Run-Demo.ps1"
pause