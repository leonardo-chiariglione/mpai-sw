@echo off
rem THALIA DEMO - Thalia with its three places to look for an App, all of them answering: the Apps on this machine, the Apps in the MPAI Store, and the Apps of an MPAI as a Service instance.
rem It starts the MPAI as a Service instance of this machine (it answers Thalia's queries: it offers its Apps and runs them), and then Thalia with a Store and data of its own (%LOCALAPPDATA%\Thalia-demo): the Store is given
rem every App that comes with the software, so the demo looks the same each time and nothing of your own Thalia, Store or Apps is touched. Close Thalia with its Close button, and stop the instance with
rem AllApps\MAS\"Stop the MAS App and the Service.bat".
title Thalia demo
cd /d "%~dp0.."
powershell -NoProfile -Command "if ((Get-NetTCPConnection -LocalPort 5199 -State Listen -ErrorAction SilentlyContinue)) { exit 1 } else { exit 0 }"
if errorlevel 1 (
  echo Thalia is already running on this machine. Close it with its Close button, and run this again.
  pause
  exit /b 1
)
echo Building Thalia (only what changed)...
dotnet build Thalia\Controller\ThaliaController.csproj -nologo -v q -clp:ErrorsOnly || goto failed
dotnet build AIF\UserAgent\Clients\Browser\Host\RcaWeb.Host.csproj -nologo -v q -clp:ErrorsOnly || goto failed
dotnet build Thalia\Core\Thalia.csproj -nologo -v q -clp:ErrorsOnly || goto failed
dotnet build Store\Service\StoreService.csproj -nologo -v q -clp:ErrorsOnly -o Thalia\Store\bin || goto failed
rem The MPAI as a Service instance: started if it is not running, and waited for (it loads its models: about a minute).
call "AllApps\_support\_service.bat"
start "" msedge --user-data-dir="%LOCALAPPDATA%\Thalia\edge" --no-first-run --no-default-browser-check --kiosk http://localhost:5199/ --edge-kiosk-type=fullscreen
dotnet Thalia\Core\bin\Debug\net10.0\Thalia.dll --Data "%LOCALAPPDATA%\Thalia-demo" --Store http://127.0.0.1:5026/
goto :eof
:failed
echo The build failed: see the messages above.
pause
