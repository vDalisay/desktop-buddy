[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$PresetPath = Join-Path $ProjectRoot "export_presets.cfg"
$GitignorePath = Join-Path $ProjectRoot ".gitignore"
$TemplateBuilderPath = Join-Path $PSScriptRoot "build_encrypted_template.ps1"
$ObfuscatorPath = Join-Path $PSScriptRoot "obfuscate_export.ps1"

function Require-Text {
    param([string]$Text, [string]$Needle, [string]$Description)
    if ($Text.IndexOf($Needle, [System.StringComparison]::Ordinal) -lt 0) {
        throw "Protected-demo hardening guard failed: $Description (missing '$Needle')."
    }
}

$Presets = Get-Content -LiteralPath $PresetPath -Raw
$ProtectedStart = $Presets.IndexOf("[preset.1]", [System.StringComparison]::Ordinal)
if ($ProtectedStart -lt 0) {
    throw "Protected-demo hardening guard failed: preset.1 is missing."
}
$Protected = $Presets.Substring($ProtectedStart)

$RequiredProtectedSettings = [ordered]@{
    'name="Windows Steam Demo Protected"' = "protected preset name"
    'encryption_include_filters="*"' = "per-file PCK encryption wildcard"
    'encrypt_pck=true' = "PCK encryption"
    'encrypt_directory=true' = "PCK directory encryption"
    'custom_template/release=".protected/templates/windows_release_x86_64.exe"' = "keyed custom release template"
    'binary_format/embed_pck=true' = "embedded PCK"
    'debug/export_console_wrapper=0' = "release console wrapper removal"
    'dotnet/include_scripts_content=false' = ".NET source-content stripping"
    'dotnet/include_debug_symbols=false' = ".NET symbol stripping"
    'dotnet/embed_build_outputs=false' = "post-export assembly hardening seam"
}
foreach ($Pair in $RequiredProtectedSettings.GetEnumerator()) {
    Require-Text $Protected $Pair.Key $Pair.Value
}

$Normal = $Presets.Substring(0, $ProtectedStart)
Require-Text $Normal 'name="Windows Desktop"' "normal development export preset"
Require-Text $Normal 'encrypt_pck=false' "normal preset must remain independent from protected encryption"
Require-Text $Normal 'binary_format/embed_pck=false' "normal preset must remain independent from protected packing"

$Gitignore = Get-Content -LiteralPath $GitignorePath -Raw
Require-Text $Gitignore '/.protected/' "local protected cache/key ignore"
Require-Text $Gitignore '*.gdkey' "Godot key-file ignore"

$TemplateBuilder = Get-Content -LiteralPath $TemplateBuilderPath -Raw
Require-Text $TemplateBuilder 'module_mono_enabled=yes' "custom template must keep Godot .NET support"
Require-Text $TemplateBuilder 'production=yes' "production-optimized custom template"
Require-Text $TemplateBuilder 'SCRIPT_AES256_ENCRYPTION_KEY' "compile-time PCK key injection"
Require-Text $TemplateBuilder '4.6.1-stable' "pinned Godot template source"

$Obfuscator = Get-Content -LiteralPath $ObfuscatorPath -Raw
Require-Text $Obfuscator 'Obfuscar.GlobalTool' "managed obfuscator installation"
Require-Text $Obfuscator '2.2.50' "pinned stable Obfuscar version"
Require-Text $Obfuscator '<Var name="KeepPublicApi" value="true" />' "public Godot/save API preservation"
Require-Text $Obfuscator '<Var name="HidePrivateApi" value="true" />' "private implementation renaming"
Require-Text $Obfuscator '<Var name="HideStrings" value="false" />' "zero-overhead string policy"
Require-Text $Obfuscator '<Var name="OptimizeMethods" value="false" />' "no obfuscator method rewriting"

if (Get-Command git -ErrorAction SilentlyContinue) {
    $TrackedProtected = @(& git -C $ProjectRoot ls-files -- .protected)
    if ($LASTEXITCODE -ne 0) {
        throw "Protected-demo hardening guard failed while checking tracked .protected files."
    }
    if ($TrackedProtected.Count -gt 0) {
        throw "Protected-demo hardening guard failed: .protected must remain untracked. Found: $($TrackedProtected -join ', ')"
    }
}

Write-Host "Protected Steam-demo hardening configuration guard passed."
