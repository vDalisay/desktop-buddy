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
    echo Start the Steam client and sign in with an account that has access to Desktop Buddy Demo AppID 5228990, then run this script again.
    exit /b 2
)

rem Local source runs use the public Demo scope by default. Match that scope at the Steam layer too:
rem the running app is the Demo, while Demo publishes are mirrored to the full game's Workshop.
rem A caller can still set both variables explicitly before invoking this script for another target.
if not defined DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID set "DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID=5228990"
if not defined DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID set "DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID=5114950"

rem Steamworks needs an AppID hint when the editor/game is launched directly instead of by Steam.
rem Keep it development-only: .gitignore excludes it and this launcher deletes the file it creates.
set "STEAM_APPID_FILE=%PROJECT_ROOT%\steam_appid.txt"
set "CREATED_STEAM_APPID_FILE=0"
if not exist "%STEAM_APPID_FILE%" (
    >"%STEAM_APPID_FILE%" echo %DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID%
    set "CREATED_STEAM_APPID_FILE=1"
)

echo [Steam Workshop] Runtime AppID:  %DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID%
echo [Steam Workshop] Workshop mirror target: %DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID%
echo [Steam Workshop] Development AppID hint: %STEAM_APPID_FILE%
echo [Steam Workshop] Launching Desktop Buddy with the verified local GodotSteam addon.

call "%~dp0play_game.bat" %*
set "RESULT=%ERRORLEVEL%"

if "%CREATED_STEAM_APPID_FILE%"=="1" del /q "%STEAM_APPID_FILE%" >nul 2>&1
exit /b %RESULT%

:help
echo Launches the default Desktop Buddy Demo scope for a local Steam/GodotSteam Workshop smoke test.
echo.
echo Requirements:
echo   - Steam client running and signed in with access to Desktop Buddy Demo AppID 5228990
echo   - pinned Godot 4.6.1 editor discoverable by the normal play_game.bat rules
echo.
echo Defaults:
echo   DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID=5228990
echo   DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID=5114950
echo.
echo This makes local Workshop testing match the Steam Demo: Demo items are primary and are
 echo mirrored to the full-game Workshop. To test the full game's Steam identity instead, set:
echo   DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID=5114950
echo   DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID=5114950
echo before invoking this script.
echo.
echo For persistent logs during live Workshop verification, use play_game_steam_diagnostics.bat.
echo Valve/GodotSteam binaries and steam_appid.txt are never committed or shipped by this launcher.
exit /b 0
