@echo off
call "%~dp0..\_support\_settings.bat"
rem A GROUP'S MEETING in the MMM world: the people of the group are taken from AllApps\MMM\Registry\people.json and each is sent an email, through Outlook, with their personal link. See AllApps\MMM\Run-Meeting.ps1.
rem   ask   shows who will be mailed and waits for your yes (the default);  draft  saves the emails in Outlook as drafts, nothing is sent;  send  sends without asking;  none  no emails.
set GROUP=MMM
set /p GROUP=Group (Enter for MMM): 
set WHEN=
set /p WHEN=When, as the emails say it (for example Thursday 29 October, 17:00): 
set MAILMODE=ask
set /p MAILMODE=Emails - ask, draft, send or none (Enter for ask): 
if "%GROUP%"=="" set GROUP=MMM
if "%MAILMODE%"=="" set MAILMODE=ask
call "%~dp0Run-Meeting.bat" -Group %GROUP% -When "%WHEN%" -Mail %MAILMODE%
pause
