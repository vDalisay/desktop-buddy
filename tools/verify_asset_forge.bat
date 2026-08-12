@echo off
setlocal
set "PROJECT_ROOT=%~dp0.."
pushd "%PROJECT_ROOT%"
echo [asset_forge] Re-deriving all authored assets without Godot...
dotnet run --project "devtools\AssetForge.Cli\DesktopBuddy.AssetForge.Cli.csproj" -c Debug -- --verify-all "%PROJECT_ROOT%"
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
