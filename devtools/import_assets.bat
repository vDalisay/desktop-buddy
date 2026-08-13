@echo off
setlocal

rem Imports any new/changed assets (.glb, .png) into the game project.
rem Asset Forge exports raw files; without an import pass Godot cannot load the
rem generated cosmetic catalogue and the game quits at boot with code 2.

if not defined PROJECT_ROOT set "PROJECT_ROOT=%~dp0.."
call "%~dp0..\tools\resolve_godot.bat"
if errorlevel 1 exit /b %ERRORLEVEL%

echo [import_assets] Importing assets into %PROJECT_ROOT%...
"%GODOT_EXE%" --headless --import --path "%PROJECT_ROOT%"
exit /b %ERRORLEVEL%
