$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$destination = Join-Path $root 'devtools\AssetForge\.generated\profiles'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$files = @('lab_buddy_look.tres', 'lab_buddy_visual.tres', 'lab_puppet_rig.tres')
foreach ($file in $files) {
    Copy-Item -Force (Join-Path $root "data\buddy\$file") (Join-Path $destination $file)
}
Write-Host "[asset_forge] Synced trusted Buddy preview profiles."
