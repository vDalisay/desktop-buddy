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
set "BUILD_LOG=!LOG_DIR!\play_game-build-latest.log"
set "LOG_FILE=!LOG_DIR!\play_game-latest.log"

echo.
echo [play_game] Project: !PROJECT_ROOT!
echo [play_game] Building current C# sources...
echo [play_game] Build log: !BUILD_LOG!
echo.

pushd "!PROJECT_ROOT!"
dotnet build "!PROJECT_ROOT!\DesktopBuddy.csproj" --configuration Debug --nologo --verbosity minimal -flp:"logfile=!BUILD_LOG!;verbosity=normal"
set "BUILD_RESULT=!ERRORLEVEL!"
if not "!BUILD_RESULT!"=="0" (
  echo.
  echo [play_game] Build failed with code !BUILD_RESULT!.
  echo [play_game] Godot was not launched, preventing a stale C# assembly from running.
  echo [play_game] Full build log: !BUILD_LOG!
  popd
  endlocal & exit /b %BUILD_RESULT%
)

echo.
echo [play_game] Build succeeded.
echo [play_game] Godot: !GODOT_RUN_EXE!
echo [play_game] Runtime log: !LOG_FILE!
echo [play_game] Dock diagnostics are prefixed with [DockDiagnostics].
echo [play_game] Input diagnostics are prefixed with [InputDiagnostics].
echo.

"!GODOT_RUN_EXE!" --verbose --log-file "!LOG_FILE!" --path "!PROJECT_ROOT!" %*
set "RESULT=!ERRORLEVEL!"
popd

echo.
echo [play_game] Godot exited with code !RESULT!.
echo [play_game] Full runtime log: !LOG_FILE!
endlocal & exit /b %RESULT%

:help
echo Builds and launches the real game while keeping Godot output attached to this terminal.
echo Build output is written to artifacts\logs\play_game-build-latest.log.
echo Runtime output is written to artifacts\logs\play_game-latest.log.
echo The game is not launched when the current C# source fails to compile.
echo Extra arguments are passed through, e.g. --presentation=legacy.
echo Uses GODOT_PATH or auto-discovers the pinned editor. See README.md for the search order.
exit /b 0
