@echo off
setlocal
set "release_version=%~1"
if not defined release_version set "release_version=1.0.1"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish.ps1" -Version "%release_version%" -OpenOutputFolder
set "publish_result=%errorlevel%"
echo.
if not "%publish_result%"=="0" echo Publish failed. See the error above.
pause
exit /b %publish_result%
