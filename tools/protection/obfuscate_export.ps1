[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExportDirectory,
    [switch]$ConservativeMainAssembly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$ExportDirectory = [System.IO.Path]::GetFullPath($ExportDirectory)
$ProtectedRoot = Join-Path $ProjectRoot ".protected"
$ToolDirectory = Join-Path $ProtectedRoot "tools/obfuscar-2.2.50"
$WorkDirectory = Join-Path $ProtectedRoot "obfuscar"
$OutputDirectory = Join-Path $WorkDirectory "output"
$ConfigPath = Join-Path $WorkDirectory "obfuscar.xml"

function Invoke-Checked {
    param([Parameter(Mandatory = $true)][string]$FilePath, [Parameter(Mandatory = $true)][string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE: $FilePath $($Arguments -join ' ')"
    }
}

function Xml-Escape {
    param([string]$Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

if (-not (Test-Path -LiteralPath $ExportDirectory -PathType Container)) {
    throw "Export directory does not exist: $ExportDirectory"
}

$AssemblyDirectories = Get-ChildItem -LiteralPath $ExportDirectory -Recurse -File -Filter "DesktopBuddy.dll" |
    ForEach-Object { $_.Directory.FullName } |
    Where-Object {
        (Test-Path -LiteralPath (Join-Path $_ "DesktopBuddy.Domain.dll")) -and
        (Test-Path -LiteralPath (Join-Path $_ "DesktopBuddy.Visuals.dll"))
    } |
    Select-Object -Unique

if (@($AssemblyDirectories).Count -ne 1) {
    throw "Expected exactly one exported .NET assembly directory containing DesktopBuddy.dll, DesktopBuddy.Domain.dll and DesktopBuddy.Visuals.dll; found $(@($AssemblyDirectories).Count)."
}
$AssemblyDirectory = @($AssemblyDirectories)[0]
$AssemblyNames = @("DesktopBuddy.dll", "DesktopBuddy.Domain.dll", "DesktopBuddy.Visuals.dll")

New-Item -ItemType Directory -Force -Path $ToolDirectory, $WorkDirectory | Out-Null
if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$Obfuscar = Get-ChildItem -LiteralPath $ToolDirectory -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @("obfuscar.console.exe", "obfuscar.console") } |
    Select-Object -First 1
if (-not $Obfuscar) {
    Write-Host "Installing pinned Obfuscar.GlobalTool 2.2.50 into the ignored .protected cache..."
    Invoke-Checked "dotnet" @("tool", "install", "--tool-path", $ToolDirectory, "Obfuscar.GlobalTool", "--version", "2.2.50")
    $Obfuscar = Get-ChildItem -LiteralPath $ToolDirectory -File |
        Where-Object { $_.Name -in @("obfuscar.console.exe", "obfuscar.console") } |
        Select-Object -First 1
}
if (-not $Obfuscar) {
    throw "Obfuscar 2.2.50 installed but its command shim could not be found in $ToolDirectory."
}

$BeforeHashes = @{}
foreach ($Name in $AssemblyNames) {
    $BeforeHashes[$Name] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $AssemblyDirectory $Name)).Hash
}

# Public API names are intentionally retained. Godot's generated bindings and the JSON save contract
# depend on stable externally visible names. We only rename implementation details; string hiding and
# method-body optimization are disabled so obfuscation adds effectively no runtime work.
$MainSafetyRules = ""
if ($ConservativeMainAssembly) {
    $MainSafetyRules = @"
    <SkipMethod type="*" name="*" />
    <SkipField type="*" name="*" />
    <SkipProperty type="*" name="*" />
    <SkipEvent type="*" name="*" />
"@
}

$InputXml = Xml-Escape $AssemblyDirectory
$OutputXml = Xml-Escape $OutputDirectory
$MainXml = Xml-Escape (Join-Path $AssemblyDirectory "DesktopBuddy.dll")
$DomainXml = Xml-Escape (Join-Path $AssemblyDirectory "DesktopBuddy.Domain.dll")
$VisualsXml = Xml-Escape (Join-Path $AssemblyDirectory "DesktopBuddy.Visuals.dll")

$Config = @"
<?xml version="1.0" encoding="utf-8"?>
<Obfuscator>
  <Var name="InPath" value="$InputXml" />
  <Var name="OutPath" value="$OutputXml" />
  <Var name="KeepPublicApi" value="true" />
  <Var name="HidePrivateApi" value="true" />
  <Var name="RenameFields" value="true" />
  <Var name="RenameProperties" value="true" />
  <Var name="RenameEvents" value="true" />
  <Var name="ReuseNames" value="true" />
  <Var name="UseUnicodeNames" value="false" />
  <Var name="HideStrings" value="false" />
  <Var name="OptimizeMethods" value="false" />
  <Var name="SuppressIldasm" value="true" />
  <Module file="$MainXml">
$MainSafetyRules  </Module>
  <Module file="$DomainXml" />
  <Module file="$VisualsXml" />
</Obfuscator>
"@
Set-Content -LiteralPath $ConfigPath -Value $Config -Encoding utf8

Write-Host "Obfuscating Desktop Buddy managed implementation names (public contracts preserved; string hiding disabled)..."
Invoke-Checked $Obfuscar.FullName @($ConfigPath)

$Changed = 0
foreach ($Name in $AssemblyNames) {
    $Obfuscated = Join-Path $OutputDirectory $Name
    if (-not (Test-Path -LiteralPath $Obfuscated -PathType Leaf)) {
        throw "Obfuscar did not produce expected assembly: $Name"
    }
    $AfterHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Obfuscated).Hash
    if ($AfterHash -ne $BeforeHashes[$Name]) {
        $Changed++
    }
    Copy-Item -LiteralPath $Obfuscated -Destination (Join-Path $AssemblyDirectory $Name) -Force
}

if ($Changed -eq 0) {
    throw "Obfuscation completed without changing any Desktop Buddy assembly. Refusing to label this package protected."
}

$Mode = if ($ConservativeMainAssembly) { "conservative-main" } else { "private-api" }
Set-Content -LiteralPath (Join-Path $WorkDirectory "last-mode.txt") -Value $Mode -NoNewline -Encoding ascii
Write-Host "Managed obfuscation complete ($Changed/$($AssemblyNames.Count) assemblies changed, mode=$Mode)."
