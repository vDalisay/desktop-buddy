using System;

namespace DesktopBuddy.Buddy.Behavior;

public partial class ToolReactionComponent
{
    /// <summary>
    /// Clears acute guard/tickle state when the authored compatibility puppet begins representing a
    /// different persistent Buddy. Persistent learned harm is read from the newly rebound damage
    /// pipeline on the next routed tick; only the outgoing actor's transient performance is reset.
    /// </summary>
    public void ResetForSceneRebind()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("ToolReactionComponent cannot reset before initialization.");

        _guardDirection = Godot.Vector2.Right;
        _guardAimPoint = Godot.Vector2.Zero;
        _guardAimInitialized = false;
        _gloveDefenseLatched = false;
        _tickleReachLatched = false;
        Intent = default;
        Buddy.SetToolReactionIntent(default);
    }
}
