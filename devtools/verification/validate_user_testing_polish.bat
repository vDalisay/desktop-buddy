@echo off
setlocal

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0..\.."
call "%~dp0..\..\tools\resolve_godot.bat"
if errorlevel 1 (
  echo.
  echo Godot could not be resolved.
  echo.
  pause
  exit /b %ERRORLEVEL%
)

set "GODOT_HEADLESS=%GODOT_EXE%"
echo %GODOT_EXE% | findstr /I /R "_console\.exe$" >nul
if errorlevel 1 set "GODOT_HEADLESS=%GODOT_EXE:.exe=_console.exe%"
if not exist "%GODOT_HEADLESS%" set "GODOT_HEADLESS=%GODOT_EXE%"

pushd "%PROJECT_ROOT%"
if not exist .artifacts\user-testing-polish mkdir .artifacts\user-testing-polish

echo [1/11] Building solution...
call dotnet build DesktopBuddy.sln -c Debug --no-restore -m:1
if errorlevel 1 goto :failed

echo [2/11] Running domain tests for paint modifiers, fill, environment paint, and presentation response...
call dotnet test tests\DesktopBuddy.Domain.Tests\DesktopBuddy.Domain.Tests.csproj -c Debug --no-build --no-restore
if errorlevel 1 goto :failed

echo [3/11] Importing Godot project and validating autoload/script composition...
"%GODOT_HEADLESS%" --headless --path . --import --quit-after 1 --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\user-testing-polish\import.log"
if errorlevel 1 goto :failed

echo [4/11] Checking Paint Buddy trusted UV mapping...
call :scenario paint_frontal_uv_mapping
if errorlevel 1 goto :failed

echo [5/11] Checking Show limbs target mapping and exact restoration...
call :scenario paint_limb_pose_mapping
if errorlevel 1 goto :failed

echo [6/11] Checking semantic paint toolbar composition...
call :scenario paint_toolbar_icons
if errorlevel 1 goto :failed

echo [7/11] Checking Buddy Studio user-testing catalogue, preview, ownership, and save UX...
call :scenario buddy_studio_ui_composition
if errorlevel 1 goto :failed

echo [8/11] Checking unified Catalogue buy/equip behavior...
call :scenario shop_panel_purchase
if errorlevel 1 goto :failed

echo [9/11] Checking room decorator user-testing closure behavior...
call :scenario environment_decorator_room_build
if errorlevel 1 goto :failed

echo [10/11] Checking Work Mode resize/resilience/exit behavior...
call :scenario work_mode_resilience
if errorlevel 1 goto :failed

echo [11/11] Checking live 3D presentation remains coherent after rotation polish...
call :scenario presentation_3d
if errorlevel 1 goto :failed

echo.
echo User-testing polish automated closure passed.
echo Logs and artifacts: .artifacts\user-testing-polish
echo A final manual Paint Buddy / Paint Background / Catalogue / Decorate Room / Work feel pass is still required.
echo.
pause
popd
exit /b 0

:scenario
set "SCENARIO=%~1"
"%GODOT_HEADLESS%" --headless --path . --rendering-driver opengl3 --log-file "%PROJECT_ROOT%\.artifacts\user-testing-polish\%SCENARIO%.log" -- --scenario=%SCENARIO% --seed=1 --artifacts=.artifacts\user-testing-polish\%SCENARIO%
exit /b %ERRORLEVEL%

:failed
set "FAIL_CODE=%ERRORLEVEL%"
echo.
echo User-testing polish validation failed with exit code %FAIL_CODE%.
echo Logs and artifacts: .artifacts\user-testing-polish
echo.
pause
popd
exit /b 1

:help
echo Builds the solution, runs all domain tests, imports Godot, and executes the focused Section 1-7 user-testing closure scenarios.
echo Logs and artifacts are written under .artifacts\user-testing-polish.
echo Run this before the final owner manual feel/interaction pass and before starting Potion Shop.
exit /b 0
