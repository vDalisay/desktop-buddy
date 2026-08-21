[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExportDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$ExportDirectory = [System.IO.Path]::GetFullPath($ExportDirectory)
$ExePath = Join-Path $ExportDirectory "DesktopBuddyDemo.exe"

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "Protected-demo validation failed: DesktopBuddyDemo.exe is missing."
}

$Files = @(Get-ChildItem -LiteralPath $ExportDirectory -Recurse -File)
if ($Files.Count -eq 0) {
    throw "Protected-demo validation failed: export directory is empty."
}

$ForbiddenExtensions = @(".pdb", ".cs", ".csproj", ".sln", ".props", ".targets", ".md", ".ps1", ".bat")
$ForbiddenFiles = @($Files | Where-Object { $ForbiddenExtensions -contains $_.Extension.ToLowerInvariant() })
if ($ForbiddenFiles.Count -gt 0) {
    throw "Protected-demo validation failed: source/debug/build files leaked into the package: $($ForbiddenFiles.FullName -join ', ')"
}

$LoosePcks = @($Files | Where-Object { $_.Extension -ieq ".pck" })
if ($LoosePcks.Count -gt 0) {
    throw "Protected-demo validation failed: a loose .pck exists even though the protected preset must embed it: $($LoosePcks.FullName -join ', ')"
}

$ForbiddenSegments = @("tests", "docs", "devtools", "authoring", "tools", "artifacts", "mcp", ".github", ".codex", ".mcp", ".protected")
$LeakedPaths = @()
$RootPrefix = $ExportDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($File in $Files) {
    $Relative = if ($File.FullName.StartsWith($RootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        $File.FullName.Substring($RootPrefix.Length)
    } else {
        $File.Name
    }
    $Segments = $Relative -split '[\\/]'
    if ($Segments | Where-Object { $ForbiddenSegments -contains $_.ToLowerInvariant() }) {
        $LeakedPaths += $Relative
    }
}
if ($LeakedPaths.Count -gt 0) {
    throw "Protected-demo validation failed: developer-only directory content leaked into the package: $($LeakedPaths -join ', ')"
}

foreach ($AssemblyName in @("DesktopBuddy.dll", "DesktopBuddy.Domain.dll", "DesktopBuddy.Visuals.dll")) {
    $Matches = @($Files | Where-Object { $_.Name -ceq $AssemblyName })
    if ($Matches.Count -ne 1) {
        throw "Protected-demo validation failed: expected exactly one $AssemblyName after obfuscation; found $($Matches.Count)."
    }
}

$PresetText = Get-Content -LiteralPath (Join-Path $ProjectRoot "export_presets.cfg") -Raw
$ProtectedStart = $PresetText.IndexOf("[preset.1]", [System.StringComparison]::Ordinal)
if ($ProtectedStart -lt 0) {
    throw "Protected-demo validation failed: protected export preset is missing."
}
$ProtectedPreset = $PresetText.Substring($ProtectedStart)
foreach ($Required in @(
    'encryption_include_filters="*"',
    'encrypt_pck=true',
    'encrypt_directory=true',
    'binary_format/embed_pck=true',
    'dotnet/include_scripts_content=false',
    'dotnet/include_debug_symbols=false',
    'debug/export_console_wrapper=0'
)) {
    if ($ProtectedPreset.IndexOf($Required, [System.StringComparison]::Ordinal) -lt 0) {
        throw "Protected-demo validation failed: protected preset no longer contains '$Required'."
    }
}

$ObfuscationModePath = Join-Path $ProjectRoot ".protected/obfuscar/last-mode.txt"
if (-not (Test-Path -LiteralPath $ObfuscationModePath -PathType Leaf)) {
    throw "Protected-demo validation failed: managed obfuscation completion marker is missing."
}
$Mode = (Get-Content -LiteralPath $ObfuscationModePath -Raw).Trim()
if ($Mode -notin @("private-api", "conservative-main")) {
    throw "Protected-demo validation failed: unexpected obfuscation mode '$Mode'."
}

$ExeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ExePath).Hash.ToLowerInvariant()
$TotalBytes = ($Files | Measure-Object Length -Sum).Sum
Write-Host "Protected Steam-demo package validation passed."
Write-Host "  mode: $Mode"
Write-Host "  files: $($Files.Count)"
Write-Host "  bytes: $TotalBytes"
Write-Host "  exe sha256: $ExeHash"
