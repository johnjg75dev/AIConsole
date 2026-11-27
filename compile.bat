@echo off
setlocal
set "SCRIPT=%~dp0compile.py"

where py >nul 2>nul
if %errorlevel%==0 (
  py -3 "%SCRIPT%" %*
) else (
  python "%SCRIPT%" %*
)

set "CODE=%errorlevel%"
if "%~1"=="" (
  echo.
  pause
)
exit /b %CODE%
