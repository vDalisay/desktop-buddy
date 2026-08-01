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

echo [1/35] Building solution...
call dotnet build DesktopBuddy.sln -c Debug --no-restore -m:1
if errorlevel 1 goto :failed

echo [2/35] Running domain tests...
call dotnet test tests\DesktopBuddy.Domain.Tests\DesktopBuddy.Domain.Tests.csproj -c Debug --no-build --no-restore
if errorlevel 1 goto :failed

echo [3/35] Importing Godot project...
"%GODOT_HEADLESS%" --headless --path . --import --quit-after 1 --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\import.log"
if errorlevel 1 goto :failed

echo [4/35] Checking laboratory controls...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\laboratory_controls.log" -- --scenario=laboratory_controls --seed=1 --artifacts=.artifacts\quick\laboratory_controls
if errorlevel 1 goto :failed

echo [5/35] Checking grab and throw...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\grab_release.log" -- --scenario=grab_release --seed=1 --artifacts=.artifacts\quick\grab_release
if errorlevel 1 goto :failed

echo [6/35] Checking grab hang orientation...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\grab_hang_orientation.log" -- --scenario=grab_hang_orientation --seed=1 --artifacts=.artifacts\quick\grab_hang_orientation
if errorlevel 1 goto :failed

echo [7/35] Checking grab swing pendulum...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\grab_swing_pendulum.log" -- --scenario=grab_swing_pendulum --seed=1 --artifacts=.artifacts\quick\grab_swing_pendulum
if errorlevel 1 goto :failed

echo [8/35] Checking room resize and zoom...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\room_resize_zoom.log" -- --scenario=room_resize_zoom --seed=1 --artifacts=.artifacts\quick\room_resize_zoom
if errorlevel 1 goto :failed

echo [9/35] Running lab spawn/settle journey...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\lab_spawn_settle.log" -- --journey=lab_spawn_settle --seed=1 --artifacts=.artifacts\quick\lab_spawn_settle
if errorlevel 1 goto :failed

echo [10/35] Running grab-throw journey...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\lab_grab_throw.log" -- --journey=lab_grab_throw --seed=1 --artifacts=.artifacts\quick\lab_grab_throw
if errorlevel 1 goto :failed

echo [11/35] Checking dual-profile composition...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\dual_profile_smoke.log" -- --scenario=dual_profile_smoke --seed=1 --artifacts=.artifacts\quick\dual_profile_smoke
if errorlevel 1 goto :failed

echo [12/35] Checking M4 object catch/hold...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\object_catch_hold.log" -- --scenario=object_catch_hold --seed=1 --artifacts=.artifacts\quick\object_catch_hold
if errorlevel 1 goto :failed

echo [13/35] Checking M4 behavior priority ladder...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\behavior_priority_ladder.log" -- --scenario=behavior_priority_ladder --seed=1 --artifacts=.artifacts\quick\behavior_priority_ladder
if errorlevel 1 goto :failed

echo [14/35] Checking M4 hidden lifecycle accrual...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\hidden_clock_accrual.log" -- --scenario=hidden_clock_accrual --seed=1 --artifacts=.artifacts\quick\hidden_clock_accrual
if errorlevel 1 goto :failed

echo [15/35] Running M4 care/persistence relaunch journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\care_persistence.log" -- --journey=care_persistence --seed=424242 --artifacts=.artifacts\quick\care_persistence
if errorlevel 1 goto :failed

echo [16/35] Checking clean-catch laugh and fun interest...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\fun_catch_laugh.log" -- --scenario=fun_catch_laugh --seed=1 --artifacts=.artifacts\quick\fun_catch_laugh
if errorlevel 1 goto :failed

echo [17/35] Checking M5 Baseball purchase and pullback launch...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\baseball_pullback.log" -- --scenario=baseball_pullback --seed=1 --artifacts=.artifacts\quick\baseball_pullback
if errorlevel 1 goto :failed

echo [18/35] Checking cornered ground pickup...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\corner_scoop.log" -- --scenario=corner_scoop --seed=1 --artifacts=.artifacts\quick\corner_scoop
if errorlevel 1 goto :failed

echo [19/35] Checking the FR-014 loose-object budget...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\object_budget.log" -- --scenario=object_budget --seed=1 --artifacts=.artifacts\quick\object_budget
if errorlevel 1 goto :failed

echo [20/35] Checking the M5 Meal consume and cooldown...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\meal_consume.log" -- --scenario=meal_consume --seed=1 --artifacts=.artifacts\quick\meal_consume
if errorlevel 1 goto :failed

echo [21/35] Running the M5 Meal journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_meal.log" -- --journey=m5_meal --seed=1 --artifacts=.artifacts\quick\m5_meal
if errorlevel 1 goto :failed

echo [22/35] Checking the M5 Baseball Bat swing...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\bat_swing.log" -- --scenario=bat_swing --seed=1 --artifacts=.artifacts\quick\bat_swing
if errorlevel 1 goto :failed

echo [23/35] Running the M5 Baseball Bat journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_baseball_bat.log" -- --journey=m5_baseball_bat --seed=1 --artifacts=.artifacts\quick\m5_baseball_bat
if errorlevel 1 goto :failed

echo [24/35] Running the M5 Home-Run Bat journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_homerun_bat.log" -- --journey=m5_homerun_bat --seed=1 --artifacts=.artifacts\quick\m5_homerun_bat
if errorlevel 1 goto :failed

echo [25/35] Checking the M5 Pistol cadence, reload, and shot...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\pistol_fire.log" -- --scenario=pistol_fire --seed=1 --artifacts=.artifacts\quick\pistol_fire
if errorlevel 1 goto :failed

echo [26/35] Running the M5 Pistol journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_pistol.log" -- --journey=m5_pistol --seed=1 --artifacts=.artifacts\quick\m5_pistol
if errorlevel 1 goto :failed

echo [27/35] Checking the M5 Grenade pin, fuse, and blast...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\grenade_fuse.log" -- --scenario=grenade_fuse --seed=1 --artifacts=.artifacts\quick\grenade_fuse
if errorlevel 1 goto :failed

echo [28/35] Running the M5 Grenade journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_grenade.log" -- --journey=m5_grenade --seed=1 --artifacts=.artifacts\quick\m5_grenade
if errorlevel 1 goto :failed

echo [29/35] Checking the M5 Fire Sprayer stream and Burning...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\burning_status.log" -- --scenario=burning_status --seed=1 --artifacts=.artifacts\quick\burning_status
if errorlevel 1 goto :failed

echo [30/35] Running the M5 Fire Sprayer journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_fire_sprayer.log" -- --journey=m5_fire_sprayer --seed=1 --artifacts=.artifacts\quick\m5_fire_sprayer
if errorlevel 1 goto :failed

echo [31/35] Checking the M5 Soccer Ball bounce and the Drink's care rules...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\soccer_and_drink.log" -- --scenario=soccer_and_drink --seed=1 --artifacts=.artifacts\quick\soccer_and_drink
if errorlevel 1 goto :failed

echo [32/35] Running the M5 Soccer Ball journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_soccer_ball.log" -- --journey=m5_soccer_ball --seed=1 --artifacts=.artifacts\quick\m5_soccer_ball
if errorlevel 1 goto :failed

echo [33/35] Running the M5 Drink journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_drink.log" -- --journey=m5_drink --seed=1 --artifacts=.artifacts\quick\m5_drink
if errorlevel 1 goto :failed

echo [34/35] Checking the M5 Shotgun spread, dedup, and shell eject...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\shotgun_spread.log" -- --scenario=shotgun_spread --seed=1 --artifacts=.artifacts\quick\shotgun_spread
if errorlevel 1 goto :failed

echo [35/35] Running the M5 Shotgun journey...
"%GODOT_HEADLESS%" --headless --fixed-fps 120 --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\quick\m5_shotgun.log" -- --journey=m5_shotgun --seed=1 --artifacts=.artifacts\quick\m5_shotgun
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
