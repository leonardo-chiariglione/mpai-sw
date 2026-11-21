@echo off
rem THALIA REMOTE SERVICE - offers this machine's MPAI as a Service to the User's own devices over Tailscale. Close this window to stop it.
title Thalia Remote Service
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Service.ps1"
pause
