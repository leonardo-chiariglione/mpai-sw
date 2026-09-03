@echo off
setlocal
echo ============================================================
echo   Building MacApp.exe (Multimodal Access Control)
echo ============================================================
set SRC=D:\AI\MPAIApps\CAV-MAC\CavMac\CavMac.csproj
set HERE=D:\AI\MPAIApps\MMCApps
set TMP=%HERE%\_build\MacApp
echo Closing any running instance...
taskkill /IM MacApp.exe /F >nul 2>&1
echo Publishing single-file exe...
dotnet publish "%SRC%" -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:AssemblyName=MacApp ^
  -o "%TMP%"
if errorlevel 1 ( echo BUILD FAILED. & pause & exit /b 1 )
echo Placing MacApp.exe next to this .bat...
copy /Y "%TMP%\MacApp.exe" "%HERE%\" >nul
if errorlevel 1 ( echo COPY FAILED (is MacApp.exe running?). & pause & exit /b 1 )
if exist "%TMP%\WebView2Loader.dll" copy /Y "%TMP%\WebView2Loader.dll" "%HERE%\" >nul
rmdir /S /Q "%HERE%\_build" 2>nul
echo.
echo ============================================================
echo   DONE.  Double-click to demo:  %HERE%\MacApp.exe
echo   (Avatar assets are read from D:\AI\Lib\Assets. Gallery: D:\AI\TestData\gallery.json.)
echo ============================================================
pause
