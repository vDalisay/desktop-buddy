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
    private double _pendingSeconds;

    public bool IsInitialized { get; private set; }
    public bool IsHiddenToTray { get; private set; }
    public double AcceptedRunningSeconds { get; private set; }
    public int ExcludedSpanCount => _clock?.ExcludedSpanCount ?? 0;

    public void Configure(
        BuddyProgressState progress,
        EconomyService economy,
        SaveCoordinator saves,
        MoodEconomyProfile profile,
        Func<bool> activeInteraction,
        IMonotonicTimeSource? timeSource = null)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _activeInteraction = activeInteraction ??
            throw new ArgumentNullException(nameof(activeInteraction));
        if (!profile.IsRuntimeValid)
            throw new ArgumentException("Mood economy profile is invalid.", nameof(profile));
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
        double cadence = IsHiddenToTray
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
    }

    public void NotifySuspended()
    {
        _clock.Reset();
        _pendingSeconds = 0.0;
        GetTree().Paused = true;
    }

    public void NotifyResumed(bool remainHidden)
    {
        _clock.Reset();
        _pendingSeconds = 0.0;
        IsHiddenToTray = remainHidden;
        GetTree().Paused = remainHidden;
    }

    private void ApplyAcceptedSpan(double elapsed)
    {
        _progress.DriftMood(elapsed);
        long milliCredits = _income.Accrue(_progress.Mood, elapsed);
        _economy.DepositPassive(milliCredits);
        bool active = !IsHiddenToTray && _activeInteraction();
        _progress.AccrueTime(
            elapsed,
            active ? elapsed : 0.0,
            IsHiddenToTray ? elapsed : 0.0);
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
