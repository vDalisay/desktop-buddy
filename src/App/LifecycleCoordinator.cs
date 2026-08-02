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
    private bool _shuttingDown;
    private bool _editorModeActive;

    public bool IsInitialized { get; private set; }
    public bool IsHiddenToTray { get; private set; }
    public bool IsSessionLocked { get; private set; }
    public bool IsPresentationThrottled => _presentationThrottled;
    public bool IsEditorModeActive => _editorModeActive;
    public double AcceptedRunningSeconds { get; private set; }
    public int ExcludedSpanCount => _clock?.ExcludedSpanCount ?? 0;
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
        Func<bool>? isWorkMode = null)
    {
        _isWorkMode = isWorkMode;
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _activeInteraction = activeInteraction ?? throw new ArgumentNullException(nameof(activeInteraction));
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
        PauseCoordinator.Set(GameplayPauseReason.Suspended, false);
        PauseCoordinator.Set(GameplayPauseReason.HiddenToTray, remainHidden);
        if (remainHidden)
            ThrottlePresentation();
        else
            RestorePresentation();
    }

    public void NotifySessionLock(bool locked)
    {
        if (!IsInitialized || _shuttingDown || locked == IsSessionLocked)
            return;
        SettleCurrentBucket();
        IsSessionLocked = locked;
    }

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
        _resumePresentation?.Invoke();
    }

    private void ApplyAcceptedSpan(double elapsed)
    {
        _progress.DriftMood(elapsed);
        _progress.RechargeFun(elapsed);
        long milliCredits = _income.Accrue(_progress.Mood, elapsed);
        _economy.DepositPassive(milliCredits);
        bool hidden = AccruesAsHidden;
        bool active = !hidden && _activeInteraction();
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
