param(
    [string]$ExpectedSha256 = $env:DESKTOP_BUDDY_GODOTSTEAM_SHA256,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ManifestPath = Join-Path $PSScriptRoot 'godotsteam-dependency.json'
$Manifest = Get-Content -Raw $ManifestPath | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($ExpectedSha256) -and -not [string]::IsNullOrWhiteSpace($Manifest.archiveSha256)) {
    $ExpectedSha256 = [string]$Manifest.archiveSha256
}
if ([string]::IsNullOrWhiteSpace($ExpectedSha256)) {
    throw 'GodotSteam archive SHA-256 is required. Set DESKTOP_BUDDY_GODOTSTEAM_SHA256 or pass -ExpectedSha256. Unverified native dependencies are never installed.'
}
$ExpectedSha256 = $ExpectedSha256.Trim().ToUpperInvariant()
if ($ExpectedSha256 -notmatch '^[0-9A-F]{64}$') {
    throw 'Expected GodotSteam SHA-256 must contain exactly 64 hexadecimal characters.'
}

$Deps = Join-Path $RepoRoot '.deps\godotsteam'
$Archive = Join-Path $Deps ("godotsteam-{0}.zip" -f $Manifest.version)
$Extract = Join-Path $Deps 'extract'
$Target = Join-Path $RepoRoot ([string]$Manifest.expectedAddonDirectory -replace '/', '\')

New-Item -ItemType Directory -Force $Deps | Out-Null
if ($Force -or -not (Test-Path $Archive)) {
    Write-Host "Downloading GodotSteam $($Manifest.version) revision $($Manifest.sourceRevision)..."
    Invoke-WebRequest -UseBasicParsing -Uri $Manifest.sourceArchiveUrl -OutFile $Archive
}

$Actual = (Get-FileHash -Algorithm SHA256 $Archive).Hash.ToUpperInvariant()
if ($Actual -ne $ExpectedSha256) {
    Remove-Item -Force $Archive -ErrorAction SilentlyContinue
    throw "GodotSteam archive hash mismatch. Expected $ExpectedSha256 but received $Actual. The archive was deleted."
}

Remove-Item -Recurse -Force $Extract -ErrorAction SilentlyContinue
Expand-Archive -Path $Archive -DestinationPath $Extract -Force

# Asset Library archives have changed their outer directory name over time. Locate exactly one
# project-ready addon rather than baking that wrapper name into the repo.
$Candidates = @(Get-ChildItem -Path $Extract -Directory -Recurse | Where-Object {
    $_.FullName.Replace('\','/').EndsWith('/addons/godotsteam') -and
    (Test-Path (Join-Path $_.FullName 'godotsteam.gdextension'))
})
if ($Candidates.Count -ne 1) {
    throw "Expected exactly one addons/godotsteam directory containing godotsteam.gdextension; found $($Candidates.Count)."
}

if (Test-Path $Target) {
    if (-not $Force) {
        throw "Target '$Target' already exists. Re-run with -Force to replace the local dependency."
    }
    Remove-Item -Recurse -Force $Target
}
New-Item -ItemType Directory -Force (Split-Path -Parent $Target) | Out-Null
Copy-Item -Recurse -Force $Candidates[0].FullName $Target

Write-Host "Installed verified GodotSteam $($Manifest.version) into $Target"
Write-Host 'This directory is intentionally gitignored. Do not commit Valve/GodotSteam binaries.'
