using System;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Sole runtime owner of monotonic mood drift, passive income, cumulative time, and the
/// aggregate gameplay pause coordinator. It remains alive while the gameplay tree is paused.
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
    private Func<bool>? _isWorkMode;
    private Action? _resumePresentation;
    private Action<bool>? _setWindowVisibility;
    private double _pendingSeconds;
    private int _foregroundMaxFps;
    private bool _presentationThrottled;
    private bool _trayHidden;
    private bool _fullscreenHidden;
    private double _fullscreenPollSeconds;
    private bool _shuttingDown;
    private bool _editorModeActive;

    private const double FullscreenPollSeconds = 0.5;

    private Func<bool>? _foregroundAppIsFullscreen;

    public bool IsInitialized { get; private set; }
    public bool IsHiddenToTray { get; private set; }

    /// <summary>Frame cap while hidden or throttled; zero uses the tuning profile's value.</summary>
    public int BackgroundMaxFps { get; set; }

    /// <summary>Whether the buddy hides itself while a full-screen application is focused.</summary>
    public bool HideForFullscreenApps { get; set; }
    /// <summary>
    /// A locked Windows session keeps running as hidden time with no discontinuity
    /// exclusion, and the prior presentation state is restored on unlock (FR-016.8).
    /// </summary>
    public bool IsSessionLocked { get; private set; }
    public bool IsPresentationThrottled => _presentationThrottled;
    public bool IsEditorModeActive => _editorModeActive;
    public double AcceptedRunningSeconds { get; private set; }
    public int ExcludedSpanCount => _clock?.ExcludedSpanCount ?? 0;
    /// <summary>True while clock spans count as hidden rather than foreground time.</summary>
    public bool AccruesAsHidden => IsHiddenToTray || IsSessionLocked;
    public GameplayPauseCoordinator PauseCoordinator { get; private set; } = null!;

    public void Configure(
        BuddyProgressState progress,
        EconomyService economy,
        SaveCoordinator saves,
        MoodEconomyProfile profile,
        Func<bool> activeInteraction,
        IMonotonicTimeSource? timeSource = null,
        Action? resumePresentation = null,
        Action<bool>? setWindowVisibility = null,
        Func<bool>? isWorkMode = null,
        Func<bool>? foregroundAppIsFullscreen = null)
    {
        _foregroundAppIsFullscreen = foregroundAppIsFullscreen;
        _isWorkMode = isWorkMode;
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _activeInteraction = activeInteraction ??
            throw new ArgumentNullException(nameof(activeInteraction));
        if (!profile.IsRuntimeValid)
            throw new ArgumentException("Mood economy profile is invalid.", nameof(profile));
        _resumePresentation = resumePresentation;
        _setWindowVisibility = setWindowVisibility;
        _income = new PassiveIncome(profile.NeutralCreditsPerMinute / 60.0);
        _clock = new GameClock(timeSource ?? new StopwatchTimeSource(), profile.DiscontinuitySeconds);
        ProcessMode = ProcessModeEnum.Always;
        IsInitialized = true;
    }

    public override void _EnterTree()
    {
        if (PauseCoordinator is null)
            PauseCoordinator = new GameplayPauseCoordinator(GetTree());
    }

    public override void _Process(double delta)
    {
        PollForegroundApplication(delta);
        if (!IsInitialized || _shuttingDown || _editorModeActive ||
            !_clock.TrySample(out double elapsed))
        {
            return;
        }

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

    public void SetEditorMode(bool active)
    {
        if (!IsInitialized || _shuttingDown || active == _editorModeActive)
            return;

        if (active)
            SettleCurrentBucket();
        _editorModeActive = active;
        _clock.Reset();
        _pendingSeconds = 0.0;
        PauseCoordinator.Set(GameplayPauseReason.CharacterEditor, active);
    }

    public void SetHiddenToTray(bool hidden)
    {
        if (hidden == _trayHidden)
            return;
        _trayHidden = hidden;
        UpdateHidden();
    }

    /// <summary>
    /// The buddy steps aside while a full-screen application owns the screen. It is a second
    /// reason to be hidden, not a second owner of visibility: a window hidden to the tray by
    /// hand stays hidden when the game exits.
    /// </summary>
    public void SetHiddenForFullscreenApp(bool hidden)
    {
        if (hidden == _fullscreenHidden)
            return;
        _fullscreenHidden = hidden;
        UpdateHidden();
    }

    private void UpdateHidden()
    {
        bool hidden = _trayHidden || _fullscreenHidden;
        if (!IsInitialized || _shuttingDown || hidden == IsHiddenToTray)
            return;
        SettleCurrentBucket();
        IsHiddenToTray = hidden;
        PauseCoordinator.Set(GameplayPauseReason.HiddenToTray, hidden);
        if (hidden)
        {
            _setWindowVisibility?.Invoke(false);
            ThrottlePresentation();
        }
        else
        {
            RestorePresentation();
            _setWindowVisibility?.Invoke(true);
        }
    }

    public void NotifySuspended()
    {
        if (!IsInitialized || _shuttingDown)
            return;
        SettleCurrentBucket();
        _clock.Reset();
        PauseCoordinator.Set(GameplayPauseReason.Suspended, true);
    }

    public void NotifyResumed(bool remainHidden)
    {
        if (!IsInitialized || _shuttingDown)
            return;
        _clock.Reset();
        _pendingSeconds = 0.0;
        IsHiddenToTray = remainHidden;
        _trayHidden = remainHidden;
        _fullscreenHidden = false;
        PauseCoordinator.Set(GameplayPauseReason.Suspended, false);
        PauseCoordinator.Set(GameplayPauseReason.HiddenToTray, remainHidden);
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
        if (!IsInitialized || _shuttingDown || locked == IsSessionLocked)
            return;
        SettleCurrentBucket();
        IsSessionLocked = locked;
    }

    /// <summary>
    /// Settles the final accepted span and stops lifecycle mutation before a clean-exit
    /// snapshot. Suspended time was already re-anchored on resume and is never included.
    /// Idempotent so the explicit close path and the tree-exit fallback can both call it.
    /// </summary>
    public void BeginShutdown()
    {
        if (!IsInitialized || _shuttingDown)
            return;
        if (!_editorModeActive)
            SettleCurrentBucket();
        _shuttingDown = true;
        SetProcess(false);
    }

    private void SettleCurrentBucket()
    {
        if (_editorModeActive)
            return;
        if (_clock.TrySample(out double elapsed))
            _pendingSeconds += elapsed;
        if (_pendingSeconds > 0.0)
        {
            double accepted = _pendingSeconds;
            _pendingSeconds = 0.0;
            ApplyAcceptedSpan(accepted);
        }
    }

    /// <summary>
    /// Polled rather than event-driven: Windows has no notification for "some window became
    /// full-screen", and twice a second is far cheaper than any hook would be.
    /// </summary>
    private void PollForegroundApplication(double delta)
    {
        if (!IsInitialized || _shuttingDown || _foregroundAppIsFullscreen is null)
            return;
        if (!HideForFullscreenApps)
        {
            SetHiddenForFullscreenApp(false);
            return;
        }

        _fullscreenPollSeconds += delta;
        if (_fullscreenPollSeconds < FullscreenPollSeconds)
            return;
        _fullscreenPollSeconds = 0.0;
        SetHiddenForFullscreenApp(_foregroundAppIsFullscreen());
    }

    private void ThrottlePresentation()
    {
        if (_presentationThrottled || DisplayServer.GetName() == "headless")
            return;
        _foregroundMaxFps = Engine.MaxFps;
        Engine.MaxFps = BackgroundMaxFps > 0 ? BackgroundMaxFps : _profile.HiddenMaxFps;
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
        // Appetite burns on the same accepted span, at the rate for what is actually going on
        // (owner decision 2026-07-29): barely anything while the player works or the buddy is
        // hidden, more while it is being played with.
        _progress.DrainHunger(elapsed, ClassifyHunger(hidden, active));
        _progress.AccrueTime(
            elapsed,
            active ? elapsed : 0.0,
            hidden ? elapsed : 0.0);
        AcceptedRunningSeconds += elapsed;
        _ = ObserveAutosaveAsync(_saves.TickAsync(elapsed));
    }

    private HungerActivity ClassifyHunger(bool hidden, bool activeInteraction) =>
        HungerActivityPolicy.Classify(hidden, _isWorkMode?.Invoke() ?? false, activeInteraction);

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
