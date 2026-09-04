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

rem Steamworks needs an AppID hint when the editor/game is launched directly instead of by Steam.
rem Keep it development-only: .gitignore excludes it and this launcher deletes the file it creates.
set "STEAM_APPID_FILE=%PROJECT_ROOT%\steam_appid.txt"
set "CREATED_STEAM_APPID_FILE=0"
if not exist "%STEAM_APPID_FILE%" (
    >"%STEAM_APPID_FILE%" echo %DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID%
    set "CREATED_STEAM_APPID_FILE=1"
)

echo [Steam Workshop] Runtime AppID:  %DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID%
echo [Steam Workshop] Workshop owner: %DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID%
echo [Steam Workshop] Development AppID hint: %STEAM_APPID_FILE%
echo [Steam Workshop] Launching Desktop Buddy with the verified local GodotSteam addon.

call "%~dp0play_game.bat" %*
set "RESULT=%ERRORLEVEL%"

if "%CREATED_STEAM_APPID_FILE%"=="1" del /q "%STEAM_APPID_FILE%" >nul 2>&1
exit /b %RESULT%

:help
echo Launches Desktop Buddy for a local Steam/GodotSteam Workshop development smoke test.
echo.
echo Requirements:
echo   - Steam client running and signed in with access to Desktop Buddy AppID 5114950
echo   - pinned Godot 4.6.1 editor discoverable by the normal play_game.bat rules
echo.
echo The script materializes the pinned GodotSteam 4.22 addon when missing and defaults both
echo the runtime and Workshop-owner AppIDs to 5114950. It also creates the gitignored
echo steam_appid.txt hint Steamworks requires for direct development launches, then deletes the
echo file again when the game closes. Future demo testing can override:
echo   DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID=^<demo AppID^>
echo   DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID=5114950
echo.
echo For persistent logs during live Workshop verification, use play_game_steam_diagnostics.bat.
echo Valve/GodotSteam binaries and steam_appid.txt are never committed or shipped by this launcher.
exit /b 0
