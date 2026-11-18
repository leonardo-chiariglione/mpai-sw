@echo off
call "%~dp0..\_support\_settings.bat"
rem The world of the MMM - the Metaverse Square, the Town Hall with its meeting room - with a meeting code and a public https address (Cloudflare tunnel) for the guests.
rem It asks for the names of the participants (the convener first). See AllApps\MMM\CONFERENCE.md.
call "%~dp0Run-Conference.bat" %*
pause
