@echo off
setlocal EnableExtensions
rem ===========================================================================
rem  Build-TST.bat  -  builds MPAI Text and Speech Translation as SINGLE FILES.
rem
rem  Everything lands in this folder, with no subfolders to dig through:
rem
rem    TSTStandalone.exe    one process, no server
rem    TSTServer.exe        the MPAI-MAS server
rem    TSTClient.exe        the Remote Client Application
rem    TSTNetworked.bat     starts the server, waits, then the client
rem
rem  Single-file publish is what keeps it to four items instead of four hundred:
rem  the runtime assemblies and the native libraries are packed inside each .exe.
rem  Framework-dependent, so the .NET 10 runtime is still required.
rem
rem  The two client copies would otherwise fight over one tst-config.json, so
rem  each reads a file named after itself - TSTClient-config.json - which is why
rem  they can share a folder at all.
rem ===========================================================================

set "ROOT=D:\AI"
set "DEST=%ROOT%\MPAIApps\TSTApp"
set "TSTUI=%ROOT%\MPAIApps\TST\TstUi\TstUi.csproj"
set "SCI=%ROOT%\MPAIApps\MAS\SciHost\SciHost.csproj"

set "SINGLE=-c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None"
set "STAGE=%TEMP%\tst-publish"

rem  NOTE on naming. -p:AssemblyName=... cannot be used here: a property given on
rem  the command line applies to EVERY project in the graph, so all nine
rem  references were renamed at once and NuGet reported an ambiguous project
rem  name. Each application is therefore published under its own name into a
rem  staging folder and the single file is renamed on the way out. That is safe:
rem  a single-file apphost finds its bundle inside itself, not by its filename.

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

call :WRITECFG "%DEST%\TSTStandalone-config.json" ""
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
copy /y "%STAGE%\server\SciHost.exe" "%DEST%\TSTServer.exe" >nul
if errorlevel 1 ( echo. & echo Could not place TSTServer.exe. & exit /b 1 )

echo(
echo Building TSTClient.exe ...
if exist "%STAGE%\client" rmdir /s /q "%STAGE%\client"
dotnet publish "%TSTUI%" %SINGLE% -o "%STAGE%\client"
if errorlevel 1 ( echo. & echo CLIENT BUILD FAILED. & exit /b 1 )
copy /y "%STAGE%\client\TstUi.exe" "%DEST%\TSTClient.exe" >nul
if errorlevel 1 ( echo. & echo Could not place TSTClient.exe. & exit /b 1 )

call :WRITECFG "%DEST%\TSTClient-config.json" "http://localhost:5005/"

rem The client alone would find no server, so the launcher imposes the order.
> "%DEST%\TSTNetworked.bat" echo @echo off
>>"%DEST%\TSTNetworked.bat" echo start "MPAI-MAS SERVER" "%%~dp0TSTServer.exe"
>>"%DEST%\TSTNetworked.bat" echo echo Waiting for the server to load its models...
>>"%DEST%\TSTNetworked.bat" echo timeout /t 10 /nobreak ^>nul
>>"%DEST%\TSTNetworked.bat" echo start "" "%%~dp0TSTClient.exe"

call :SHORTCUT "MPAI TST (networked)" "%DEST%\TSTNetworked.bat" "%DEST%"
echo   TSTServer.exe and TSTClient.exe done.
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
rem Publishing leaves a few loose files behind; the demo folder should hold the
rem executables and nothing else.
del /q "%DEST%\*.pdb"       2>nul
del /q "%DEST%\*.deps.json" 2>nul
del /q "%DEST%\*.xml"       2>nul
if exist "%STAGE%" rmdir /s /q "%STAGE%"

echo(
echo ============================================
echo   DONE - everything is in this folder:
echo(
dir /b "%DEST%\*.exe"
echo(
echo   Desktop shortcuts were created too.
echo ============================================
pause
exit /b 0