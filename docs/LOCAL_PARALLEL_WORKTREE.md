# Buddy Studio — Local Parallel Worktree

This branch is intended to run concurrently with `environment-customization` from a separate Git worktree.

Expected default local directory after running the setup script from the normal `main` checkout:

```text
..\desktop-buddy-studio
```

The setup script is on `main`:

```bat
tools\setup_parallel_customization_worktrees.bat
```

It creates an untracked `override.cfg` in this worktree so Godot resolves:

```text
user:// -> %APPDATA%\DesktopBuddy\Dev\BuddyStudio
```

Use this worktree for all Buddy Studio builds, tests, commits, and `tools\play_game.bat` launches. Do not switch this worktree to `environment-customization` while both agents are active.

`override.cfg` is local development state and must never be committed. The setup script adds it to the repository's local Git exclude file.

The authoritative feature/file ownership rules remain in `docs/BUDDY_STUDIO_AGENT_HANDOFF.md` and `docs/CUSTOMIZATION_PARALLEL_IMPLEMENTATION_FOUNDATION.md`.
