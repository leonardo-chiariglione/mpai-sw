@echo off
rem THE AI FRAMEWORK, as its User sees it: Thalia, the MPAI AI Framework user environment. It builds what changed, opens its screen full-screen (an Edge kiosk window in its own profile) and runs the Apps from it.
rem "Return to Windows" on its screen brings the desktop back; close the window that this opens to stop Thalia and the Apps it started.
call "%~dp0..\..\Thalia\Thalia.bat"
