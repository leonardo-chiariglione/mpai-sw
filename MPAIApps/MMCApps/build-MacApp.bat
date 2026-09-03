@echo off
setlocal
echo ============================================================
echo   Building MacApp.exe (Multimodal Access Control)
echo ============================================================
set SRC=D:\AI\MPAIApps\CAV-MAC\CavMac
set HERE=D:\AI\MPAIApps\MMCApps
set TMP=%HERE%\_macpublish

echo Closing any running instance...
taskkill /IM MacApp.exe /F >nul 2>&1

echo Cleaning this project's bin/obj (avoids 'ambiguous project name')...
if exist "%SRC%\bin" rd /s /q "%SRC%\bin" 2>nul
if exist "%SRC%\obj" rd /s /q "%SRC%\obj" 2>nul
if exist "%TMP%" rd /s /q "%TMP%" 2>nul

echo Publishing single-file exe (AssemblyName=MacApp is set in CavMac.csproj)...
dotnet publish "%SRC%\CavMac.csproj" -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "%TMP%"
if errorlevel 1 ( echo BUILD FAILED. & pause & exit /b 1 )

if not exist "%TMP%\MacApp.exe" ( echo MacApp.exe not produced. & pause & exit /b 1 )
echo Placing MacApp.exe next to this .bat...
copy /Y "%TMP%\MacApp.exe" "%HERE%\" >nul
if errorlevel 1 ( echo COPY FAILED (is MacApp.exe running?). & pause & exit /b 1 )
if exist "%TMP%\WebView2Loader.dll" copy /Y "%TMP%\WebView2Loader.dll" "%HERE%\" >nul
rd /s /q "%TMP%" 2>nul

echo.
echo ============================================================
echo   DONE.  %HERE%\MacApp.exe
echo ============================================================
pause
