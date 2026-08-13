@echo off
setlocal

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0..\.."
call "%~dp0..\..\tools\resolve_godot.bat"
if errorlevel 1 exit /b %ERRORLEVEL%

set "GODOT_HEADLESS=%GODOT_EXE%"
echo %GODOT_EXE% | findstr /I /R "_console\.exe$" >nul
if errorlevel 1 set "GODOT_HEADLESS=%GODOT_EXE:.exe=_console.exe%"
if not exist "%GODOT_HEADLESS%" set "GODOT_HEADLESS=%GODOT_EXE%"

pushd "%PROJECT_ROOT%"
if not exist .artifacts\environment mkdir .artifacts\environment

echo [1/9] Building solution...
call dotnet build DesktopBuddy.sln -c Debug --no-restore -m:1
if errorlevel 1 goto :failed

echo [2/9] Running domain tests...
call dotnet test tests\DesktopBuddy.Domain.Tests\DesktopBuddy.Domain.Tests.csproj -c Debug --no-build --no-restore
if errorlevel 1 goto :failed

echo [3/9] Importing Godot project...
"%GODOT_HEADLESS%" --headless --path . --import --quit-after 1 --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\environment\import.log"
if errorlevel 1 goto :failed

echo [4/9] Checking Environment launch catalogue and render contracts...
call :scenario environment_decor_catalogue
if errorlevel 1 goto :failed

echo [5/9] Checking Environment purchase, ownership, cancel, rotation, and wallpaper contracts...
call :scenario environment_decor_purchase_per_instance
if errorlevel 1 goto :failed

echo [6/9] Checking Environment free placement, anchors, snap, and resize mapping...
call :scenario environment_decor_free_placement
if errorlevel 1 goto :failed

echo [7/9] Checking Environment save/restart restoration...
call :scenario environment_decor_restart_restore
if errorlevel 1 goto :failed

echo [8/9] Running in-scene Environment Decorator room-build closure...
call :scenario environment_decorator_room_build
if errorlevel 1 goto :failed

echo [9/9] Checking semantic paint-toolbar icon closure...
call :scenario paint_toolbar_icons
if errorlevel 1 goto :failed

echo Environment closure validation passed.
popd
exit /b 0

:scenario
set "SCENARIO=%~1"
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\environment\%SCENARIO%.log" -- --scenario=%SCENARIO% --seed=1 --artifacts=.artifacts\environment\%SCENARIO%
exit /b %ERRORLEVEL%

:failed
echo Environment closure validation failed with exit code %ERRORLEVEL%.
popd
exit /b 1

:help
echo Builds the solution, runs domain tests, imports Godot, then runs the focused Environment/paint closure scenarios.
echo Artifacts and Godot logs are written under .artifacts\environment.
echo This is the automated gate before the ED6 + PAINT-R7 manual DPI/feel review.
exit /b 0
