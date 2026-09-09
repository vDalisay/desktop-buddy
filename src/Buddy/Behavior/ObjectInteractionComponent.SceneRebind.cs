using System;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Buddy.Behavior;

public partial class ObjectInteractionComponent
{
    /// <summary>
    /// Rebinds this already-composed actor to another Buddy identity. Held objects, refusals,
    /// soccer/catch commitments and consume transactions belong to the outgoing live Scene and are
    /// discarded before the target Buddy starts using the shared room object registry.
    /// </summary>
    public void RebindProgress(BuddyRuntimeProgressBinding progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!IsInitialized)
            throw new InvalidOperationException("ObjectInteractionComponent cannot rebind before initialization.");

        Reset();
        _progress = progress;
        _isHarmful = progress.IsContentHarmful;
    }
}
