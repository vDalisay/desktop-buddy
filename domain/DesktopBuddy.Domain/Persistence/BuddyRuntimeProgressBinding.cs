using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>
/// Staged runtime compatibility seam between the current aggregate progress API and the
/// account/Buddy split required by multi-Buddy Scenes. Components migrate to this focused binding
/// one at a time instead of each growing its own legacy-vs-split branches.
///
/// Account-global semantics route to <see cref="PlayerProgressState"/> in split mode; Buddy-local
/// semantics route to the bound <see cref="BuddyIdentityState"/>. The legacy constructor preserves
/// today's one-Buddy behavior exactly until the Initial Demo path is retired.
/// </summary>
public sealed class BuddyRuntimeProgressBinding
{
    private readonly BuddyProgressState? _legacy;
    private readonly BuddyProgressCoordinator? _split;

    public BuddyRuntimeProgressBinding(BuddyProgressState legacy)
    {
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
    }

    public BuddyRuntimeProgressBinding(BuddyProgressCoordinator split)
    {
        _split = split ?? throw new ArgumentNullException(nameof(split));
    }

    public bool IsSplit => _split is not null;
    public BuddyProgressState? LegacyProgress => _legacy;
    public BuddyProgressCoordinator? SplitProgress => _split;
    public PlayerProgressState? PlayerProgress => _split?.Player;
    public BuddyIdentityState? BuddyProgress => _split?.Buddy;

    public long BalanceMilliCredits =>
        _split?.Player.BalanceMilliCredits ?? RequireLegacy().BalanceMilliCredits;
    public long BalanceCredits =>
        _split?.Player.BalanceCredits ?? RequireLegacy().BalanceCredits;
    public ToolId SelectedTool =>
        _split?.Player.SelectedTool ?? RequireLegacy().SelectedTool;
    public string SelectedToolId => ContentIds.ForTool(SelectedTool);
    public float Mood => _split?.Buddy.Mood ?? RequireLegacy().Mood;
    public MoodBand MoodBand => _split?.Buddy.MoodBand ?? RequireLegacy().MoodBand;
    public float Fullness => _split?.Buddy.Fullness ?? RequireLegacy().Fullness;
    public float Appetite => _split?.Buddy.Appetite ?? RequireLegacy().Appetite;
    public BuddyTraits Traits => _split?.Buddy.Traits ?? RequireLegacy().Traits;
    public ProgressStatistics Statistics =>
        _split?.Player.Statistics ?? RequireLegacy().Statistics;
    public CumulativeTimes Times =>
        _split?.Player.Times ?? RequireLegacy().Times;
    public ProgressExtensionData? Extensions =>
        _split?.Player.Extensions ?? RequireLegacy().Extensions;

    public bool IsContentHarmful(string contentId) =>
        _split?.Buddy.IsContentHarmful(contentId) ?? RequireLegacy().IsContentHarmful(contentId);

    public bool IsToolUnlocked(string contentId) =>
        _split?.Player.IsUnlocked(contentId) ?? RequireLegacy().IsToolUnlocked(contentId);

    public bool WouldEat(float hungerFill) =>
        _split?.Buddy.WouldEat(hungerFill) ?? RequireLegacy().WouldEat(hungerFill);

    public float InterestIn(FunActivityId activity) =>
        _split?.Buddy.InterestIn(activity) ?? RequireLegacy().InterestIn(activity);

    public bool IsFun(FunActivityId activity) =>
        _split?.Buddy.IsFun(activity) ?? RequireLegacy().IsFun(activity);

    public bool SelectTool(ToolId tool) =>
        _split?.Player.SelectTool(tool) ?? RequireLegacy().SelectTool(tool);

    public bool ApplyCareMood(float delta) =>
        _split?.ApplyCareMood(delta) ?? RequireLegacy().ApplyCareMood(delta);

    public void FillHunger(float amount)
    {
        if (_split is not null)
            _split.Buddy.FillHunger(amount);
        else
            RequireLegacy().FillHunger(amount);
    }

    public void DrainHunger(double elapsedSeconds, HungerActivity activity)
    {
        if (_split is not null)
            _split.Buddy.DrainHunger(elapsedSeconds, activity);
        else
            RequireLegacy().DrainHunger(elapsedSeconds, activity);
    }

    public FunOutcome EngageFun(FunActivityId activity) =>
        _split?.Buddy.EngageFun(activity) ?? RequireLegacy().EngageFun(activity);

    public void RechargeFun(double elapsedSeconds)
    {
        if (_split is not null)
            _split.Buddy.RechargeFun(elapsedSeconds);
        else
            RequireLegacy().RechargeFun(elapsedSeconds);
    }

    public void DriftMood(double elapsedSeconds)
    {
        if (_split is not null)
        {
            _split.Buddy.DriftMood(elapsedSeconds);
            _split.Player.ObserveBuddyMood(_split.Buddy.Mood);
        }
        else
        {
            RequireLegacy().DriftMood(elapsedSeconds);
        }
    }

    public void RecordKnockout()
    {
        if (_split is not null)
            _split.RecordKnockout();
        else
            RequireLegacy().RecordKnockout();
    }

    public void RecordSuccessfulCatch()
    {
        if (_split is not null)
            _split.RecordSuccessfulCatch();
        else
            RequireLegacy().RecordSuccessfulCatch();
    }

    public void RecordContentUse(string contentId)
    {
        if (_split is not null)
            _split.Player.RecordContentUse(contentId);
        else
            RequireLegacy().RecordContentUse(contentId);
    }

    public void AccrueTime(double runSeconds, double activeSeconds, double hiddenSeconds)
    {
        if (_split is not null)
            _split.Player.AccrueTime(runSeconds, activeSeconds, hiddenSeconds);
        else
            RequireLegacy().AccrueTime(runSeconds, activeSeconds, hiddenSeconds);
    }

    public bool SetExtensionValue(string key, string value) =>
        _split?.Player.SetExtensionValue(key, value) ?? RequireLegacy().SetExtensionValue(key, value);

    public void SeedTraits(BuddyTraits traits)
    {
        if (_split is not null)
            _split.Buddy.SeedTraits(traits);
        else
            RequireLegacy().SeedTraits(traits);
    }

    private BuddyProgressState RequireLegacy() =>
        _legacy ?? throw new InvalidOperationException("This runtime progress binding uses split player/Buddy state.");
}
