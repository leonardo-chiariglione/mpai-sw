@echo off
rem SPEAK MODEL WITH EMOTION: you pick emotionless speech and a model utterance; it writes the speech with the emotion of the prosody of the model.
call "%~dp0..\_support\_settings.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\_support\_usc.ps1" -App SME
if errorlevel 1 pause
