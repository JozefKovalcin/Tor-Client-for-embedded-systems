@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Tor-Test.ps1" %*
set "code=%ERRORLEVEL%"
echo.
pause
exit /b %code%
