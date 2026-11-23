@echo off
rem XRV-LTP, Live Theatrical Performance, in Real-Virtual Environments, LIVE ON THE SCREEN: it starts the two M-Instances of MMM-TEC (the Theatre, the Real Environment, and the Square, the Virtual Environment),
rem the Controller with the Module of XRV-LTP and its User Agent with the devices Show, RealVenue and VirtualVenue, and opens the two Venues full screen in an Edge window.
rem In the window press Start (it also allows the sound); the show plays its Cues; Close ends it. Ctrl+C in this window stops everything. Apps\XRV\LTP\README.md says what the show is.
call "%~dp0..\_support\_settings.bat"
cd /d "%MPAI_ROOT%"
dotnet build Apps\XRV\LTP\TwoMetaverses\TwoMetaverses.csproj -nologo -v q
if errorlevel 1 ( echo The build failed. & pause & exit /b 1 )
dotnet run --no-build --project Apps\XRV\LTP\TwoMetaverses -- --live
if errorlevel 1 pause
