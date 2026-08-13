@echo off
setlocal

where dotnet >nul 2>&1
if errorlevel 1 (
  >&2 echo .NET SDK was not found on PATH.
  exit /b 2
)

for /f "delims=" %%V in ('dotnet --version') do set "DOTNET_VERSION=%%V"
echo .NET SDK: %DOTNET_VERSION%

call "%~dp0..\..\tools\start_godot_mcp.bat" --check
if errorlevel 1 exit /b %ERRORLEVEL%

echo Local toolchain check passed.
exit /b 0
