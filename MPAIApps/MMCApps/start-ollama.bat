@echo off
setlocal
echo ============================================================
echo   Starting Ollama (kept warm for development)
echo ============================================================

echo Persisting keep-alive (model stays loaded, no cold reloads)...
setx OLLAMA_KEEP_ALIVE -1 >nul
set OLLAMA_KEEP_ALIVE=-1

echo Restarting Ollama so it reads the setting...
taskkill /IM ollama.exe /F >nul 2>&1
taskkill /IM "ollama app.exe" /F >nul 2>&1
timeout /t 2 >nul

echo Launching Ollama server...
start "" /B ollama serve
timeout /t 3 >nul

echo Loading the model (llama3.2:1b)...
ollama run llama3.2:1b "ready" >nul

echo.
ollama ps
echo.
echo ============================================================
echo   Ollama is up. The model is loaded and kept warm (Forever).
echo   Leave this running while you develop / demo.
echo ============================================================
pause
