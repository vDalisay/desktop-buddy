using System;
using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.App;

public enum GameplayPauseReason
{
    HiddenToTray,
    Suspended,
    CharacterEditor,
}

/// <summary>
/// Sole writer of SceneTree.Paused. Callers own reasons, not the aggregate pause flag, so
/// editor exit cannot accidentally resume a still-hidden or suspended application.
/// </summary>
public sealed class GameplayPauseCoordinator
{
    private readonly SceneTree _tree;
    private readonly HashSet<GameplayPauseReason> _reasons = [];

    public GameplayPauseCoordinator(SceneTree tree)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
    }

    public event Action<bool>? Changed;

    public bool IsPaused => _reasons.Count > 0;
    public IReadOnlyCollection<GameplayPauseReason> Reasons => _reasons;
    public int MutationCount { get; private set; }

    public void Set(GameplayPauseReason reason, bool active)
    {
        bool changed = active ? _reasons.Add(reason) : _reasons.Remove(reason);
        if (!changed)
            return;

        bool paused = _reasons.Count > 0;
        _tree.Paused = paused;
        MutationCount++;
        Changed?.Invoke(paused);
    }

    public bool Contains(GameplayPauseReason reason) => _reasons.Contains(reason);
}
