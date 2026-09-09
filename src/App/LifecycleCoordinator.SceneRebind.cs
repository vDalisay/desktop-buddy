using System;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.App;

public partial class LifecycleCoordinator
{
    /// <summary>
    /// Moves the compatibility lifecycle clock from the outgoing roster-head Buddy to the target
    /// roster-head Buddy. The current bucket is settled against the outgoing binding first; no mood,
    /// hunger, fun recharge or account time can leak across the Scene boundary.
    /// </summary>
    public void RebindBuddyProgress(BuddyRuntimeProgressBinding progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!IsInitialized || _shuttingDown)
            throw new InvalidOperationException("Lifecycle cannot rebind outside an active run.");

        SettleCurrentBucket();
        _progress = progress;
        _clock.Reset();
        _pendingSeconds = 0.0;
    }
}
