[CmdletBinding()]
param(
    [string]$EnvironmentPath,
    [string]$BuddyStudioPath
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-BranchWorktreePath {
    param([Parameter(Mandatory = $true)][string]$Branch)

    $currentPath = $null
    foreach ($line in (& git worktree list --porcelain)) {
        if ($line.StartsWith("worktree ", [System.StringComparison]::Ordinal)) {
            $currentPath = $line.Substring(9)
            continue
        }

        if ($line -eq "branch refs/heads/$Branch") {
            return $currentPath
        }
    }

    return $null
}

function Ensure-LocalBranch {
    param([Parameter(Mandatory = $true)][string]$Branch)

    & git show-ref --verify --quiet "refs/heads/$Branch"
    if ($LASTEXITCODE -eq 0) {
        return
    }

    & git show-ref --verify --quiet "refs/remotes/origin/$Branch"
    if ($LASTEXITCODE -ne 0) {
        throw "origin/$Branch does not exist. Fetch/push the prepared parallel branch first."
    }

    Invoke-Git branch --track $Branch "origin/$Branch"
}

function Ensure-Worktree {
    param(
        [Parameter(Mandatory = $true)][string]$Branch,
        [Parameter(Mandatory = $true)][string]$RequestedPath
    )

    $existing = Get-BranchWorktreePath -Branch $Branch
    if ($existing) {
        Write-Host "[$Branch] already checked out at: $existing"
        return [System.IO.Path]::GetFullPath($existing)
    }

    $fullPath = [System.IO.Path]::GetFullPath($RequestedPath)
    if (Test-Path -LiteralPath $fullPath) {
        $entries = @(Get-ChildItem -LiteralPath $fullPath -Force -ErrorAction Stop)
        if ($entries.Count -gt 0) {
            throw "Refusing to create $Branch worktree: '$fullPath' already exists and is not empty."
        }
    }

    Write-Host "[$Branch] creating worktree: $fullPath"
    Invoke-Git worktree add $fullPath $Branch
    return $fullPath
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Write-IsolatedGodotOverride {
    param(
        [Parameter(Mandatory = $true)][string]$WorktreePath,
        [Parameter(Mandatory = $true)][string]$UserDirectoryName
    )

    $overridePath = Join-Path $WorktreePath "override.cfg"
    $content = @"
[application]

config/use_custom_user_dir=true
config/custom_user_dir_name="$UserDirectoryName"
"@
    Write-Utf8NoBom -Path $overridePath -Text $content
    Write-Host "  Godot user:// isolated as: %APPDATA%\$($UserDirectoryName -replace '/', '\')"
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$repoParent = Split-Path -Parent $repoRoot

if ([string]::IsNullOrWhiteSpace($EnvironmentPath)) {
    $EnvironmentPath = Join-Path $repoParent "desktop-buddy-environment"
}
if ([string]::IsNullOrWhiteSpace($BuddyStudioPath)) {
    $BuddyStudioPath = Join-Path $repoParent "desktop-buddy-studio"
}

Push-Location $repoRoot
try {
    $topLevel = (& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($topLevel)) {
        throw "This script must be run from a Desktop Buddy Git checkout."
    }

    Write-Host "Fetching prepared parallel branches..."
    Invoke-Git fetch origin environment-customization buddy-studio

    Ensure-LocalBranch -Branch "environment-customization"
    Ensure-LocalBranch -Branch "buddy-studio"

    # override.cfg is deliberately local-only. Godot reads res://override.cfg automatically,
    # so each worktree can keep using tools/play_game.bat while resolving user:// elsewhere.
    $commonDir = (& git rev-parse --git-common-dir).Trim()
    if (-not [System.IO.Path]::IsPathRooted($commonDir)) {
        $commonDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $commonDir))
    }
    $excludePath = Join-Path $commonDir "info\exclude"
    $excludeDirectory = Split-Path -Parent $excludePath
    [System.IO.Directory]::CreateDirectory($excludeDirectory) | Out-Null
    $exclude = if (Test-Path -LiteralPath $excludePath) {
        [System.IO.File]::ReadAllText($excludePath)
    } else {
        ""
    }
    if ($exclude -notmatch '(?m)^/override\.cfg\s*$') {
        if ($exclude.Length -gt 0 -and -not $exclude.EndsWith("`n")) {
            $exclude += "`r`n"
        }
        $exclude += "/override.cfg`r`n"
        Write-Utf8NoBom -Path $excludePath -Text $exclude
    }

    $environmentWorktree = Ensure-Worktree `
        -Branch "environment-customization" `
        -RequestedPath $EnvironmentPath
    $studioWorktree = Ensure-Worktree `
        -Branch "buddy-studio" `
        -RequestedPath $BuddyStudioPath

    Write-IsolatedGodotOverride `
        -WorktreePath $environmentWorktree `
        -UserDirectoryName "DesktopBuddy/Dev/EnvironmentCustomization"
    Write-IsolatedGodotOverride `
        -WorktreePath $studioWorktree `
        -UserDirectoryName "DesktopBuddy/Dev/BuddyStudio"

    Write-Host ""
    Write-Host "Parallel customization workspaces are ready."
    Write-Host "Environment : $environmentWorktree"
    Write-Host "Buddy Studio: $studioWorktree"
    Write-Host ""
    Write-Host "Each agent can run tools\play_game.bat from its own worktree concurrently."
    Write-Host "The source tree, .godot cache, bin/obj outputs, logs, and user:// saves are isolated."
    Write-Host "Your normal DesktopBuddy save directory is not used by either development workspace."
}
finally {
    Pop-Location
}
