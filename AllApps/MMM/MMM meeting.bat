@echo off
call "%~dp0..\_support\_settings.bat"
rem A meeting in the MMM world: it asks for the chair and the guests, starts the server and a public https address, and prints a personal link for each guest. See AllApps\MMM\Run-Meeting.ps1.
rem SPOKEN: speech recognition (Whisper), the language model (Ollama) and the voices; WRITTEN: none of them, the participants write their requests to the Secretary.
set VERSION=spoken
set /p VERSION=Spoken or written (Enter for spoken): 
set WRITTEN=
if /i "%VERSION%"=="written" set WRITTEN=-Written
if /i "%VERSION%"=="w" set WRITTEN=-Written
call "%~dp0Run-Meeting.bat" %WRITTEN% %*
pause
