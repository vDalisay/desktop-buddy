using System;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Persistence;
using Godot;

namespace DesktopBuddy.Environment;

public partial class EnvironmentDecorator
{
    /// <summary>
    /// Compatibility composition seam for Initial Demo and existing startup scenarios. It adapts
    /// the legacy aggregate into the same focused persistence capability used by Scene builds; the
    /// editor itself no longer knows about BuddyProgressState or SaveCoordinator.
    /// </summary>
    public void Configure(
        BuddyProgressState progress,
        EconomyService economy,
        LabPointerGrabComponent pointer,
        CanvasItem buddy2D,
        Node3D buddy3D,
        EnvironmentProgressState state,
        SaveCoordinator saves,
        EnvironmentDecorationLayer visuals)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(saves);
        Configure(
            economy,
            pointer,
            buddy2D,
            buddy3D,
            new LegacyEnvironmentProgressPersistence(progress, state, saves),
            visuals);
    }
}
