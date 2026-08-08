using System;
using System.Threading.Tasks;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Work;

/// <summary>
/// Low-frequency Work-mode durability and native lifecycle handling. Kept separate from the
/// session coordinator so global input callbacks remain allocation-light and disk-free.
/// </summary>
public partial class WorkCompanionCoordinator
{
    private const double WorkCheckpointSeconds = 45.0;
    private const double ActivityHealthCheckSeconds = 1.0;

    private double _workCheckpointElapsed;
    private double _activityHealthElapsed;
    private bool _capturePausedForSuspend;
    private bool _resilienceSubscribed;

    public override void _EnterTree()
    {
        SubscribeResilienceEvents();

        // The pre-revamp shell defaulted to its click-through state named "Work". The revised
        // feature is a deliberate companion mode, so loading an old/default setting must not
        // silently launch the typing companion. Normalize once to normal Play; the top-level
        // Work command then performs the explicit entry transition.
        if (GodotObject.IsInstanceValid(_sandbox) && _sandbox.Shell.Mode == InputMode.Work)
            Callable.From(NormalizeLegacyStartupMode).CallDeferred();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsActive)
        {
            _workCheckpointElapsed = 0.0;
            _activityHealthElapsed = 0.0;
            return;
        }
        if (_transitioning)
            return;

        _workCheckpointElapsed += Math.Max(0.0, delta);
        _activityHealthElapsed += Math.Max(0.0, delta);

        if (_activityHealthElapsed >= ActivityHealthCheckSeconds)
        {
            _activityHealthElapsed = 0.0;
            if (!_capturePausedForSuspend && _activitySource is { IsRunning: false })
            {
                Log.Error(Category,
                    "Global Work activity capture stopped unexpectedly; returning to normal mode.");
                _ = ExitObservedAsync();
                return;
            }
        }

        if (_workCheckpointElapsed < WorkCheckpointSeconds)
            return;

        _workCheckpointElapsed = 0.0;
        // Monitor/taskbar topology is queried by the recovery policy. Re-applying the current
        // position therefore clamps an active companion back into a recoverable work area if
        // a monitor disappears or its usable bounds change while Work Mode is running.
        if (_sandbox.Window.WorkCompanionActive)
            _sandbox.Window.MoveWorkCompanion(_sandbox.Window.WorkCompanionRect.Position);
        _ = CheckpointObservedAsync();
    }

    private void NormalizeLegacyStartupMode()
    {
        if (!IsActive && !_transitioning && _sandbox.Shell.Mode == InputMode.Work)
            _sandbox.Shell.ToggleInteractionMode();
    }

    private void SubscribeResilienceEvents()
    {
        if (_resilienceSubscribed ||
            !GodotObject.IsInstanceValid(_sandbox) ||
            !GodotObject.IsInstanceValid(_sandbox.Window))
        {
            return;
        }

        _sandbox.Window.Adapter.SystemSuspending += OnWorkSystemSuspending;
        _sandbox.Window.Adapter.SystemResumed += OnWorkSystemResumed;
        TreeExiting += UnsubscribeResilienceEvents;
        _resilienceSubscribed = true;
    }

    private void UnsubscribeResilienceEvents()
    {
        if (!_resilienceSubscribed ||
            !GodotObject.IsInstanceValid(_sandbox) ||
            !GodotObject.IsInstanceValid(_sandbox.Window))
        {
            return;
        }

        _sandbox.Window.Adapter.SystemSuspending -= OnWorkSystemSuspending;
        _sandbox.Window.Adapter.SystemResumed -= OnWorkSystemResumed;
        _resilienceSubscribed = false;
    }

    private void OnWorkSystemSuspending()
    {
        if (!IsActive || _transitioning || _capturePausedForSuspend)
            return;

        // Hooks are not needed while Windows is suspending. Stop them before the process loses
        // execution time, consume the final anonymous deltas, and checkpoint on the main thread.
        _capturePausedForSuspend = true;
        _activitySource?.Stop();
        DrainActivity();
        _ = CheckpointObservedAsync();
    }

    private void OnWorkSystemResumed()
    {
        if (!IsActive || _transitioning || !_capturePausedForSuspend)
            return;

        _capturePausedForSuspend = false;
        if (_activitySource is null)
        {
            Log.Error(Category, "Work activity source was missing after resume; leaving Work Mode.");
            _ = ExitObservedAsync();
            return;
        }

        WorkActivitySourceResult result = _activitySource.Start();
        if (!result.Success)
        {
            Log.Error(Category, result.Detail ?? "Work activity capture could not restart after resume.");
            _ = ExitObservedAsync();
        }
    }

    private async Task CheckpointObservedAsync()
    {
        try
        {
            if (_geometryDirty)
                await PersistPreferencesAsync(forceGeometry: true);
            if (_context.Saves.IsDirty)
                await _context.Saves.FlushProgressAsync(force: true);
        }
        catch (Exception exception)
        {
            Log.Error(Category, $"Periodic Work checkpoint failed: {exception.Message}");
        }
    }
}
