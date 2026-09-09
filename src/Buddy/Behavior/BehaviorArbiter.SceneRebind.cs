using System;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Buddy.Behavior;

public partial class BehaviorArbiter
{
    /// <summary>
    /// Rebinds the authored actor to another Buddy's semantic state without re-running node
    /// composition. The arbiter model is transient Scene state, so switching Buddies resets its
    /// ladder history and starts the target actor from a clean decision tick.
    /// </summary>
    public void RebindProgress(BuddyRuntimeProgressBinding progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!IsInitialized)
            throw new InvalidOperationException("BehaviorArbiter cannot rebind before initialization.");

        _progress = progress;
        _model.Reset();
        Intent = default;
        DriveIntent = default;
    }
}
