@echo off
setlocal
if not defined MSYS2_ROOT set "MSYS2_ROOT=C:\msys64"
if not exist "%MSYS2_ROOT%\usr\bin\bash.exe" (
  echo ERROR: Install MSYS2 as documented in windows-native\BUILD.md or set MSYS2_ROOT.
  exit /b 1
)
set "MSYSTEM=UCRT64"
set "CHERE_INVOKING=1"
set "TOR_WINDOWS_SCRIPT=%~dp0build-windows.sh"
"%MSYS2_ROOT%\usr\bin\bash.exe" -lc "exec bash \"$(cygpath -u \"$TOR_WINDOWS_SCRIPT\")\" \"$@\"" tor-build %*
exit /b %ERRORLEVEL%
