@echo off
rem EMOTION ENHANCED SPEECH: you pick emotionless speech and either a model utterance (its prosody gives the emotion) or an emotion you name and how strong; it writes the speech with the emotion.
call "%~dp0..\_support\_settings.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\_support\_usc.ps1" -App EES
if errorlevel 1 pause
