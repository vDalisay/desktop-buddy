@echo off
setlocal

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0.."
pushd "%PROJECT_ROOT%"

echo [build_game] Building current C# sources...
call dotnet build "%PROJECT_ROOT%\DesktopBuddy.csproj" --configuration Debug --nologo --verbosity minimal
set "RESULT=%ERRORLEVEL%"
if "%RESULT%"=="0" call "%~dp0import_assets.bat"

echo.
if "%RESULT%"=="0" (
  echo [build_game] Build succeeded.
) else (
  echo [build_game] Build failed with code %RESULT%.
)
echo.
pause
popd
exit /b %RESULT%

:help
echo Builds the current C# sources without launching Godot.
echo Run devtools\play_game.bat afterwards to launch the game.
exit /b 0
