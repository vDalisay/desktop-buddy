@echo off
setlocal EnableExtensions

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0.."
call "%~dp0resolve_godot.bat"
if errorlevel 1 goto :resolve_failed

set "LOG_DIR=%PROJECT_ROOT%\artifacts\logs"
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>&1
set "BUILD_LOG=%LOG_DIR%\play_game-build-latest.log"
set "RUNTIME_LOG=%LOG_DIR%\play_game-latest.log"
set "EXIT_FILE=%LOG_DIR%\play_game-exit-code.txt"

echo.
echo [play_game_diagnostics] Project: %PROJECT_ROOT%
echo [play_game_diagnostics] Building current C# sources...
echo [play_game_diagnostics] Build log: %BUILD_LOG%
echo.

pushd "%PROJECT_ROOT%"
dotnet build "%PROJECT_ROOT%\DesktopBuddy.csproj" --configuration Debug --nologo --verbosity minimal -flp:"logfile=%BUILD_LOG%;verbosity=normal"
set "BUILD_RESULT=%ERRORLEVEL%"
if not "%BUILD_RESULT%"=="0" goto :build_failed

echo.
echo [play_game_diagnostics] Build succeeded.
echo [play_game_diagnostics] Launching: %GODOT_EXE%
echo [play_game_diagnostics] Runtime log: %RUNTIME_LOG%
echo.

start "" /wait "%GODOT_EXE%" --verbose --log-file "%RUNTIME_LOG%" --path "%PROJECT_ROOT%" %*
set "RUNTIME_RESULT=%ERRORLEVEL%"
>"%EXIT_FILE%" echo runtime=%RUNTIME_RESULT%

echo.
echo [play_game_diagnostics] Godot exited with code %RUNTIME_RESULT%.
echo [play_game_diagnostics] Runtime log: %RUNTIME_LOG%
echo [play_game_diagnostics] Exit code file: %EXIT_FILE%
echo.
pause
popd
exit /b %RUNTIME_RESULT%

:build_failed
>"%EXIT_FILE%" echo build=%BUILD_RESULT%
echo.
echo [play_game_diagnostics] Build failed with code %BUILD_RESULT%.
echo [play_game_diagnostics] Godot was not launched.
echo [play_game_diagnostics] Build log: %BUILD_LOG%
echo.
pause
popd
exit /b %BUILD_RESULT%

:resolve_failed
echo.
echo [play_game_diagnostics] Godot could not be resolved.
echo.
pause
exit /b 2

:help
echo Builds and launches the game with persistent diagnostics.
echo This launcher always pauses after Godot exits.
echo Build log: artifacts\logs\play_game-build-latest.log
echo Runtime log: artifacts\logs\play_game-latest.log
echo Exit code: artifacts\logs\play_game-exit-code.txt
echo Use tools\play_game.bat for the normal launcher.
exit /b 0
