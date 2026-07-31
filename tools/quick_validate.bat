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

echo [1/26] Building solution...
call dotnet build DesktopBuddy.sln -c Debug --no-restore -m:1
if errorlevel 1 goto :failed

echo [2/26] Running domain tests...
call dotnet test tests\DesktopBuddy.Domain.Tests\DesktopBuddy.Domain.Tests.csproj -c Debug --no-build --no-restore
if errorlevel 1 goto :failed

echo [3/26] Importing Godot project...
"%GODOT_HEADLESS%" --headless --path . --import --quit-after 1 --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\import.log"
if errorlevel 1 goto :failed

echo [4/26] Checking laboratory controls...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\laboratory_controls.log" -- --scenario=laboratory_controls --seed=1 --artifacts=.artifacts\quick\laboratory_controls
if errorlevel 1 goto :failed

echo [5/26] Checking grab and throw...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\grab_release.log" -- --scenario=grab_release --seed=1 --artifacts=.artifacts\quick\grab_release
if errorlevel 1 goto :failed

echo [6/26] Checking grab hang orientation...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\grab_hang_orientation.log" -- --scenario=grab_hang_orientation --seed=1 --artifacts=.artifacts\quick\grab_hang_orientation
if errorlevel 1 goto :failed

echo [7/26] Checking grab swing pendulum...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\grab_swing_pendulum.log" -- --scenario=grab_swing_pendulum --seed=1 --artifacts=.artifacts\quick\grab_swing_pendulum
if errorlevel 1 goto :failed

echo [8/26] Checking room resize and zoom...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\room_resize_zoom.log" -- --scenario=room_resize_zoom --seed=1 --artifacts=.artifacts\quick\room_resize_zoom
if errorlevel 1 goto :failed

echo [9/26] Running lab spawn/settle journey...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\lab_spawn_settle.log" -- --journey=lab_spawn_settle --seed=1 --artifacts=.artifacts\quick\lab_spawn_settle
if errorlevel 1 goto :failed

echo [10/26] Running grab-throw journey...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\lab_grab_throw.log" -- --journey=lab_grab_throw --seed=1 --artifacts=.artifacts\quick\lab_grab_throw
if errorlevel 1 goto :failed

echo [11/26] Checking dual-profile composition...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\dual_profile_smoke.log" -- --scenario=dual_profile_smoke --seed=1 --artifacts=.artifacts\quick\dual_profile_smoke
if errorlevel 1 goto :failed

echo [12/26] Checking M4 object catch/hold...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\object_catch_hold.log" -- --scenario=object_catch_hold --seed=1 --artifacts=.artifacts\quick\object_catch_hold
if errorlevel 1 goto :failed

echo [13/26] Checking M4 behavior priority ladder...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\behavior_priority_ladder.log" -- --scenario=behavior_priority_ladder --seed=1 --artifacts=.artifacts\quick\behavior_priority_ladder
if errorlevel 1 goto :failed

echo [14/26] Checking M4 hidden lifecycle accrual...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\hidden_clock_accrual.log" -- --scenario=hidden_clock_accrual --seed=1 --artifacts=.artifacts\quick\hidden_clock_accrual
if errorlevel 1 goto :failed

echo [15/26] Running M4 care/persistence relaunch journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\care_persistence.log" -- --journey=care_persistence --seed=424242 --artifacts=.artifacts\quick\care_persistence
if errorlevel 1 goto :failed

echo [16/26] Checking clean-catch laugh and fun interest...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\fun_catch_laugh.log" -- --scenario=fun_catch_laugh --seed=1 --artifacts=.artifacts\quick\fun_catch_laugh
if errorlevel 1 goto :failed

echo [17/26] Checking M5 Baseball purchase and pullback launch...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\baseball_pullback.log" -- --scenario=baseball_pullback --seed=1 --artifacts=.artifacts\quick\baseball_pullback
if errorlevel 1 goto :failed

echo [18/26] Checking cornered ground pickup...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\corner_scoop.log" -- --scenario=corner_scoop --seed=1 --artifacts=.artifacts\quick\corner_scoop
if errorlevel 1 goto :failed

echo [19/26] Checking the FR-014 loose-object budget...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\object_budget.log" -- --scenario=object_budget --seed=1 --artifacts=.artifacts\quick\object_budget
if errorlevel 1 goto :failed

echo [20/26] Checking the M5 Meal consume and cooldown...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\meal_consume.log" -- --scenario=meal_consume --seed=1 --artifacts=.artifacts\quick\meal_consume
if errorlevel 1 goto :failed

echo [21/26] Running the M5 Meal journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_meal.log" -- --journey=m5_meal --seed=1 --artifacts=.artifacts\quick\m5_meal
if errorlevel 1 goto :failed

echo [22/26] Checking the M5 Baseball Bat swing...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\bat_swing.log" -- --scenario=bat_swing --seed=1 --artifacts=.artifacts\quick\bat_swing
if errorlevel 1 goto :failed

echo [23/26] Running the M5 Baseball Bat journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_baseball_bat.log" -- --journey=m5_baseball_bat --seed=1 --artifacts=.artifacts\quick\m5_baseball_bat
if errorlevel 1 goto :failed

echo [24/26] Running the M5 Home-Run Bat journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_homerun_bat.log" -- --journey=m5_homerun_bat --seed=1 --artifacts=.artifacts\quick\m5_homerun_bat
if errorlevel 1 goto :failed

echo [25/26] Checking the M5 Pistol cadence, reload, and shot...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\pistol_fire.log" -- --scenario=pistol_fire --seed=1 --artifacts=.artifacts\quick\pistol_fire
if errorlevel 1 goto :failed

echo [26/26] Running the M5 Pistol journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_pistol.log" -- --journey=m5_pistol --seed=1 --artifacts=.artifacts\quick\m5_pistol
if errorlevel 1 goto :failed

echo Quick validation passed.
popd
exit /b 0

:failed
echo Quick validation failed with exit code %ERRORLEVEL%.
popd
exit /b 1

:help
echo Builds the project and runs the fast implemented-milestone unit/headless checks.
echo Uses GODOT_PATH or auto-discovers the pinned editor. See README.md for the search order.
exit /b 0
