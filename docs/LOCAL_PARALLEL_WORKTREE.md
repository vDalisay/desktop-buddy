# Environment Customization — Local Parallel Worktree

This branch is intended to run concurrently with `buddy-studio` from a separate Git worktree.

Expected default local directory after running the setup script from the normal `main` checkout:

```text
..\desktop-buddy-environment
```

The setup script is on `main`:

```bat
tools\setup_parallel_customization_worktrees.bat
```

It creates an untracked `override.cfg` in this worktree so Godot resolves:

```text
user:// -> %APPDATA%\DesktopBuddy\Dev\EnvironmentCustomization
```

Use this worktree for all Environment builds, tests, commits, and `tools\play_game.bat` launches. Do not switch this worktree to `buddy-studio` while both agents are active.

`override.cfg` is local development state and must never be committed. The setup script adds it to the repository's local Git exclude file.

The authoritative feature/file ownership rules remain in `docs/ENVIRONMENT_CUSTOMIZATION_AGENT_HANDOFF.md` and `docs/CUSTOMIZATION_PARALLEL_IMPLEMENTATION_FOUNDATION.md`.
