@echo off
setlocal
echo ============================================================
echo   Building MatApp.exe (Multimodal Anonymous Translation)
echo ============================================================
set SRC=D:\AI\MPAIApps\HCIApp\MatApp\MatApp.csproj
set HERE=D:\AI\MPAIApps\MMCApps
set TMP=%HERE%\_build\MatApp

echo Publishing single-file exe...
dotnet publish "%SRC%" -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "%TMP%"
if errorlevel 1 ( echo BUILD FAILED. & pause & exit /b 1 )

echo Placing MatApp.exe next to this .bat...
copy /Y "%TMP%\MatApp.exe" "%HERE%\" >nul
if exist "%TMP%\WebView2Loader.dll" copy /Y "%TMP%\WebView2Loader.dll" "%HERE%\" >nul
rmdir /S /Q "%HERE%\_build" 2>nul

echo.
echo ============================================================
echo   DONE.  Double-click to demo:  %HERE%\MatApp.exe
echo   (Avatar assets are read from D:\AI\Lib\Assets.)
echo ============================================================
pause
