@echo off
call "%~dp0..\_support\_settings.bat"
rem A meeting in the MMM world: it asks for the chair and the guests, starts the server and a public https address, and prints a personal link for each guest. See AllApps\MMM\Run-Meeting.ps1.
call "%~dp0Run-Meeting.bat" %*
pause
