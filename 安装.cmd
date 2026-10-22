@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set "INSTALL_EXIT_CODE=%ERRORLEVEL%"
if not "%INSTALL_EXIT_CODE%"=="0" echo Installation did not complete. Read the error above before trying again.
pause
exit /b %INSTALL_EXIT_CODE%
