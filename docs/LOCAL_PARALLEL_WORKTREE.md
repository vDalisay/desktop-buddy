# Buddy Studio — Local Parallel Worktree

Buddy Studio development uses the shared parallel-worktree setup documented in:

```text
docs/PARALLEL_CUSTOMIZATION_WORKTREES.md
```

Expected default local directory:

```text
..\desktop-buddy-studio
```

The setup script is run from the normal `main` checkout:

```bat
tools\setup_parallel_customization_worktrees.bat
```

It creates an untracked `override.cfg` in this worktree so Godot resolves:

```text
user:// -> %APPDATA%\DesktopBuddy\Dev\BuddyStudio
```

Use this worktree for Buddy Studio builds, tests, commits and `devtools\play_game.bat`. `override.cfg` is local development state and must never be committed.

The current Buddy Studio implementation requirements remain in `docs/BUDDY_STUDIO_CUSTOMIZATION_PLAN.md` and `docs/BUDDY_STUDIO_AGENT_HANDOFF.md`. Full-release-only custom cosmetic creation, Steam sharing, bounded deformation and the larger Studio UI revamp remain in `docs/BUDDY_STUDIO_FULL_RELEASE_PLAN.md`.
