@echo off
setlocal EnableExtensions
rem ===========================================================================
rem  Build-AMQ.bat  -  builds MPAI Answer to Multimodal Question as SINGLE FILES.
rem
rem  The application folder holds ONLY what a person launches:
rem
rem    AMQStandalone.exe    one process, no server
rem    AMQNetworked.exe     starts the server, waits, then the client
rem    bin\                 everything those two need and nobody opens
rem
rem  This replaces Build-AMQ-V4.bat, which published FOLDERS of loose assemblies
rem  into _bin\standalone, _bin\mas\Server and _bin\mas\Client, and wrote two
rem  .bat launchers. Single-file publish makes each application one file, so the
rem  three copies can share one bin\ instead of needing a directory apiece to
rem  keep their assemblies and their ua-config.json apart.
rem
rem  Same shape as Build-TST.bat, deliberately: two applications that behave the
rem  same way are easier to demonstrate than two that each have their own habits.
rem ===========================================================================

set "ROOT=D:\AI"
set "DEST=%ROOT%\MPAIApps\AMQApp"
set "BIN=%DEST%\bin"
set "UAUI=%ROOT%\MPAIApps\AMQ\UaUi\UaUi.csproj"
set "SCI=%ROOT%\MPAIApps\MAS\SciHost\SciHost.csproj"
set "LAUNCHER=%ROOT%\MPAIApps\AMQ\Launcher\MpaiNetworked.csproj"
set "SRCCFG=%ROOT%\MPAIApps\AMQ\UaUi\ua-config.json"

set "SINGLE=-c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None"
set "STAGE=%TEMP%\amq-publish"

if not exist "%BIN%" mkdir "%BIN%"

echo(
echo ============================================
echo   Build MPAI Answer to Multimodal Question
echo ============================================
echo   [1] Standalone       - one process, no server
echo   [2] Networked (MAS)  - server + client
echo   [3] Both
echo(
set "CHOICE="
set /p "CHOICE=Choose 1, 2 or 3: "

if "%CHOICE%"=="1" goto STANDALONE
if "%CHOICE%"=="2" goto MAS
if "%CHOICE%"=="3" goto BOTH
echo Invalid choice.
pause
exit /b 1

:BOTH
call :DOSTANDALONE || goto :FAILED
call :DOMAS        || goto :FAILED
goto :FINISHED

:STANDALONE
call :DOSTANDALONE || goto :FAILED
goto :FINISHED

:MAS
call :DOMAS || goto :FAILED
goto :FINISHED

rem ---------------------------------------------------------------------------
:DOSTANDALONE
echo(
echo Building AMQStandalone.exe ...
if exist "%STAGE%\standalone" rmdir /s /q "%STAGE%\standalone"
dotnet publish "%UAUI%" %SINGLE% -o "%STAGE%\standalone"
if errorlevel 1 ( echo. & echo STANDALONE BUILD FAILED. & exit /b 1 )
copy /y "%STAGE%\standalone\UaUi.exe" "%DEST%\AMQStandalone.exe" >nul
if errorlevel 1 ( echo. & echo Could not place AMQStandalone.exe. & exit /b 1 )

call :WRITECFG "%BIN%\AMQStandalone-config.json" ""
call :SHORTCUT "MPAI AMQ (standalone)" "%DEST%\AMQStandalone.exe" "%DEST%"
echo   AMQStandalone.exe done.
exit /b 0

rem ---------------------------------------------------------------------------
:DOMAS
echo(
echo Building AMQServer.exe ...
if exist "%STAGE%\server" rmdir /s /q "%STAGE%\server"
dotnet publish "%SCI%" %SINGLE% -o "%STAGE%\server"
if errorlevel 1 ( echo. & echo SERVER BUILD FAILED. & exit /b 1 )
copy /y "%STAGE%\server\SciHost.exe" "%BIN%\AMQServer.exe" >nul
if errorlevel 1 ( echo. & echo Could not place AMQServer.exe. & exit /b 1 )

echo(
echo Building AMQClient.exe ...
if exist "%STAGE%\client" rmdir /s /q "%STAGE%\client"
dotnet publish "%UAUI%" %SINGLE% -o "%STAGE%\client"
if errorlevel 1 ( echo. & echo CLIENT BUILD FAILED. & exit /b 1 )
copy /y "%STAGE%\client\UaUi.exe" "%BIN%\AMQClient.exe" >nul
if errorlevel 1 ( echo. & echo Could not place AMQClient.exe. & exit /b 1 )

call :WRITECFG "%BIN%\AMQClient-config.json" "http://localhost:5005/"

echo(
echo Building AMQNetworked.exe ...
if exist "%STAGE%\launcher" rmdir /s /q "%STAGE%\launcher"
dotnet publish "%LAUNCHER%" %SINGLE% -o "%STAGE%\launcher"
if errorlevel 1 ( echo. & echo LAUNCHER BUILD FAILED. & exit /b 1 )
copy /y "%STAGE%\launcher\MpaiNetworked.exe" "%DEST%\AMQNetworked.exe" >nul
if errorlevel 1 ( echo. & echo Could not place AMQNetworked.exe. & exit /b 1 )

call :SHORTCUT "MPAI AMQ (networked)" "%DEST%\AMQNetworked.exe" "%DEST%"
echo   AMQServer.exe, AMQClient.exe and AMQNetworked.exe done.
exit /b 0

rem ---------------------------------------------------------------------------
rem  :WRITECFG  <targetPath>  <MasServerUrl>
rem  Copies ua-config.json, replacing only MasServerUrl. The paths inside it -
rem  MpaiRoot and the rest - are what the application needs and are kept.
:WRITECFG
powershell -NoProfile -ExecutionPolicy Bypass -Command "$src='%SRCCFG%'; $dst='%~1'; $url='%~2'; $c=[IO.File]::ReadAllText($src); $c=[regex]::Replace($c,'\"MasServerUrl\"\s*:\s*\"[^\"]*\"',('\"MasServerUrl\": \"'+$url+'\"')); [IO.File]::WriteAllText($dst,$c)"
exit /b 0

rem ---------------------------------------------------------------------------
rem  :SHORTCUT  <name>  <target>  <workingDirectory>
:SHORTCUT
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) '%~1.lnk')); $s.TargetPath='%~2'; $s.WorkingDirectory='%~3'; $s.Description='MPAI Answer to Multimodal Question'; $s.Save()"
exit /b 0

rem ---------------------------------------------------------------------------
:FAILED
echo(
echo BUILD FAILED - see the messages above.
echo(
echo If it could not place an .exe, the application is probably RUNNING.
pause
exit /b 1

:FINISHED
rem What the older builder left behind, and what publishing leaves loose.
if exist "%DEST%\_bin"              rmdir /s /q "%DEST%\_bin"
if exist "%DEST%\AMQStandalone.bat" del /q "%DEST%\AMQStandalone.bat"
if exist "%DEST%\AMQMas.bat"        del /q "%DEST%\AMQMas.bat"
del /q "%DEST%\*.pdb"       2>nul
del /q "%DEST%\*.deps.json" 2>nul
del /q "%DEST%\*.xml"       2>nul
del /q "%BIN%\*.pdb"        2>nul
del /q "%BIN%\*.deps.json"  2>nul
del /q "%BIN%\*.xml"        2>nul
if exist "%STAGE%" rmdir /s /q "%STAGE%"

echo(
echo ============================================
echo   DONE
echo(
echo   Double-click one of these:
dir /b "%DEST%\*.exe"
echo(
echo   Nothing else is in that folder - the rest is in bin\.
echo(
echo   Desktop shortcuts were created too.
echo ============================================
pause
exit /b 0