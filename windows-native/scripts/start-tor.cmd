@echo off
setlocal
cd /d "%~dp0"
if /i "%~1"=="onion" (
  tor.exe --defaults-torrc config\defaults-torrc -f config\torrc-onion-service
) else (
  tor.exe --defaults-torrc config\defaults-torrc -f config\torrc
)
exit /b %ERRORLEVEL%
