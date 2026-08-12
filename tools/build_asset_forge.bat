@echo off
setlocal
set "PROJECT_ROOT=%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sync_asset_forge_profiles.ps1"
if errorlevel 1 exit /b %ERRORLEVEL%
pushd "%PROJECT_ROOT%"
echo [asset_forge] Building deterministic Core tests...
dotnet test "devtools\AssetForge.Core.Tests\DesktopBuddy.AssetForge.Core.Tests.csproj" -c Debug
if errorlevel 1 (set "RESULT=%ERRORLEVEL%" & popd & exit /b %RESULT%)
echo [asset_forge] Building Asset Forge executable...
dotnet build "devtools\AssetForge\DesktopBuddy.AssetForge.csproj" -c Debug
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
