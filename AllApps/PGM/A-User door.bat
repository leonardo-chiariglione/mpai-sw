@echo off
rem PGM-AUA, the A-User App "Open the door when the bell rings" (M3313): the Host User is an A-User App - its User Agent and the A-User Module of PGM-AUA (1PGM-ATU-V1.0-I01) on the AIF Controller - in the
rem M-Instance of a room with one door (the reference software of MPAI-MMM). It starts Ollama (the language model of Basic Knowledge), the M-Instance and the Host, and opens the page in the browser.
rem On the page: "Enter the room", send or speak the command when it says "The A-User is ready.", then let the Visitor knock or ring. Ctrl+C in this window stops it.
call "%~dp0..\_support\_settings.bat"
call "%~dp0..\_support\_ollama.bat"
call "%~dp0..\MMM\Run-Door.cmd"
if errorlevel 1 pause
