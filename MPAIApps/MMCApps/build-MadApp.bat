@echo off
setlocal
echo ============================================================
echo   Building MadApp.exe (Multimodal Anonymous Dialogue)
echo ============================================================
set SRC=D:\AI\MPAIApps\HCIApp\MadApp\MadApp.csproj
set HERE=D:\AI\MPAIApps\MMCApps
set TMP=%HERE%\_build\MadApp
set ASSETS=D:\AI\TestData\Avatars

echo Publishing single-file exe...
dotnet publish "%SRC%" -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "%TMP%"
if errorlevel 1 ( echo BUILD FAILED. & pause & exit /b 1 )

echo Placing MadApp.exe next to this .bat...
copy /Y "%TMP%\MadApp.exe" "%HERE%\" >nul
if exist "%TMP%\WebView2Loader.dll" copy /Y "%TMP%\WebView2Loader.dll" "%HERE%\" >nul

echo Preparing shared web\ (viewer + avatar) ...
if not exist "%HERE%\web" mkdir "%HERE%\web"
if exist "%TMP%\web\cav-webview.html" copy /Y "%TMP%\web\cav-webview.html" "%HERE%\web\" >nul
copy /Y "%ASSETS%\cav-avatar.glb" "%HERE%\web\" >nul
copy /Y "%ASSETS%\studio.hdr"     "%HERE%\web\" >nul

echo Cleaning up build files...
rmdir /S /Q "%HERE%\_build" 2>nul

echo.
echo ============================================================
echo   DONE.  Double-click to demo:
echo   %HERE%\MadApp.exe
echo   (Needs Ollama running.)
echo ============================================================
pause
