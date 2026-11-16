@echo off
rem Starts the MMM demonstration "Open the door when the bell rings" (see Run-Door.ps1). Windows does not let PowerShell
rem run scripts by default; this lets it run this one script, for this one run, and changes nothing.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-Door.ps1" %*
