@echo off
rem ---------------------------------------------------------------------------
rem  Builds AMQApp.exe in this folder from the canonical MPAIApps\AMQApp project.
rem
rem  Answer to Multimodal Question: select an image, speak a question, hear the
rem  answer. The application is a thin host; the AI Modules and the AI Framework
rem  live in D:\AI\AIMs and D:\AI\AIF, and the model locations are in
rem  D:\AI\AIMs\aim-settings.json.
rem
rem  Publishing goes to a clean .\publish subfolder and the finished single-file
rem  exe is copied up here. (Publishing straight to "%~dp0" fails: the trailing
rem  backslash before the quote is parsed as an escaped quote and mangles the
rem  command line.)
rem ---------------------------------------------------------------------------

rem  A running application cannot be overwritten, so close it first.
tasklist /fi "imagename eq AMQApp.exe" | find /i "AMQApp.exe" >nul
if not errorlevel 1 (
    echo AMQApp.exe is running - closing it.
    taskkill /im AMQApp.exe /f >nul 2>&1
    timeout /t 2 /nobreak >nul
)

echo Cleaning previous build output ...
if exist "%~dp0obj"     rmdir /s /q "%~dp0obj"
if exist "%~dp0bin"     rmdir /s /q "%~dp0bin"
if exist "%~dp0publish" rmdir /s /q "%~dp0publish"
echo.

echo Building AMQApp.exe ...
echo.

dotnet publish "%~dp0Mpai.AmqApp.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained false ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=none ^
    -p:DebugSymbols=false ^
    -o "%~dp0publish"

if errorlevel 1 (
    echo.
    echo Build FAILED.
    echo.
    echo If the error mentions "Access to the path ... is denied", the
    echo application was still running: close it and run this file again.
    pause
    exit /b 1
)

rem  Single-file publish bundles the managed dependencies, so only the exe and
rem  its json config need to sit next to this script.
for %%F in ("%~dp0publish\AMQApp.exe" "%~dp0publish\AMQApp.runtimeconfig.json" "%~dp0publish\AMQApp.deps.json") do (
    if exist "%%~F" copy /y "%%~F" "%~dp0" >nul
)
rmdir /s /q "%~dp0publish"

echo.
echo Built: %~dp0AMQApp.exe
echo.
dir /b "%~dp0AMQApp.*"
echo.
pause
