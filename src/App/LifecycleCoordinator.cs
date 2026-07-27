using System;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Sole runtime owner of monotonic mood drift, passive income, and cumulative
/// run/active/hidden time. It remains alive while the gameplay tree is paused.
/// </summary>
public partial class LifecycleCoordinator : Node
{
    private BuddyProgressState _progress = null!;
    private EconomyService _economy = null!;
    private SaveCoordinator _saves = null!;
    private MoodEconomyProfile _profile = null!;
    private PassiveIncome _income = null!;
    private GameClock _clock = null!;
    private Func<bool> _activeInteraction = null!;
    private Action? _resumePresentation;
    private double _pendingSeconds;
    private int _foregroundMaxFps;
    private bool _presentationThrottled;

    public bool IsInitialized { get; private set; }
    public bool IsHiddenToTray { get; private set; }

    /// <summary>
    /// A locked Windows session keeps running as hidden time with no discontinuity
    /// exclusion, and the prior presentation state is restored on unlock (FR-016.8).
    /// </summary>
    public bool IsSessionLocked { get; private set; }
    public bool IsPresentationThrottled => _presentationThrottled;
    public double AcceptedRunningSeconds { get; private set; }
    public int ExcludedSpanCount => _clock?.ExcludedSpanCount ?? 0;

    /// <summary>True while clock spans count as hidden rather than foreground time.</summary>
    public bool AccruesAsHidden => IsHiddenToTray || IsSessionLocked;

    public void Configure(
        BuddyProgressState progress,
        EconomyService economy,
        SaveCoordinator saves,
        MoodEconomyProfile profile,
        Func<bool> activeInteraction,
        IMonotonicTimeSource? timeSource = null,
        Action? resumePresentation = null)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _activeInteraction = activeInteraction ??
            throw new ArgumentNullException(nameof(activeInteraction));
        if (!profile.IsRuntimeValid)
            throw new ArgumentException("Mood economy profile is invalid.", nameof(profile));
        _resumePresentation = resumePresentation;
        _income = new PassiveIncome(profile.NeutralCreditsPerMinute / 60.0);
        _clock = new GameClock(timeSource ?? new StopwatchTimeSource(), profile.DiscontinuitySeconds);
        ProcessMode = ProcessModeEnum.Always;
        IsInitialized = true;
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !_clock.TrySample(out double elapsed))
            return;
        _pendingSeconds += elapsed;
        double cadence = AccruesAsHidden
            ? _profile.HiddenUpdateSeconds
            : _profile.ForegroundUpdateSeconds;
        if (_pendingSeconds < cadence)
            return;
        double accepted = _pendingSeconds;
        _pendingSeconds = 0.0;
        ApplyAcceptedSpan(accepted);
    }

    public void SetHiddenToTray(bool hidden)
    {
        if (!IsInitialized || hidden == IsHiddenToTray)
            return;
        _clock.Reset();
        _pendingSeconds = 0.0;
        IsHiddenToTray = hidden;
        if (DisplayServer.GetName() != "headless")
            GetWindow().Visible = !hidden;
        GetTree().Paused = hidden;
        if (hidden)
            ThrottlePresentation();
        else
            RestorePresentation();
    }

    public void NotifySuspended()
    {
        if (!IsInitialized)
            return;
        _clock.Reset();
        _pendingSeconds = 0.0;
        GetTree().Paused = true;
    }

    public void NotifyResumed(bool remainHidden)
    {
        if (!IsInitialized)
            return;
        _clock.Reset();
        _pendingSeconds = 0.0;
        IsHiddenToTray = remainHidden;
        GetTree().Paused = remainHidden;
        if (remainHidden)
            ThrottlePresentation();
        else
            RestorePresentation();
    }

    /// <summary>
    /// A session lock is not a suspension: the machine keeps running, so mood drift and
    /// passive income continue and the span is never excluded as a discontinuity
    /// (FR-016.8). The clock is deliberately <b>not</b> reset. Gameplay keeps simulating —
    /// only the time-accounting bucket changes — and unlocking restores nothing because
    /// nothing was torn down.
    /// </summary>
    public void NotifySessionLock(bool locked)
    {
        if (!IsInitialized || locked == IsSessionLocked)
            return;
        IsSessionLocked = locked;
    }

    private void ThrottlePresentation()
    {
        if (_presentationThrottled || DisplayServer.GetName() == "headless")
            return;
        _foregroundMaxFps = Engine.MaxFps;
        Engine.MaxFps = _profile.HiddenMaxFps;
        RenderingServer.RenderLoopEnabled = false;
        _presentationThrottled = true;
    }

    private void RestorePresentation()
    {
        if (!_presentationThrottled)
            return;
        Engine.MaxFps = _foregroundMaxFps;
        RenderingServer.RenderLoopEnabled = true;
        _presentationThrottled = false;
        // Re-anchor interpolation before the first visible frame so the restarted render
        // loop cannot tween bodies from their pre-hide transforms (FR-015.10). The physics
        // step accumulator itself stays bounded by the project's
        // physics/common/max_physics_steps_per_frame setting.
        _resumePresentation?.Invoke();
    }

    private void ApplyAcceptedSpan(double elapsed)
    {
        _progress.DriftMood(elapsed);
        // Novelty recovers on the same monotonic accepted span mood drifts on, so a toy the
        // buddy tired of becomes interesting again by the passage of time rather than by
        // anything the player does (owner instruction 2026-07-27).
        _progress.RechargeFun(elapsed);
        long milliCredits = _income.Accrue(_progress.Mood, elapsed);
        _economy.DepositPassive(milliCredits);
        bool hidden = AccruesAsHidden;
        bool active = !hidden && _activeInteraction();
        _progress.AccrueTime(
            elapsed,
            active ? elapsed : 0.0,
            hidden ? elapsed : 0.0);
        AcceptedRunningSeconds += elapsed;
        _ = ObserveAutosaveAsync(_saves.TickAsync(elapsed));
    }

    private static async System.Threading.Tasks.Task ObserveAutosaveAsync(
        System.Threading.Tasks.Task operation)
    {
        try
        {
            await operation;
        }
        catch (Exception exception)
        {
            Diagnostics.Log.Error(
                "Persistence",
                $"Lifecycle autosave failed; progress remains dirty: {exception.Message}");
        }
    }
}
