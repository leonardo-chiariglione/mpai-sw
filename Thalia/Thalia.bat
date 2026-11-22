@echo off
rem THALIA - starts the Thalia core and opens its Launcher in the browser (http://localhost:5199/).
rem It builds, where needed, the local Controller, the App client, the core and the MPAI Store of this machine. Close this window to stop Thalia and the Apps it started.
title Thalia
cd /d "%~dp0.."
echo Building Thalia (only what changed)...
dotnet build Thalia\Controller\ThaliaController.csproj -nologo -v q -clp:ErrorsOnly || goto failed
dotnet build AIF\UserAgent\Clients\Browser\Host\RcaWeb.Host.csproj -nologo -v q -clp:ErrorsOnly || goto failed
dotnet build Thalia\Core\Thalia.csproj -nologo -v q -clp:ErrorsOnly || goto failed
rem The MPAI Store of this machine is built apart, in its own folder, so that a Store left running does not stop the build.
dotnet build Store\Service\StoreService.csproj -nologo -v q -clp:ErrorsOnly -o Thalia\Store\bin || goto failed
rem Thalia shows its Apps. Open http://localhost:5199/?first=1 to see it as a User sees it the first time, with its functions and no Apps.
rem Thalia fills the whole screen: an Edge kiosk window in its own profile (an Edge that is already open ignores the full-screen request).
start "" msedge --user-data-dir="%LOCALAPPDATA%\Thalia\edge" --no-first-run --no-default-browser-check --kiosk http://localhost:5199/ --edge-kiosk-type=fullscreen
dotnet Thalia\Core\bin\Debug\net10.0\Thalia.dll
goto :eof
:failed
echo The build failed: see the messages above.
pause
