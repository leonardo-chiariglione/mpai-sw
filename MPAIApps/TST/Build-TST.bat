@echo off
setlocal EnableExtensions
rem ===========================================================================
rem  Build-TST.bat  -  builds MPAI Text and Speech Translation as SINGLE FILES.
rem
rem  The folder holds ONLY what a person launches:
rem
rem    TSTStandalone.exe    one process, no server
rem    TSTNetworked.exe     starts the server, waits, then the client
rem    bin\                 everything those two need and nobody opens
rem
rem  This script and the launcher source live HERE, under MPAIApps\TST, with the
rem  other source - they build the application, they do not launch it.
rem
rem  TSTNetworked.exe replaces the old TSTNetworked.bat. It WAITS for the server
rem  to answer instead of sleeping ten seconds - too long on a warm machine, too
rem  short on a cold one loading full-precision models - and it stops the server
rem  when the client closes, which the .bat never did. The server keeps its own
rem  window: for a demonstration, watching the AIMs run is most of the point.
rem
rem  Single-file publish is what keeps this to a handful of items instead of four
rem  hundred: the runtime assemblies and the native libraries are packed inside
rem  each .exe. Framework-dependent, so the .NET 10 runtime is still required.
rem
rem  The two copies of the same application would otherwise fight over one
rem  tst-config.json, so each reads a file named after itself.
rem ===========================================================================

set "ROOT=D:\AI"
set "DEST=%ROOT%\MPAIApps\TSTApp"
set "BIN=%DEST%\bin"
set "TSTUI=%ROOT%\MPAIApps\TST\TstUi\TstUi.csproj"
set "SCI=%ROOT%\MPAIApps\MAS\SciHost\SciHost.csproj"
set "LAUNCHER=%ROOT%\MPAIApps\TST\Launcher\MpaiNetworked.csproj"

set "SINGLE=-c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None"
set "STAGE=%TEMP%\tst-publish"

rem  NOTE on naming. -p:AssemblyName=... cannot be used here: a property given on
rem  the command line applies to EVERY project in the graph, so all nine
rem  references were renamed at once and NuGet reported an ambiguous project
rem  name. Each application is therefore published under its own name into a
rem  staging folder and the single file is renamed on the way out. That is safe:
rem  a single-file apphost finds its bundle inside itself, not by its filename.

if not exist "%BIN%" mkdir "%BIN%"

echo(
echo ============================================
echo   Build MPAI Text and Speech Translation
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
echo Building TSTStandalone.exe ...
if exist "%STAGE%\standalone" rmdir /s /q "%STAGE%\standalone"
dotnet publish "%TSTUI%" %SINGLE% -o "%STAGE%\standalone"
if errorlevel 1 ( echo. & echo STANDALONE BUILD FAILED. & exit /b 1 )
copy /y "%STAGE%\standalone\TstUi.exe" "%DEST%\TSTStandalone.exe" >nul
if errorlevel 1 ( echo. & echo Could not place TSTStandalone.exe. & exit /b 1 )

call :WRITECFG "%BIN%\TSTStandalone-config.json" ""
call :SHORTCUT "MPAI TST (standalone)" "%DEST%\TSTStandalone.exe" "%DEST%"
echo   TSTStandalone.exe done.
exit /b 0

rem ---------------------------------------------------------------------------
:DOMAS
echo(
echo Building TSTServer.exe ...
if exist "%STAGE%\server" rmdir /s /q "%STAGE%\server"
dotnet publish "%SCI%" %SINGLE% -o "%STAGE%\server"
if errorlevel 1 ( echo. & echo SERVER BUILD FAILED. & exit /b 1 )
copy /y "%STAGE%\server\SciHost.exe" "%BIN%\TSTServer.exe" >nul
if errorlevel 1 ( echo. & echo Could not place TSTServer.exe. & exit /b 1 )

echo(
echo Building TSTClient.exe ...
if exist "%STAGE%\client" rmdir /s /q "%STAGE%\client"
dotnet publish "%TSTUI%" %SINGLE% -o "%STAGE%\client"
if errorlevel 1 ( echo. & echo CLIENT BUILD FAILED. & exit /b 1 )
copy /y "%STAGE%\client\TstUi.exe" "%BIN%\TSTClient.exe" >nul
if errorlevel 1 ( echo. & echo Could not place TSTClient.exe. & exit /b 1 )

rem The config must sit beside the executable that reads it - both are in bin.
call :WRITECFG "%BIN%\TSTClient-config.json" "http://localhost:5005/"

echo(
echo Building TSTNetworked.exe ...
if exist "%STAGE%\launcher" rmdir /s /q "%STAGE%\launcher"
dotnet publish "%LAUNCHER%" %SINGLE% -o "%STAGE%\launcher"
if errorlevel 1 ( echo. & echo LAUNCHER BUILD FAILED. & exit /b 1 )
copy /y "%STAGE%\launcher\MpaiNetworked.exe" "%DEST%\TSTNetworked.exe" >nul
if errorlevel 1 ( echo. & echo Could not place TSTNetworked.exe. & exit /b 1 )

rem What older builds left at the top, now that only the two executables
rem belong there.
if exist "%DEST%\TSTNetworked.bat"          del /q "%DEST%\TSTNetworked.bat"
if exist "%DEST%\TSTStandalone-config.json" del /q "%DEST%\TSTStandalone-config.json"
if exist "%DEST%\README.md"                 move /y "%DEST%\README.md" "%BIN%\README.md" >nul

call :SHORTCUT "MPAI TST (networked)" "%DEST%\TSTNetworked.exe" "%DEST%"
echo   TSTServer.exe, TSTClient.exe and TSTNetworked.exe done.
exit /b 0

rem ---------------------------------------------------------------------------
rem  :WRITECFG  <targetPath>  <MasServerUrl>
:WRITECFG
powershell -NoProfile -ExecutionPolicy Bypass -Command "$j = '{' + [char]10 + '  \"MasServerUrl\": \"%~2\",' + [char]10 + '  \"Languages\": [ \"en\", \"it\", \"fr\", \"de\", \"es\", \"pt\", \"zh\", \"ja\" ]' + [char]10 + '}'; [IO.File]::WriteAllText('%~1', $j)"
exit /b 0

rem ---------------------------------------------------------------------------
rem  :SHORTCUT  <name>  <target>  <workingDirectory>
:SHORTCUT
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) '%~1.lnk')); $s.TargetPath='%~2'; $s.WorkingDirectory='%~3'; $s.Description='MPAI Text and Speech Translation'; $s.Save()"
exit /b 0

rem ---------------------------------------------------------------------------
:FAILED
echo(
echo BUILD FAILED - see the messages above.
pause
exit /b 1

:FINISHED
rem Publishing leaves a few loose files behind, in both folders.
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
echo   In bin\, which nobody needs to open:
dir /b "%BIN%" 2>nul
echo(
echo   Desktop shortcuts were created too.
echo ============================================
pause
exit /b 0