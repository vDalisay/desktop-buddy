@echo off
setlocal EnableExtensions

if /I "%~1"=="--help" goto :help

set "PROJECT_ROOT=%~dp0.."

where powershell >nul 2>nul
if errorlevel 1 (
    echo [Steam Workshop Diagnostics] Windows PowerShell is required to materialize the verified GodotSteam dependency.
    exit /b 1
)

if not exist "%PROJECT_ROOT%\addons\godotsteam\godotsteam.gdextension" (
    echo [Steam Workshop Diagnostics] Installing verified GodotSteam 4.22 locally...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%\tools\install_godotsteam.ps1"
    if errorlevel 1 exit /b 1
)

tasklist /FI "IMAGENAME eq steam.exe" 2>nul | find /I "steam.exe" >nul
if errorlevel 1 (
    echo [Steam Workshop Diagnostics] Steam is not running.
    echo Start the Steam client and sign in with an account that has developer/test access to Desktop Buddy AppID 5114950, then run this script again.
    exit /b 2
)

if not defined DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID set "DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID=5114950"
if not defined DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID set "DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID=5114950"

set "STAMP_FILE=%PROJECT_ROOT%\artifacts\logs\play_game-steam-environment.txt"
if not exist "%PROJECT_ROOT%\artifacts\logs" mkdir "%PROJECT_ROOT%\artifacts\logs" >nul 2>&1
>"%STAMP_FILE%" echo runtime_app_id=%DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID%
>>"%STAMP_FILE%" echo workshop_owner_app_id=%DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID%
>>"%STAMP_FILE%" echo godotsteam=4.22
>>"%STAMP_FILE%" echo base_game_app_id=5114950

echo [Steam Workshop Diagnostics] Runtime AppID:   %DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID%
echo [Steam Workshop Diagnostics] Workshop owner: %DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID%
echo [Steam Workshop Diagnostics] Environment:    %STAMP_FILE%
echo [Steam Workshop Diagnostics] Runtime log will be written by play_game_diagnostics.bat.
echo.

call "%~dp0play_game_diagnostics.bat" %*
exit /b %ERRORLEVEL%

:help
echo Builds and launches Desktop Buddy with verified GodotSteam 4.22 and persistent Steam-test diagnostics.
echo.
echo Requirements:
echo   - Steam client running and signed in with developer/test access to Desktop Buddy AppID 5114950
echo   - pinned Godot 4.6.1 editor discoverable by the normal project tooling
echo.
echo Output:
echo   artifacts\logs\play_game-build-latest.log
echo   artifacts\logs\play_game-latest.log
echo   artifacts\logs\play_game-exit-code.txt
echo   artifacts\logs\play_game-steam-environment.txt
echo.
echo Defaults:
echo   DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID=5114950
echo   DESKTOP_BUDDY_WORKSHOP_OWNER_APP_ID=5114950
echo.
echo A future demo test can override only DESKTOP_BUDDY_STEAM_RUNTIME_APP_ID while retaining
echo Workshop ownership at 5114950, once that cross-app Steamworks configuration exists.
echo.
echo No steam_appid.txt or Valve/GodotSteam binary is written to source control.
exit /b 0
