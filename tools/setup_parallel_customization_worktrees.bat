@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%setup_parallel_customization_worktrees.ps1" %*
exit /b %ERRORLEVEL%
