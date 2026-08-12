@echo off
setlocal
set "PROJECT_ROOT=%~dp0.."
call "%~dp0build_asset_forge.bat"
if errorlevel 1 exit /b %ERRORLEVEL%
call "%~dp0resolve_godot.bat"
if errorlevel 1 exit /b %ERRORLEVEL%
"%GODOT_EXE%" --path "%PROJECT_ROOT%\devtools\AssetForge" %*
exit /b %ERRORLEVEL%
