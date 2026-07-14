@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "PROJECT_ROOT=%~dp0.."
call "%~dp0resolve_godot.bat"
if errorlevel 1 exit /b %ERRORLEVEL%

set "GODOT_PATH=%GODOT_EXE%"
set "MCP_ENTRY="

if defined GODOT_MCP_PATH (
  call :try "%GODOT_MCP_PATH%"
  call :try "%GODOT_MCP_PATH%\build\index.js"
  call :try "%GODOT_MCP_PATH%\dist\index.js"
  if not defined MCP_ENTRY (
    >&2 echo GODOT_MCP_PATH does not contain a built MCP entrypoint: "%GODOT_MCP_PATH%"
    exit /b 2
  )
)

if not defined MCP_ENTRY call :try "%PROJECT_ROOT%\..\mcp\godot-mcp\build\index.js"
if not defined MCP_ENTRY call :try "%PROJECT_ROOT%\Mcp\godot-mcp\build\index.js"
if not defined MCP_ENTRY call :try "%PROJECT_ROOT%\Mcp\godot-mcp-runtime\dist\index.js"

if not defined MCP_ENTRY (
  >&2 echo A built Godot MCP server was not found.
  >&2 echo Checked the repository-adjacent mcp\godot-mcp checkout and the in-project Mcp fallbacks.
  >&2 echo Build the server or set GODOT_MCP_PATH, then try again. See README.md.
  exit /b 2
)

if /I "%~1"=="--check" (
  where node >nul 2>&1
  if errorlevel 1 (
    >&2 echo Node.js was not found on PATH.
    exit /b 2
  )
  for /f "delims=" %%V in ('node --version') do set "NODE_VERSION=%%V"
  echo Godot: %GODOT_PATH%
  echo Godot MCP: %MCP_ENTRY%
  echo Node.js: !NODE_VERSION!
  exit /b 0
)

node "%MCP_ENTRY%"
exit /b %ERRORLEVEL%

:try
if defined MCP_ENTRY exit /b 0
if exist "%~1\*" exit /b 0
if exist "%~1" for %%M in ("%~1") do set "MCP_ENTRY=%%~fM"
exit /b 0
