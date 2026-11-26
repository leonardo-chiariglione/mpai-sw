@echo off
call "%~dp0..\_support\_settings.bat"
rem A meeting in the MMM world: it asks for the chair and the guests, starts the server and a public https address, and prints a personal link for each guest. See AllApps\_support\Run-Meeting.ps1.
rem SPOKEN: speech recognition (Whisper), the language model (Ollama) and the voices; WRITTEN: none of them, the participants write their requests to the Secretary.
rem (MMM_VERSION, not VERSION: MSBuild takes the variables of the environment as properties, and VERSION=spoken made the build of the server fail in its restore)
rem AS LAST TIME (2026-11-26): Enter keeps what the last meeting had (spoken the first time); the chair, the guests and the rest are also those of the last meeting (Run-Meeting.ps1 -Fresh: anew).
set MMM_VERSION=
set /p MMM_VERSION=Spoken or written (Enter: as last time): 
set WRITTEN=
if /i "%MMM_VERSION%"=="written" set WRITTEN=-Written
if /i "%MMM_VERSION%"=="w" set WRITTEN=-Written
if /i "%MMM_VERSION%"=="spoken" set WRITTEN=-Spoken
if /i "%MMM_VERSION%"=="s" set WRITTEN=-Spoken
call "%~dp0Run-Meeting.bat" %WRITTEN% %*
pause
