@echo off
setlocal EnableExtensions DisableDelayedExpansion

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0.."
call "%~dp0resolve_godot.bat"
if errorlevel 1 exit /b %ERRORLEVEL%

rem The normal Windows Godot executable is a GUI-subsystem process and may not keep stdout
rem attached to this terminal. Prefer the companion console executable when the distribution
rem contains it so GD.Print, warnings, errors, and C# diagnostics remain visible here.
for %%G in ("%GODOT_EXE%") do (
  set "GODOT_DIR=%%~dpG"
  set "GODOT_NAME=%%~nG"
)
setlocal EnableDelayedExpansion
set "GODOT_RUN_EXE=!GODOT_EXE!"
set "GODOT_CONSOLE_EXE=!GODOT_DIR!!GODOT_NAME!_console.exe"
if exist "!GODOT_CONSOLE_EXE!" set "GODOT_RUN_EXE=!GODOT_CONSOLE_EXE!"

set "LOG_DIR=!PROJECT_ROOT!\artifacts\logs"
if not exist "!LOG_DIR!" mkdir "!LOG_DIR!" >nul 2>&1
set "LOG_FILE=!LOG_DIR!\play_game-latest.log"

 echo.
 echo [play_game] Godot: !GODOT_RUN_EXE!
 echo [play_game] Project: !PROJECT_ROOT!
 echo [play_game] Log file: !LOG_FILE!
 echo [play_game] Dock diagnostics are prefixed with [DockDiagnostics].
 echo [play_game] Input diagnostics are prefixed with [InputDiagnostics].
 echo.

pushd "!PROJECT_ROOT!"
"!GODOT_RUN_EXE!" --verbose --log-file "!LOG_FILE!" --path "!PROJECT_ROOT!" %*
set "RESULT=!ERRORLEVEL!"
popd

echo.
echo [play_game] Godot exited with code !RESULT!.
echo [play_game] Full log: !LOG_FILE!
endlocal & exit /b %RESULT%

:help
echo Launches the real game and keeps Godot output attached to this terminal.
echo Also writes the latest complete launch log to artifacts\logs\play_game-latest.log.
echo Extra arguments are passed through, e.g. --presentation=legacy.
echo Uses GODOT_PATH or auto-discovers the pinned editor. See README.md for the search order.
exit /b 0
