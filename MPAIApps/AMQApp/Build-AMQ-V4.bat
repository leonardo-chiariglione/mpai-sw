@echo off
setlocal EnableExtensions
rem ===========================================================================
rem  Build-AMQ-V4.bat  -  ONE-TIME builder for the AMQ application.
rem  Framework-dependent (.NET 10 runtime required).
rem
rem  Layout (only the two launchers are visible; all binaries live under _bin\):
rem    AMQApp\
rem      AMQStandalone.bat        (run this - standalone)
rem      AMQMas.bat               (run this - server + client)
rem      _bin\standalone\         UaUi.exe (+dlls)  MasServerUrl=""
rem      _bin\mas\Server\         SciHost.exe (+dlls)
rem      _bin\mas\Client\         UaUi.exe (+dlls)  MasServerUrl=localhost:5005
rem ===========================================================================

set "ROOT=D:\AI"
set "DEST=%ROOT%\MPAIApps\AMQApp"
set "BIN=%DEST%\_bin"
set "UAUI=%ROOT%\MPAIApps\AMQ\UaUi\UaUi.csproj"
set "SCI=%ROOT%\MPAIApps\MAS\SciHost\SciHost.csproj"
set "SRCCFG=%ROOT%\MPAIApps\AMQ\UaUi\ua-config.json"

echo(
echo ============================================
echo   Build AMQ  (one-time)
echo ============================================
echo   [1] Standalone (no MAS)
echo   [2] With MAS (server + client + launcher)
echo(
set "CHOICE="
set /p "CHOICE=Choose 1 or 2: "

if "%CHOICE%"=="1" goto STANDALONE
if "%CHOICE%"=="2" goto MAS
echo Invalid choice.
pause
exit /b 1

rem ---------------------------------------------------------------------------
:STANDALONE
set "OUT=%BIN%\standalone"
echo(
echo Building STANDALONE -^> %OUT%
if exist "%OUT%" rmdir /s /q "%OUT%"
dotnet publish "%UAUI%" -c Release -o "%OUT%"
if errorlevel 1 ( echo. & echo BUILD FAILED. & pause & exit /b 1 )

rem force MasServerUrl="" (PS 5.1-safe write via .NET)
call :WRITECFG "%OUT%\ua-config.json" ""

rem visible launcher at top level
> "%DEST%\AMQStandalone.bat" echo @echo off
>>"%DEST%\AMQStandalone.bat" echo start "" "%%~dp0_bin\standalone\UaUi.exe"

echo(
echo ============================================
echo   DONE.  Run standalone with:  %DEST%\AMQStandalone.bat
echo ============================================
pause
exit /b 0

rem ---------------------------------------------------------------------------
:MAS
set "SRV=%BIN%\mas\Server"
set "CLI=%BIN%\mas\Client"
echo(
echo Building MAS SERVER -^> %SRV%
if exist "%SRV%" rmdir /s /q "%SRV%"
dotnet publish "%SCI%" -c Release -o "%SRV%"
if errorlevel 1 ( echo. & echo SERVER BUILD FAILED. & pause & exit /b 1 )

echo(
echo Building MAS CLIENT -^> %CLI%
if exist "%CLI%" rmdir /s /q "%CLI%"
dotnet publish "%UAUI%" -c Release -o "%CLI%"
if errorlevel 1 ( echo. & echo CLIENT BUILD FAILED. & pause & exit /b 1 )

rem force MasServerUrl at the local server
call :WRITECFG "%CLI%\ua-config.json" "http://localhost:5005/"

rem visible launcher at top level: Terminal 1 = server, Terminal 2 = client
> "%DEST%\AMQMas.bat" echo @echo off
>>"%DEST%\AMQMas.bat" echo start "TERMINAL 1 - MAS SERVER" cmd /k ""%%~dp0_bin\mas\Server\SciHost.exe""
>>"%DEST%\AMQMas.bat" echo echo Waiting for the server to load models...
>>"%DEST%\AMQMas.bat" echo timeout /t 8 /nobreak ^>nul
>>"%DEST%\AMQMas.bat" echo start "TERMINAL 2 - CLIENT" ""%%~dp0_bin\mas\Client\UaUi.exe""

echo(
echo ============================================
echo   DONE.  Run MAS with:  %DEST%\AMQMas.bat
echo   Terminal 1 = server, Terminal 2 = client.
echo ============================================
pause
exit /b 0

rem ---------------------------------------------------------------------------
rem  :WRITECFG  <targetPath>  <MasServerUrl>
rem  Writes ua-config.json from the source, replacing only MasServerUrl,
rem  using .NET File IO so it works on Windows PowerShell 5.1.
:WRITECFG
powershell -NoProfile -Command "$src='%SRCCFG%'; $dst='%~1'; $url='%~2'; $c=[IO.File]::ReadAllText($src); $c=[regex]::Replace($c,'\"MasServerUrl\"\s*:\s*\"[^\"]*\"',('\"MasServerUrl\": \"'+$url+'\"')); [IO.File]::WriteAllText($dst,$c)"
exit /b 0
