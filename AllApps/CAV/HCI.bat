@echo off
rem Runs HCI (passenger conversation with the CAV) from the source (the first run builds it). Its Controller listens on 5062 (5059 is the CPS Controller's).
call "%~dp0..\_support\_settings.bat"
cd /d %MPAI_ROOT%
call "%~dp0..\_support\_controller.bat" Apps\CAV\HCIApps\Controller\HciController.csproj Apps\CAV\HCIApps\Controller\bin\Debug\net10.0\HciController.exe 5062 "HCI Controller"
dotnet run --project Apps\CAV\HCIApps\HciApp\src\HciApp.csproj -- --controller http://127.0.0.1:5062
pause