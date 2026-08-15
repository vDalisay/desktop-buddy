@echo off
setlocal
set "PROJECT_ROOT=%~dp0..\.."
rem `call` matters: if dotnet resolves to a .cmd shim, running it without call ends this script.
call powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sync_asset_forge_profiles.ps1"
if errorlevel 1 exit /b 1
pushd "%PROJECT_ROOT%"
echo [asset_forge] Building deterministic Core tests...
call dotnet test "devtools\AssetForge.Core.Tests\DesktopBuddy.AssetForge.Core.Tests.csproj" -c Debug
if errorlevel 1 goto :fail
echo [asset_forge] Building Asset Forge executable...
call dotnet build "devtools\AssetForge\DesktopBuddy.AssetForge.csproj" -c Debug -m:1
if errorlevel 1 goto :fail
popd
exit /b 0

:fail
popd
exit /b 1
