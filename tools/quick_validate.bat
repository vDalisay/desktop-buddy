@echo off
setlocal

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0.."
call "%~dp0resolve_godot.bat"
if errorlevel 1 exit /b %ERRORLEVEL%

set "GODOT_HEADLESS=%GODOT_EXE%"
echo %GODOT_EXE% | findstr /I /R "_console\.exe$" >nul
if errorlevel 1 set "GODOT_HEADLESS=%GODOT_EXE:.exe=_console.exe%"
if not exist "%GODOT_HEADLESS%" set "GODOT_HEADLESS=%GODOT_EXE%"

pushd "%PROJECT_ROOT%"
if not exist .artifacts\quick mkdir .artifacts\quick

echo [1/9] Building solution...
call dotnet build DesktopBuddy.sln -c Debug --no-restore -m:1
if errorlevel 1 goto :failed

echo [2/9] Running domain tests...
call dotnet test tests\DesktopBuddy.Domain.Tests\DesktopBuddy.Domain.Tests.csproj -c Debug --no-build --no-restore
if errorlevel 1 goto :failed

echo [3/9] Importing Godot project...
"%GODOT_HEADLESS%" --headless --path . --import --quit-after 1 --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\import.log"
if errorlevel 1 goto :failed

echo [4/9] Checking laboratory controls...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\laboratory_controls.log" -- --scenario=laboratory_controls --seed=1 --artifacts=.artifacts\quick\laboratory_controls
if errorlevel 1 goto :failed

echo [5/9] Checking grab and throw...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\grab_release.log" -- --scenario=grab_release --seed=1 --artifacts=.artifacts\quick\grab_release
if errorlevel 1 goto :failed

echo [6/9] Checking room resize and zoom...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\room_resize_zoom.log" -- --scenario=room_resize_zoom --seed=1 --artifacts=.artifacts\quick\room_resize_zoom
if errorlevel 1 goto :failed

echo [7/9] Running lab spawn/settle journey...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\lab_spawn_settle.log" -- --journey=lab_spawn_settle --seed=1 --artifacts=.artifacts\quick\lab_spawn_settle
if errorlevel 1 goto :failed

echo [8/9] Running grab-throw journey...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\lab_grab_throw.log" -- --journey=lab_grab_throw --seed=1 --artifacts=.artifacts\quick\lab_grab_throw
if errorlevel 1 goto :failed

echo [9/9] Checking dual-profile composition...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\dual_profile_smoke.log" -- --scenario=dual_profile_smoke --seed=1 --artifacts=.artifacts\quick\dual_profile_smoke
if errorlevel 1 goto :failed

echo Quick validation passed.
popd
exit /b 0

:failed
echo Quick validation failed with exit code %ERRORLEVEL%.
popd
exit /b 1

:help
echo Builds the project and runs the fast Milestone 1 unit/headless checks.
echo Uses GODOT_PATH or auto-discovers the pinned editor. See README.md for the search order.
exit /b 0
