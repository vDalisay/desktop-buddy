@echo off
setlocal
set "PROJECT_ROOT=%~dp0.."
pushd "%PROJECT_ROOT%"
echo [asset_forge] Regenerating all saved Asset Forge authoring content...
call dotnet run --project "devtools\AssetForge.Cli\DesktopBuddy.AssetForge.Cli.csproj" -c Debug -- --regenerate-all "%PROJECT_ROOT%"
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
