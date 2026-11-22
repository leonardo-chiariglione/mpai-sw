@echo off
rem ADD EMOTION TO SPEECH: you pick emotionless speech and name an emotion and how strong; it writes the speech with the emotion.
call "%~dp0..\_support\_settings.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\_support\_usc.ps1" -App AES
if errorlevel 1 pause
