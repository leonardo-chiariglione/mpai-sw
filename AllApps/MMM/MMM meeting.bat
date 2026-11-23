@echo off
call "%~dp0..\_support\_settings.bat"
rem A meeting in the MMM world: it asks for the chair and the guests, starts the server and a public https address, and prints a personal link for each guest. See AllApps\MMM\Run-Meeting.ps1.
rem SPOKEN: speech recognition (Whisper), the language model (Ollama) and the voices; WRITTEN: none of them, the participants write their requests to the Secretary.
rem (MMM_VERSION, not VERSION: MSBuild takes the variables of the environment as properties, and VERSION=spoken made the build of the server fail in its restore)
set MMM_VERSION=spoken
set /p MMM_VERSION=Spoken or written (Enter for spoken): 
set WRITTEN=
if /i "%MMM_VERSION%"=="written" set WRITTEN=-Written
if /i "%MMM_VERSION%"=="w" set WRITTEN=-Written
call "%~dp0Run-Meeting.bat" %WRITTEN% %*
pause
