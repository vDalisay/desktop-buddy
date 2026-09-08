using System;
using System.Collections.Generic;
using System.Globalization;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Domain.Achievements;

[Flags]
public enum CustomizationArea
{
    None = 0,
    BuddyStudio = 1 << 0,
    PaintBuddy = 1 << 1,
    PaintBackground = 1 << 2,
    EnvironmentDecorator = 1 << 3,
    All = BuddyStudio | PaintBuddy | PaintBackground | EnvironmentDecorator,
}

/// <summary>
/// Pure achievement rule engine for the split-state architecture. Account-global qualification,
/// counters, ownership, statistics and Work milestones live on <see cref="PlayerProgressState"/>;
/// Buddy-local rules receive the specific <see cref="BuddyIdentityState"/> being observed. The
/// coordinator references neither Godot nor Steam.
/// </summary>
public sealed class AchievementCoordinator
{
    private const string BoxingHitsCounter = "boxing_glove_hits";
    private const string CharacterArcLowPrefix = "character_arc.low.";
    private const string CustomizationPrefix = "customization.";
    private const string HomeCategoriesMaskKey = "home.categories_mask";
    private const double RubeWindowSeconds = 5.0;
    private static readonly int RequiredHomeCategoriesMask = BuildRequiredHomeCategoriesMask();

    private readonly PlayerProgressState _player;
    private readonly WorkProgressState? _work;
    private readonly AchievementProgressStore _store;
    private readonly Dictionary<string, double> _recentDamageSources = new(StringComparer.Ordinal);

    public AchievementCoordinator(
        PlayerProgressState player,
        WorkProgressState? work = null,
        AchievementProgressStore? store = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _work = work;
        _store = store ?? new AchievementProgressStore(player);
    }

    public AchievementProgressStore Store => _store;

    /// <summary>
    /// Reconciles rules reconstructible from durable account state without requiring a live Buddy.
    /// This is safe during boot before Scene actors have been materialized.
    /// </summary>
    public void EvaluateAccountState()
    {
        ProgressStatistics statistics = _player.Statistics;

        if (statistics.Knockouts > 0)
            _store.Qualify(AchievementIds.LightsOut);
        if (statistics.HighestMood >= 100.0f)
            _store.Qualify(AchievementIds.BestFriends);
        if (statistics.TrustResets > 0)
            _store.Qualify(AchievementIds.Forgiven);
        if (statistics.SuccessfulCatches >= 25)
            _store.Qualify(AchievementIds.NiceCatch);
        if (_player.Times.RunSeconds >= 2.0 * 60.0 * 60.0)
            _store.Qualify(AchievementIds.DesktopShift);

        bool boughtSomething = false;
        bool fullToybox = true;
        bool variety = true;
        foreach (string id in CataloguePolicy.LaunchContentIds)
        {
            bool owned = _player.IsUnlocked(id);
            fullToybox &= owned;
            if (!string.Equals(id, ContentIds.ToolGrab, StringComparison.Ordinal) && owned)
                boughtSomething = true;
            variety &= statistics.ToolUses is not null &&
                       statistics.ToolUses.TryGetValue(id, out long uses) && uses > 0;
        }

        if (boughtSomething)
            _store.Qualify(AchievementIds.RetailTherapy);
        if (fullToybox)
            _store.Qualify(AchievementIds.FullToybox);
        if (variety)
            _store.Qualify(AchievementIds.VarietyHour);

        bool triedEveryDamagingTool = true;
        foreach (string id in AchievementCatalog.DamagingLaunchContentIds)
        {
            triedEveryDamagingTool &= statistics.ToolPainMilli is not null &&
                statistics.ToolPainMilli.TryGetValue(id, out long pain) && pain > 0;
        }
        if (triedEveryDamagingTool)
            _store.Qualify(AchievementIds.TryEverythingOnce);

        if (_work is not null)
        {
            long actions = _work.Lifetime.TotalActions;
            if (actions >= 100) _store.Qualify(AchievementIds.EmployeeDay);
            if (actions >= 1_000) _store.Qualify(AchievementIds.EmployeeWeek);
            if (actions >= 10_000) _store.Qualify(AchievementIds.EmployeeMonth);
            if (actions >= 100_000) _store.Qualify(AchievementIds.EmployeeYear);
            if (actions >= 1_000_000) _store.Qualify(AchievementIds.EmployeeForLife);
        }
    }

    /// <summary>
    /// Reconciles account rules plus the durable rules that genuinely belong to one Buddy identity.
    /// Evaluating Buddy B can never clear Buddy A's Character Arc working state.
    /// </summary>
    public void EvaluatePersistentState(BuddyIdentityState buddy)
    {
        ArgumentNullException.ThrowIfNull(buddy);
        EvaluateAccountState();
        if (buddy.Mood >= 100.0f)
            _store.Qualify(AchievementIds.BestFriends);
        ObserveCharacterArc(buddy);
    }

    /// <summary>
    /// Records one accepted positive-pain semantic damage event. The account/buddy gameplay owners
    /// remain responsible for applying damage, payout and mood first; this method only observes the
    /// accepted outcome and updates achievement working state.
    /// </summary>
    public void RecordDamage(
        string contentId,
        float pain,
        long milliCredits,
        double nowSeconds,
        BuddyIdentityState? buddy = null)
    {
        if (pain <= 0.0f || string.IsNullOrWhiteSpace(contentId))
            return;

        if (milliCredits > 0)
            _store.Qualify(AchievementIds.FirstImpression);

        if (string.Equals(contentId, ContentIds.ToolBoxingGlove, StringComparison.Ordinal))
        {
            long hits = _store.IncrementCounter(BoxingHitsCounter);
            if (hits >= 100)
                _store.Qualify(AchievementIds.PunchingBag);
        }

        double cutoff = nowSeconds - RubeWindowSeconds;
        string[]? expired = null;
        int expiredCount = 0;
        foreach ((string source, double time) in _recentDamageSources)
        {
            if (time >= cutoff)
                continue;

            expired ??= new string[_recentDamageSources.Count];
            expired[expiredCount++] = source;
        }
        for (int index = 0; index < expiredCount; index++)
            _recentDamageSources.Remove(expired![index]);

        _recentDamageSources[contentId] = nowSeconds;
        if (_recentDamageSources.Count >= 3)
            _store.Qualify(AchievementIds.RubeGoldberg);

        if (buddy is null)
            EvaluateAccountState();
        else
            EvaluatePersistentState(buddy);
    }

    /// <summary>Clears only process-local observation windows after reset/rollback/Scene teardown.</summary>
    public void ResetTransientObservations() => _recentDamageSources.Clear();

    public void RecordFireDrill() => _store.Qualify(AchievementIds.FireDrill);
    public void RecordBankShot() => _store.Qualify(AchievementIds.BankShot);

    public void RecordAirborneSeconds(double seconds)
    {
        if (seconds >= 30.0)
            _store.Qualify(AchievementIds.AirBud);
    }

    public void RecordFullyDressed(bool headwear, bool top, bool shoes, bool glasses)
    {
        if (headwear && top && shoes && glasses)
            _store.Qualify(AchievementIds.FullyDressed);
    }

    public void RecordCustomization(BuddyIdentityId buddyIdentityId, CustomizationArea area)
    {
        if (area == CustomizationArea.None)
            return;
        string buddy = BuddyKey(buddyIdentityId);
        string key = CustomizationPrefix + buddy;
        int prior = 0;
        _ = int.TryParse(_store.Value(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out prior);
        int updated = prior | (int)area;
        _store.SetValue(key, updated.ToString(CultureInfo.InvariantCulture));
        if ((updated & (int)CustomizationArea.All) == (int)CustomizationArea.All)
            _store.Qualify(AchievementIds.MakeItYours);
    }

    public void RecordEnvironmentCategory(DecorationCategory category)
    {
        int prior = ReadHomeCategoriesMask();
        int updated = prior | CategoryBit(category);
        _store.SetValue(HomeCategoriesMaskKey, updated.ToString(CultureInfo.InvariantCulture));
        if ((updated & RequiredHomeCategoriesMask) == RequiredHomeCategoriesMask)
            _store.Qualify(AchievementIds.HomeSweetHome);
    }

    private int ReadHomeCategoriesMask()
    {
        int parsed = 0;
        _ = int.TryParse(
            _store.Value(HomeCategoriesMaskKey),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out parsed);
        return parsed;
    }

    private void ObserveCharacterArc(BuddyIdentityState buddy)
    {
        string key = CharacterArcLowPrefix + BuddyKey(buddy.BuddyIdentityId);
        if (buddy.Mood <= -100.0f)
        {
            _store.SetValue(key, "1");
            return;
        }

        if (buddy.Mood >= 100.0f && string.Equals(_store.Value(key), "1", StringComparison.Ordinal))
            _store.Qualify(AchievementIds.CharacterArc);
    }

    private static int BuildRequiredHomeCategoriesMask()
    {
        int mask = 0;
        foreach (DecorationCategory category in Enum.GetValues<DecorationCategory>())
            mask |= CategoryBit(category);
        return mask;
    }

    private static int CategoryBit(DecorationCategory category)
    {
        int index = (int)category;
        if (index < 0 || index >= 31)
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                category,
                "Decoration category cannot be represented in the persisted bit mask.");
        }
        return 1 << index;
    }

    private static string BuddyKey(BuddyIdentityId id)
    {
        if (!id.IsValid)
            throw new ArgumentException("Achievement working state requires a stable Buddy identity.", nameof(id));
        return id.Value.ToString("N");
    }
}
