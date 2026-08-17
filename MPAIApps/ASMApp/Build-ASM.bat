@echo off
setlocal EnableExtensions
rem ===========================================================================
rem  Build-ASM.bat  -  ONE-TIME builder for the CAE-ASM application.
rem  Framework-dependent (.NET 10 runtime required). Run once; then launch
rem  the exe (or ASMApp.bat) for everyday use - no rebuild needed unless the
rem  code changes.
rem
rem    Produces:  AMQApp-style layout
rem      D:\AI\MPAIApps\ASMApp\ASMApp.bat        (run this - launches the app)
rem      D:\AI\MPAIApps\ASMApp\_bin\ASMApp.exe   (+ its files)
rem ===========================================================================

set "ROOT=D:\AI"
set "DEST=%ROOT%\MPAIApps\ASMApp"
set "PROJ=%DEST%\Mpai.AsmApp.csproj"
set "OUT=%DEST%\_bin"

echo(
echo ============================================
echo   Build CAE-ASM  (one-time)
echo ============================================
echo(

rem close any running instance so its DLLs aren't locked
taskkill /IM ASMApp.exe /F >nul 2>&1

echo Publishing ASMApp -^> %OUT%
if exist "%OUT%" rmdir /s /q "%OUT%"
dotnet publish "%PROJ%" -c Release -o "%OUT%"
if errorlevel 1 ( echo. & echo BUILD FAILED. & pause & exit /b 1 )

rem visible launcher at the top level
> "%DEST%\ASMApp.bat" echo @echo off
>>"%DEST%\ASMApp.bat" echo start "" "%%~dp0_bin\ASMApp.exe"

echo(
echo ============================================
echo   DONE.  Run CAE-ASM with:
echo     %DEST%\ASMApp.bat
echo   (or directly: %OUT%\ASMApp.exe)
echo ============================================
pause
exit /b 0
