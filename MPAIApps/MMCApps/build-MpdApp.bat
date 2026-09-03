@echo off
setlocal
echo ============================================================
echo   Building MpdApp.exe (Multimodal Personal Status-based Dialogue)
echo ============================================================
set SRC=D:\AI\MPAIApps\HCIApp\MpdApp\MpdApp.csproj
set HERE=D:\AI\MPAIApps\MMCApps
set TMP=%HERE%\_build\MpdApp
echo Publishing single-file exe...
dotnet publish "%SRC%" -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "%TMP%"
if errorlevel 1 ( echo BUILD FAILED. & pause & exit /b 1 )
echo Placing MpdApp.exe next to this .bat...
copy /Y "%TMP%\MpdApp.exe" "%HERE%\" >nul
if exist "%TMP%\WebView2Loader.dll" copy /Y "%TMP%\WebView2Loader.dll" "%HERE%\" >nul
rmdir /S /Q "%HERE%\_build" 2>nul
echo.
echo ============================================================
echo   DONE.  Double-click to demo:  %HERE%\MpdApp.exe
echo   (Avatar assets are read from D:\AI\Lib\Assets. Needs Ollama running.)
echo ============================================================
pause
