@echo off
rem COMMUNICATING ENTITIES IN CONTEXT (HMC-CEC V2.2): your face (camera or picture) and what you say (microphone, typed, or a WAV file), in your language; it answers with a Portable Avatar in the language you choose.
call "%~dp0..\_support\_settings.bat"
call "%~dp0..\_support\_ollama.bat"
cd /d %MPAI_ROOT%
call "%~dp0..\_support\_controller.bat" Apps\HMC\CEC\Controller\HmcController.csproj Apps\HMC\CEC\Controller\bin\Debug\net10.0\HmcController.exe 5064 "HMC Controller"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\_support\_cec.ps1"
if errorlevel 1 pause
