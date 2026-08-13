@echo off
setlocal

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0.."
call "%~dp0..\tools\resolve_godot.bat"
if errorlevel 1 exit /b %ERRORLEVEL%

pushd "%PROJECT_ROOT%"
set "LAB_SCENE=res://scenes/buddy_lab.tscn"
if /I "%~1"=="--dual" set "LAB_SCENE=res://scenes/dual_profile_lab.tscn"
"%GODOT_EXE%" --path "%PROJECT_ROOT%" %LAB_SCENE% %2 %3 %4 %5
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%

:help
echo Launches the lab directly; pass --dual for the side-by-side profile lab.
echo Uses GODOT_PATH or auto-discovers the pinned editor. See README.md for the search order.
exit /b 0
