@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
set "UNINSTALL_EXIT_CODE=%ERRORLEVEL%"
if not "%UNINSTALL_EXIT_CODE%"=="0" echo Uninstall did not complete. Read the error above before trying again.
pause
exit /b %UNINSTALL_EXIT_CODE%
