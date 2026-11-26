@echo off
rem THE PARTICIPANTS' MPAI AS A SERVICE - the Apps of the Thalia given to the MMM meeting participants, on this machine, put on the internet by Tailscale Funnel. Close this window to stop it.
title Thalia - the participants' Service
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Participants-Service.ps1"
pause
