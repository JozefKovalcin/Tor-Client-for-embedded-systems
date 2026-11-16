@echo off
setlocal
title Tor - native Windows source compilation
if not defined MSYS2_ROOT set "MSYS2_ROOT=C:\msys64"
if not exist "%MSYS2_ROOT%\usr\bin\bash.exe" goto missing_msys
set "MSYSTEM=UCRT64"
set "CHERE_INVOKING=1"
set "TOR_WINDOWS_SCRIPT=%~dp0scripts\compile.sh"
if defined TOR_WINDOWS_WORK goto launch
if not defined TOR_WINDOWS_WORK_WIN set "TOR_WINDOWS_WORK_WIN=%LOCALAPPDATA%\TorNativeBuild"
if not exist "%TOR_WINDOWS_WORK_WIN%" mkdir "%TOR_WINDOWS_WORK_WIN%"
if not exist "%TOR_WINDOWS_WORK_WIN%" goto missing_work
for %%I in ("%TOR_WINDOWS_WORK_WIN%") do set "TOR_WINDOWS_WORK_WIN=%%~sI"
:launch
"%MSYS2_ROOT%\usr\bin\bash.exe" -lc "exec bash \"$(cygpath -u \"$TOR_WINDOWS_SCRIPT\")\" \"$@\"" tor-compile %*
set "TOR_BUILD_EXIT=%ERRORLEVEL%"
if /i "%~1"=="--check-deps" exit /b %TOR_BUILD_EXIT%
if /i "%~1"=="--help" exit /b %TOR_BUILD_EXIT%
if "%TOR_BUILD_EXIT%"=="0" (
  echo.
  echo SUCCESS: %~dp0dist\tor.exe
) else (
  echo.
  echo BUILD FAILED. The error and preserved build-log path are shown above.
)
pause
exit /b %TOR_BUILD_EXIT%
:missing_msys
echo Najprv nainstaluj MSYS2 do C:\msys64 podla README. Potom spusti COMPILE.cmd znova.
echo If it is installed elsewhere, set MSYS2_ROOT to its installation directory.
if /i "%~1"=="--check-deps" exit /b 1
pause
exit /b 1
:missing_work
echo ERROR: Cannot create the build directory: %TOR_WINDOWS_WORK_WIN%
if /i "%~1"=="--check-deps" exit /b 1
pause
exit /b 1
