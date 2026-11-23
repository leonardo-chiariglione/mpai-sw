@echo off
rem COMPANY PERFORMANCE PREDICTION: you pick the file of a company and the horizon; it gives the probability of default and of discontinuity, and which figures weigh most (a demonstration model trained on Polish companies, not for real companies).
rem It starts its own Controller on port 5063 for the run (the report is kept in Downloads\MPAI CUI\CPP).
call "%~dp0..\_support\_settings.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\_support\_cui.ps1"
if errorlevel 1 pause
