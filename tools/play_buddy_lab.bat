@echo off
setlocal

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0.."
set "GODOT_EXE=%GODOT_PATH%"

if not defined GODOT_EXE set "GODOT_EXE=%USERPROFILE%\Downloads\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe"

if not exist "%GODOT_EXE%" (
  echo Godot 4.6.1 .NET was not found.
  echo Set GODOT_PATH to the full Godot executable path, then run this file again.
  exit /b 2
)

pushd "%PROJECT_ROOT%"
set "LAB_SCENE=res://scenes/buddy_lab.tscn"
if /I "%~1"=="--dual" set "LAB_SCENE=res://scenes/dual_profile_lab.tscn"
"%GODOT_EXE%" --path "%PROJECT_ROOT%" %LAB_SCENE% %2 %3 %4 %5
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%

:help
echo Launches the lab directly; pass --dual for the side-by-side profile lab.
echo Uses GODOT_PATH, or the pinned editor under your Downloads folder.
exit /b 0
