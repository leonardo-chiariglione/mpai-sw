@echo off
rem A meeting in the MMM world through Tailscale (see _support\Run-Meeting.ps1). Runs the script without needing the PowerShell script policy changed.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\_support\Run-Meeting.ps1" %*
