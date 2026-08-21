[CmdletBinding()]
param(
    [string]$GodotPath,
    [string]$GodotSourcePath,
    [Parameter(Mandatory = $true)][string]$EncryptionKey,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$ProtectedRoot = Join-Path $ProjectRoot ".protected"
$TemplateDirectory = Join-Path $ProtectedRoot "templates"
$TemplatePath = Join-Path $TemplateDirectory "windows_release_x86_64.exe"
$FingerprintPath = Join-Path $TemplateDirectory "encryption-key.sha256"

function Invoke-Checked {
    param([Parameter(Mandatory = $true)][string]$FilePath, [Parameter(Mandatory = $true)][string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
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

function Get-KeyFingerprint {
    param([string]$Key)
    $Bytes = [System.Text.Encoding]::UTF8.GetBytes($Key.ToLowerInvariant())
    $Hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $Hash = $Hasher.ComputeHash($Bytes)
        return ([System.BitConverter]::ToString($Hash)).Replace("-", "").ToLowerInvariant()
    } finally {
        $Hasher.Dispose()
    }
}

if ($EncryptionKey -notmatch '^[0-9a-fA-F]{64}$') {
    throw "The PCK encryption key must be exactly 64 hexadecimal characters (256 bits)."
}

if ([string]::IsNullOrWhiteSpace($GodotSourcePath)) {
    $GodotSourcePath = Join-Path $ProtectedRoot "godot-4.6.1-source"
} else {
    $GodotSourcePath = [System.IO.Path]::GetFullPath($GodotSourcePath)
}

$GodotPath = Resolve-GodotPath $GodotPath
if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw "Godot executable not found: $GodotPath"
}

$VersionOutput = (& $GodotPath --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $VersionOutput -notmatch '^4\.6\.1') {
    throw "Protected exports require the pinned Godot 4.6.1 .NET editor. Found: $VersionOutput"
}

$Fingerprint = Get-KeyFingerprint $EncryptionKey
if (-not $Force -and (Test-Path -LiteralPath $TemplatePath) -and (Test-Path -LiteralPath $FingerprintPath)) {
    $ExistingFingerprint = (Get-Content -LiteralPath $FingerprintPath -Raw).Trim()
    if ($ExistingFingerprint -eq $Fingerprint) {
        Write-Host "Encrypted Godot template already matches this key: $TemplatePath"
        return
    }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git is required to obtain the pinned Godot 4.6.1 source."
}
if (-not (Get-Command scons -ErrorAction SilentlyContinue)) {
    throw "SCons is required to compile the protected Godot template. Install it with Python (for example: py -m pip install scons)."
}

New-Item -ItemType Directory -Force -Path $ProtectedRoot, $TemplateDirectory | Out-Null

if (-not (Test-Path -LiteralPath (Join-Path $GodotSourcePath ".git"))) {
    if (Test-Path -LiteralPath $GodotSourcePath) {
        throw "Godot source path exists but is not a Git checkout: $GodotSourcePath"
    }
    Write-Host "Cloning pinned Godot 4.6.1 source into the ignored .protected cache..."
    Invoke-Checked "git" @("clone", "--depth", "1", "--branch", "4.6.1-stable", "https://github.com/godotengine/godot.git", $GodotSourcePath)
}

$Tag = (& git -C $GodotSourcePath describe --tags --exact-match HEAD 2>$null | Out-String).Trim()
if ($Tag -ne "4.6.1-stable") {
    throw "Godot source must be exactly tag 4.6.1-stable. Found: '$Tag'."
}

$PreviousCompileKey = $env:SCRIPT_AES256_ENCRYPTION_KEY
try {
    $env:SCRIPT_AES256_ENCRYPTION_KEY = $EncryptionKey.ToLowerInvariant()

    Write-Host "Generating .NET/Mono glue with the pinned 4.6.1 editor..."
    Push-Location $GodotSourcePath
    try {
        Invoke-Checked $GodotPath @("--headless", "--generate-mono-glue", (Join-Path $GodotSourcePath "modules/mono/glue"))

        Write-Host "Compiling production Windows x86_64 .NET export template with PCK encryption support..."
        Invoke-Checked "scons" @(
            "platform=windows",
            "target=template_release",
            "arch=x86_64",
            "module_mono_enabled=yes",
            "production=yes",
            "d3d12=no"
        )
    } finally {
        Pop-Location
    }

    $TemplateCandidate = Join-Path $GodotSourcePath "bin/godot.windows.template_release.x86_64.mono.exe"
    if (-not (Test-Path -LiteralPath $TemplateCandidate -PathType Leaf)) {
        throw "Godot template compilation completed but the GUI Windows x86_64 .NET template was not found: $TemplateCandidate"
    }

    Copy-Item -LiteralPath $TemplateCandidate -Destination $TemplatePath -Force
    Set-Content -LiteralPath $FingerprintPath -Value $Fingerprint -NoNewline -Encoding ascii

    Write-Host "Protected custom template ready: $TemplatePath"
    Write-Host "Stored only a SHA-256 key fingerprint beside it; the encryption key itself was not copied."
} finally {
    $env:SCRIPT_AES256_ENCRYPTION_KEY = $PreviousCompileKey
}
