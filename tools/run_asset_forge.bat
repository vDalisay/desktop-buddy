@echo off
setlocal
set "PROJECT_ROOT=%~dp0.."
call "%~dp0build_asset_forge.bat"
if errorlevel 1 goto :done
call "%~dp0resolve_godot.bat"
if errorlevel 1 goto :done
"%GODOT_EXE%" --path "%PROJECT_ROOT%\devtools\AssetForge" %*

:done
set "RESULT=%ERRORLEVEL%"
echo.
echo [asset_forge] Exit code %RESULT%.
pause
exit /b %RESULT%
