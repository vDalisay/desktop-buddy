using DesktopBuddy.Work;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Every reference the walkthrough makes to Work Mode, in one place.
///
/// <para><c>src/Work</c> is compiled out of the reduced itch.io distribution, so the controller may
/// not name <see cref="WorkCompanionCoordinator"/> or <see cref="WorkCompanionView"/> anywhere
/// else. That build gets <c>FirstSessionGuidanceController.WorkAbsent.cs</c> instead.</para>
///
/// <para>The Work Mode steps are already absent from the itch step sequence, so none of this is
/// reachable there; the seam exists so the <em>types</em> can leave the assembly.</para>
/// </summary>
public sealed partial class FirstSessionGuidanceController
{
    private WorkCompanionCoordinator? _work;
    private WorkCompanionView? _workView;
    private bool? _workCounterOrigin;

    private void DiscoverWork()
    {
        // Not ??=: the Work companion is destroyed on exit and rebuilt on the next entry, and a
        // freed Godot object is invalid but not null. The stale reference then failed every
        // IsInstanceValid check, so the counter step could never complete and the walkthrough
        // stopped dead on the second visit to Work Mode (owner report 2026-08-20).
        if (!GodotObject.IsInstanceValid(_workView))
        {
            var rediscovered = GetTree().Root.FindChild(nameof(WorkCompanionView), true, false) as WorkCompanionView;
            if (!ReferenceEquals(rediscovered, _workView))
            {
                _workView = rediscovered;
                // A fresh companion starts the counter lesson over: the baseline belonged to the
                // instance that just went away.
                _workCounterOrigin = null;
                _workHelpLayer = null;
            }
        }

        _work ??= GetTree().Root.FindChild(nameof(WorkCompanionCoordinator), true, false) as WorkCompanionCoordinator;
    }

    /// <summary>Start the counter lesson over: the next observation becomes the new baseline.</summary>
    private void ResetWorkCounterBaseline() => _workCounterOrigin = null;

    private bool IsWorkActive() =>
        GodotObject.IsInstanceValid(_work) && _work!.IsActive;

    /// <summary>
    /// The companion's own window, or null before Work is entered. Before then the view still
    /// hangs off the main window, so GetWindow() returns the shell — building the Work help
    /// surface against that would drop a second "?" on the shell's own title bar.
    /// </summary>
    private Window? WorkCompanionWindow()
    {
        if (!GodotObject.IsInstanceValid(_workView))
            return null;
        Window window = _workView!.GetWindow();
        return GodotObject.IsInstanceValid(window) && window != GetWindow() ? window : null;
    }

    private bool IsWorkCompanionDragging() =>
        GodotObject.IsInstanceValid(_workView) && _workView!.IsDragging;

    /// <summary>
    /// True once the player has flipped the Work CRT between session and lifetime totals. The
    /// baseline is captured on the first frame the counter is observable rather than at Work
    /// entry, because the view is built asynchronously with the companion window.
    /// </summary>
    private bool HasSwitchedWorkCounter()
    {
        if (!IsWorkActive() || !GodotObject.IsInstanceValid(_workView))
            return false;
        bool showLifetime = _workView!.ShowLifetime;
        if (_workCounterOrigin is not bool origin)
        {
            _workCounterOrigin = showLifetime;
            return false;
        }
        return showLifetime != origin;
    }
}
