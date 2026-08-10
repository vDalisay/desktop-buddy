# Parallel Customization Worktrees

The Environment Customization and Buddy Studio branches are intended to run concurrently on one Windows development machine without sharing source/build/runtime state.

## One-command setup

From the normal `desktop-buddy` checkout after pulling `main`:

```bat
tools\setup_parallel_customization_worktrees.bat
```

The setup is idempotent. It fetches the prepared branches, creates local tracking branches when needed, reuses an existing worktree if that branch is already checked out elsewhere, and refuses to overwrite a non-empty unrelated directory.

Default worktrees:

```text
..\desktop-buddy-environment  -> environment-customization
..\desktop-buddy-studio       -> buddy-studio
```

Custom destinations can be supplied to the PowerShell script:

```powershell
.\tools\setup_parallel_customization_worktrees.ps1 `
  -EnvironmentPath D:\Dev\desktop-buddy-environment `
  -BuddyStudioPath D:\Dev\desktop-buddy-studio
```

## Runtime/save isolation

Each worktree receives a local, untracked `override.cfg`. Godot automatically loads `res://override.cfg`, so the normal `tools\play_game.bat` launcher requires no feature-specific changes.

Environment worktree:

```text
user:// -> %APPDATA%\DesktopBuddy\Dev\EnvironmentCustomization
```

Buddy Studio worktree:

```text
user:// -> %APPDATA%\DesktopBuddy\Dev\BuddyStudio
```

The original game checkout continues using its normal Desktop Buddy user-data directory.

`override.cfg` is added to the repository's local Git exclude file by the setup script. It must never be committed.

This isolates, per worktree:

- checked-out source;
- `.godot/` import/editor cache;
- `bin/` and `obj/` outputs;
- logs/artifacts generated in the checkout;
- `user://` progress/settings/character data.

Both feature builds can therefore launch at the same time without writing the same save files.

## Agent working directories

Environment agent:

```text
<repo-parent>\desktop-buddy-environment
```

Buddy Studio agent:

```text
<repo-parent>\desktop-buddy-studio
```

An agent should build, test, commit, and run the game only from its assigned worktree. Do not switch either worktree to the sibling branch while both agents are active.

## Source-of-truth branch rules

Both feature branches were prepared from the same shared customization foundation. Their implementation boundaries remain defined by:

- `docs/CUSTOMIZATION_PARALLEL_IMPLEMENTATION_FOUNDATION.md`
- `docs/ENVIRONMENT_CUSTOMIZATION_AGENT_HANDOFF.md` on `environment-customization`
- `docs/BUDDY_STUDIO_AGENT_HANDOFF.md` on `buddy-studio`

The worktree setup changes only local checkout/runtime isolation. It does not change the domain ownership boundaries in those documents.

## Cleanup

When parallel development is finished, first ensure each worktree has no uncommitted work. Then remove it from the normal checkout with standard Git worktree commands, for example:

```bat
git worktree remove ..\desktop-buddy-environment
git worktree remove ..\desktop-buddy-studio
git worktree prune
```

Removing a worktree does not automatically delete its `%APPDATA%\DesktopBuddy\Dev\...` test save directory. Keeping those test saves is normally useful for regression testing; delete them manually only when an intentionally clean development profile is wanted.
