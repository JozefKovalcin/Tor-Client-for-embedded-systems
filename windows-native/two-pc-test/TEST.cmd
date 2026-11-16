@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0test.ps1"
set "TEST_EXIT=%ERRORLEVEL%"
pause
exit /b %TEST_EXIT%
