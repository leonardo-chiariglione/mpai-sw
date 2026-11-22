@echo off
rem TELEVISION MEDIA ANALYSIS: you pick the video of a programme; it tells who speaks when, what is said and what is shown (the Multimodal Event Descriptors).
call "%~dp0..\_support\_settings.bat"
cd /d %MPAI_ROOT%
call "%~dp0..\_support\_controller.bat" Apps\OSD\TMA\Controller\TmaController.csproj Apps\OSD\TMA\Controller\bin\Debug\net10.0\TmaController.exe 5060 "TMA Controller"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\_support\_tma.ps1"
if errorlevel 1 pause
