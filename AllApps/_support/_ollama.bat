@echo off
rem STARTS OLLAMA (the language model of the dialogue: Entity Dialogue Processing) if it is not running, and waits until it answers.
rem It is the one in Support\Ollama, with the models kept there.
call "%~dp0_settings.bat"
powershell -NoProfile -Command "if ((Get-NetTCPConnection -LocalPort 11434 -State Listen -ErrorAction SilentlyContinue)) { exit 0 } else { exit 1 }"
if %errorlevel%==0 goto ready
echo Starting Ollama...
set OLLAMA_MODELS=%MPAI_ROOT%\Support\Ollama\models
rem A model stays loaded 30 minutes after its last use, not for ever: on the 4 GB card a model kept for ever stopped the model of another App from loading (2026-11-18).
set OLLAMA_KEEP_ALIVE=30m
start "Ollama" /min "%MPAI_ROOT%\Support\Ollama\bin\ollama.exe" serve
:wait
powershell -NoProfile -Command "Start-Sleep 2; if ((Get-NetTCPConnection -LocalPort 11434 -State Listen -ErrorAction SilentlyContinue)) { exit 0 } else { exit 1 }"
if not %errorlevel%==0 goto wait
:ready
echo Ollama is running.
