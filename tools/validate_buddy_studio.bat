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
if not exist .artifacts\buddy-studio mkdir .artifacts\buddy-studio

echo [1/11] Building solution...
call dotnet build DesktopBuddy.sln -c Debug --no-restore -m:1
if errorlevel 1 goto :failed

echo [2/11] Running domain tests, including schema migration and transform validation...
call dotnet test tests\DesktopBuddy.Domain.Tests\DesktopBuddy.Domain.Tests.csproj -c Debug --no-build --no-restore
if errorlevel 1 goto :failed

echo [3/11] Importing Godot project...
"%GODOT_HEADLESS%" --headless --path . --import --quit-after 1 --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\buddy-studio\import.log"
if errorlevel 1 goto :failed

echo [4/11] Checking trusted cosmetic anchors, render layers, paint preservation, and physics isolation...
call :scenario character_rig_view
if errorlevel 1 goto :failed

echo [5/11] Checking alternative Eyes, Eyebrows, and Mouth expression coverage...
call :scenario expression_renderer_coverage
if errorlevel 1 goto :failed

echo [6/11] Checking live character appearance swaps preserve the physics invariant...
call :scenario character_swap_physics_invariant
if errorlevel 1 goto :failed

echo [7/11] Checking permanent ownership, unowned preview, Save gating, Cancel, and restart restoration...
call :scenario buddy_studio_ownership_preview
if errorlevel 1 goto :failed

echo [8/11] Checking twelve-category deterministic owned/free Randomize...
call :scenario buddy_studio_randomize
if errorlevel 1 goto :failed

echo [9/11] Checking Studio composition, catalogue, purchase/equip, transforms, focus, and dirty-close behavior...
call :scenario buddy_studio_ui_composition
if errorlevel 1 goto :failed

echo [10/11] Running character save/use/restart plus Paint Buddy preservation journey...
call :scenario character_editor_create_use_and_react
if errorlevel 1 goto :failed

echo [11/11] Checking normal-boot Customize ^> Buddy Studio registration and opening...
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\buddy-studio\startup.log" -- --buddy-studio-startup-check
if errorlevel 1 goto :failed

echo Buddy Studio closure validation passed.
popd
exit /b 0

:scenario
set "SCENARIO=%~1"
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\buddy-studio\%SCENARIO%.log" -- --scenario=%SCENARIO% --seed=1 --artifacts=.artifacts\buddy-studio\%SCENARIO%
exit /b %ERRORLEVEL%

:failed
echo Buddy Studio closure validation failed with exit code %ERRORLEVEL%.
popd
exit /b 1

:help
echo Builds the solution, runs domain tests, imports Godot, then runs the focused Buddy Studio closure scenarios and normal-boot startup probe.
echo Includes semantic expression coverage and the live character-swap physics invariant.
echo Artifacts and Godot logs are written under .artifacts\buddy-studio.
echo This is the automated gate before the final Buddy Studio Windows DPI/visual/interaction owner review.
exit /b 0
