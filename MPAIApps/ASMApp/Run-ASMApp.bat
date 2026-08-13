@echo off
REM Launches the already-built ASMApp.exe directly - no "dotnet build", no
REM "dotnet run". Bypasses dotnet run's separate supervisor process, which
REM was the actual cause of PowerShell staying locked after closing the
REM app's windows.
REM
REM Run this for everyday use. Only re-run the sync script + "dotnet build"
REM when the code itself has actually changed.

cd /d "D:\AI\MPAIApps\ASMApp"
start "" "bin\Debug\net10.0-windows7.0\ASMApp.exe"