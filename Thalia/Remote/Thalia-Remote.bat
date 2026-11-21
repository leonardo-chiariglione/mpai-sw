@echo off
rem THALIA REMOTE - Thalia on this laptop, using the MPAI as a Service of the User's desktop over Tailscale. Close this window to stop it.
title Thalia Remote
cd /d "%~dp0"
start "" msedge --user-data-dir="%LOCALAPPDATA%\Thalia\edge" --no-first-run --no-default-browser-check --kiosk http://localhost:5199/ --edge-kiosk-type=fullscreen
Thalia\Thalia.exe --Profile Remote --Urls http://localhost:5199
pause
