@echo off
setlocal

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0.."
call "%~dp0..\tools\resolve_godot.bat"
if errorlevel 1 exit /b %ERRORLEVEL%

rem Normal play must use the visible editor binary even when GODOT_PATH points to the console build.
set "GODOT_GUI_EXE=%GODOT_EXE:_console.exe=.exe%"
if /I not "%GODOT_GUI_EXE%"=="%GODOT_EXE%" if exist "%GODOT_GUI_EXE%" set "GODOT_EXE=%GODOT_GUI_EXE%"

pushd "%PROJECT_ROOT%"
"%GODOT_EXE%" --path "%PROJECT_ROOT%" %*
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%

:help
echo Launches the real game (main scene, transparent desktop shell, Work Mode).
echo Extra arguments are passed through, e.g. --presentation=legacy.
echo Uses GODOT_PATH or auto-discovers the pinned editor. See README.md for the search order.
exit /b 0
