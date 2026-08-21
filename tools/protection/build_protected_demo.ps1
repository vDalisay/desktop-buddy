[CmdletBinding()]
param(
    [string]$GodotPath,
    [string]$GodotSourcePath,
    [string]$EncryptionKey,
    [string]$OutputDirectory,
    [switch]$RebuildTemplate,
    [switch]$ConservativeMainAssembly,
    [switch]$SkipSmokeTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    throw "The protected Steam-demo exporter targets Windows and must be run on Windows."
}

$ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$ProtectedRoot = Join-Path $ProjectRoot ".protected"
$KeyPath = Join-Path $ProtectedRoot "pck-encryption.gdkey"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $ProjectRoot "build/steam-demo-protected"
} else {
    $OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
}
$ExePath = Join-Path $OutputDirectory "DesktopBuddyDemo.exe"

function Invoke-Checked {
    param([Parameter(Mandatory = $true)][string]$FilePath, [Parameter(Mandatory = $true)][string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE: $FilePath $($Arguments -join ' ')"
    }
}

function Resolve-GodotPath {
    param([string]$RequestedPath)
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        return [System.IO.Path]::GetFullPath($RequestedPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:GODOT_PATH)) {
        return [System.IO.Path]::GetFullPath($env:GODOT_PATH)
    }
    $Resolver = Join-Path $ProjectRoot "tools/resolve_godot.bat"
    $Output = & cmd.exe /d /c "call `"$Resolver`" && set GODOT_EXE"
    if ($LASTEXITCODE -ne 0) {
        throw "Godot 4.6.1 .NET could not be resolved. Set GODOT_PATH or pass -GodotPath."
    }
    $Line = $Output | Where-Object { $_ -like "GODOT_EXE=*" } | Select-Object -Last 1
    if (-not $Line) {
        throw "tools/resolve_godot.bat did not return GODOT_EXE."
    }
    return [System.IO.Path]::GetFullPath($Line.Substring("GODOT_EXE=".Length))
}

function New-Aes256HexKey {
    $Bytes = New-Object byte[] 32
    $Rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $Rng.GetBytes($Bytes)
        return ([System.BitConverter]::ToString($Bytes)).Replace("-", "").ToLowerInvariant()
    } finally {
        $Rng.Dispose()
    }
}

& (Join-Path $PSScriptRoot "test_hardening_config.ps1")

New-Item -ItemType Directory -Force -Path $ProtectedRoot | Out-Null
if ([string]::IsNullOrWhiteSpace($EncryptionKey)) {
    if (-not [string]::IsNullOrWhiteSpace($env:DESKTOP_BUDDY_PCK_KEY)) {
        $EncryptionKey = $env:DESKTOP_BUDDY_PCK_KEY.Trim()
    } elseif (Test-Path -LiteralPath $KeyPath -PathType Leaf) {
        $EncryptionKey = (Get-Content -LiteralPath $KeyPath -Raw).Trim()
    } else {
        $EncryptionKey = New-Aes256HexKey
        Set-Content -LiteralPath $KeyPath -Value $EncryptionKey -NoNewline -Encoding ascii
        Write-Host "Generated a new 256-bit PCK key in the git-ignored .protected cache. Back up .protected/pck-encryption.gdkey securely."
    }
}
if ($EncryptionKey -notmatch '^[0-9a-fA-F]{64}$') {
    throw "The PCK encryption key must be exactly 64 hexadecimal characters (256 bits)."
}
$EncryptionKey = $EncryptionKey.ToLowerInvariant()

$GodotPath = Resolve-GodotPath $GodotPath
$VersionOutput = (& $GodotPath --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $VersionOutput -notmatch '^4\.6\.1') {
    throw "Protected exports require the pinned Godot 4.6.1 .NET editor. Found: $VersionOutput"
}

$TemplateParams = @{
    GodotPath = $GodotPath
    EncryptionKey = $EncryptionKey
}
if (-not [string]::IsNullOrWhiteSpace($GodotSourcePath)) {
    $TemplateParams.GodotSourcePath = $GodotSourcePath
}
if ($RebuildTemplate) {
    $TemplateParams.Force = $true
}
& (Join-Path $PSScriptRoot "build_encrypted_template.ps1") @TemplateParams

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$PreviousExportKey = $env:GODOT_SCRIPT_ENCRYPTION_KEY
try {
    $env:GODOT_SCRIPT_ENCRYPTION_KEY = $EncryptionKey
    Write-Host "Exporting encrypted/embedded protected Steam demo..."
    Invoke-Checked $GodotPath @(
        "--headless",
        "--path", $ProjectRoot,
        "--export-release", "Windows Steam Demo Protected", $ExePath
    )
} finally {
    $env:GODOT_SCRIPT_ENCRYPTION_KEY = $PreviousExportKey
}

$ObfuscationParams = @{ ExportDirectory = $OutputDirectory }
if ($ConservativeMainAssembly) {
    $ObfuscationParams.ConservativeMainAssembly = $true
}
& (Join-Path $PSScriptRoot "obfuscate_export.ps1") @ObfuscationParams

# The export preset already disables debug symbols. Delete any accidental leftovers before the
# package gate so an editor/plugin regression cannot silently ship symbols.
Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File -Filter "*.pdb" -ErrorAction SilentlyContinue |
    Remove-Item -Force
Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "DesktopBuddy*.xml" } |
    Remove-Item -Force

& (Join-Path $PSScriptRoot "validate_protected_demo.ps1") -ExportDirectory $OutputDirectory

if (-not $SkipSmokeTest) {
    Write-Host "Launching protected executable for a short startup smoke test..."
    $Process = Start-Process -FilePath $ExePath -WorkingDirectory $OutputDirectory -PassThru
    try {
        Start-Sleep -Seconds 8
        if ($Process.HasExited) {
            throw "Protected executable exited during startup smoke test with code $($Process.ExitCode)."
        }
        Write-Host "Protected executable stayed alive through startup smoke test."
    } finally {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
            $Process.WaitForExit(5000) | Out-Null
        }
    }
}

Write-Host "Protected Steam demo is ready at: $OutputDirectory"
Write-Host "Normal development and ordinary Windows export presets were not modified by this build."
