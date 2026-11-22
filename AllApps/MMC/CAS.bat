@echo off
rem CONVERSATION ABOUT A SCENE: you pick a picture of a scene and a WAV file of what you say about it; it answers with a Portable Avatar.
call "%~dp0..\_support\_settings.bat"
call "%~dp0..\_support\_ollama.bat"
cd /d %MPAI_ROOT%
call "%~dp0..\_support\_controller.bat" Apps\MMC\CAS\Controller\CasController.csproj Apps\MMC\CAS\Controller\bin\Debug\net10.0\CasController.exe 5061 "CAS Controller"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\_support\_cas.ps1"
if errorlevel 1 pause
