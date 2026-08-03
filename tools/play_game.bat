@echo off
setlocal

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0.."
call "%~dp0resolve_godot.bat"
if errorlevel 1 exit /b %ERRORLEVEL%

pushd "%PROJECT_ROOT%"
echo [play_game] Building current C# sources...
dotnet build "%PROJECT_ROOT%\DesktopBuddy.csproj" --configuration Debug --nologo --verbosity minimal
if errorlevel 1 (
  echo.
  echo [play_game] Build failed. Godot was not launched.
  echo [play_game] Fix the compiler errors above, then run this file again.
  echo.
  pause
  popd
  exit /b 1
)

echo [play_game] Build succeeded. Launching Godot...
"%GODOT_EXE%" --path "%PROJECT_ROOT%" %*
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%

:help
echo Builds the current C# sources and launches the real game with the normal Godot GUI executable.
echo Extra arguments are passed through, e.g. --presentation=legacy.
echo Uses GODOT_PATH or auto-discovers the pinned editor. See README.md for the search order.
exit /b 0
