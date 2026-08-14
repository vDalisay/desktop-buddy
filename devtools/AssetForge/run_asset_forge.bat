@echo off
setlocal
set "PROJECT_ROOT=%~dp0..\.."
rem Startup is intentionally launch-only. Use build_asset_forge.bat when source/tests need rebuilding.
call powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sync_asset_forge_profiles.ps1"
if errorlevel 1 goto :done
call "%~dp0..\..\tools\resolve_godot.bat"
if errorlevel 1 goto :done
"%GODOT_EXE%" --path "%PROJECT_ROOT%\devtools\AssetForge" %*

call "%~dp0..\import_assets.bat"

:done
set "RESULT=%ERRORLEVEL%"
echo.
echo [asset_forge] Exit code %RESULT%.
pause
exit /b %RESULT%
