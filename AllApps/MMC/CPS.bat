@echo off
rem CONVERSATION WITH PERSONAL STATUS: you pick a picture of your face and say what you say (typed, or a WAV file); it answers with a Portable Avatar, taking into account how you feel.
call "%~dp0..\_support\_settings.bat"
call "%~dp0..\_support\_ollama.bat"
cd /d %MPAI_ROOT%
call "%~dp0..\_support\_controller.bat" Apps\MMC\CPS\Controller\CpsController.csproj Apps\MMC\CPS\Controller\bin\Debug\net10.0\CpsController.exe 5059 "CPS Controller"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\_support\_cps.ps1"
if errorlevel 1 pause
