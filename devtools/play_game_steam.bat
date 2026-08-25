@echo off
setlocal

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0.."

where powershell >nul 2>nul
if errorlevel 1 (
    echo [Steam Workshop] Windows PowerShell is required to materialize the verified GodotSteam dependency.
    exit /b 1
)

if not exist "%PROJECT_ROOT%\addons\godotsteam\godotsteam.gdextension" (
    echo [Steam Workshop] Installing verified GodotSteam 4.22 locally...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%\tools\install_godotsteam.ps1"
    if errorlevel 1 exit /b 1
)

tasklist /FI "IMAGENAME eq steam.exe" 2>nul | find /I "steam.exe" >nul
if errorlevel 1 (
    echo [Steam Workshop] Steam is not running.
    echo Start the Steam client and sign in with an account that has access to Desktop Buddy AppID 5114950, then run this script again.
    exit /b 2
)

if not defined DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID set "DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID=5114950"
if not defined DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID set "DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID=5114950"

echo [Steam Workshop] Runtime AppID:  %DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID%
echo [Steam Workshop] Workshop owner: %DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID%
echo [Steam Workshop] Launching Desktop Buddy with the verified local GodotSteam addon.

call "%~dp0play_game.bat" %*
exit /b %ERRORLEVEL%

:help
echo Launches Desktop Buddy for a local Steam/GodotSteam Workshop development smoke test.
echo.
echo Requirements:
echo   - Steam client running and signed in with access to Desktop Buddy AppID 5114950
echo   - pinned Godot 4.6.1 editor discoverable by the normal play_game.bat rules
echo.
echo The script materializes the pinned GodotSteam 4.22 addon when missing and defaults both
echo the runtime and Workshop-owner AppIDs to 5114950. Future demo testing can override:
echo   DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID=^<demo AppID^>
echo   DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID=5114950
echo.
echo For persistent logs during live Workshop verification, use play_game_steam_diagnostics.bat.
echo No steam_appid.txt or Valve/GodotSteam binary is written to source control.
exit /b 0
