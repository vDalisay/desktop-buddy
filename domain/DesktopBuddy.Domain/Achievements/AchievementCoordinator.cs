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
/// Pure achievement rule engine. Runtime adapters report semantic gameplay observations; this type
/// is the single authority that turns those observations and durable progress into qualification.
/// It references neither Godot nor Steam and is exercised by ordinary domain tests.
/// </summary>
public sealed class AchievementCoordinator
{
    private const string BoxingHitsCounter = "boxing_glove_hits";
    private const string CharacterArcLowKey = "character_arc.low_character";
    private const string CustomizationPrefix = "customization.";
    private const string HomeCategoriesMaskKey = "home.categories_mask";
    private const double RubeWindowSeconds = 5.0;
    private static readonly int RequiredHomeCategoriesMask = BuildRequiredHomeCategoriesMask();

    private readonly BuddyProgressState _progress;
    private readonly WorkProgressState? _work;
    private readonly AchievementProgressStore _store;
    private readonly Dictionary<string, double> _recentDamageSources = new(StringComparer.Ordinal);

    public AchievementCoordinator(
        BuddyProgressState progress,
        WorkProgressState? work = null,
        AchievementProgressStore? store = null)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _work = work;
        _store = store ?? new AchievementProgressStore(progress);
    }

    public AchievementProgressStore Store => _store;

    /// <summary>
    /// Reconciles rules reconstructible from durable state. Active-character context is mandatory
    /// at the call site (null explicitly means the built-in Buddy) so character-scoped rules can
    /// never silently run under the wrong identity.
    /// </summary>
    public void EvaluatePersistentState(Guid? activeCharacterId)
    {
        ProgressStatistics statistics = _progress.Statistics;

        if (statistics.Knockouts > 0)
            _store.Qualify(AchievementIds.LightsOut);
        if (statistics.HighestMood >= 100.0f || _progress.Mood >= 100.0f)
            _store.Qualify(AchievementIds.BestFriends);
        if (statistics.TrustResets > 0)
            _store.Qualify(AchievementIds.Forgiven);
        if (statistics.SuccessfulCatches >= 25)
            _store.Qualify(AchievementIds.NiceCatch);
        if (_progress.Times.RunSeconds >= 2.0 * 60.0 * 60.0)
            _store.Qualify(AchievementIds.DesktopShift);

        bool boughtSomething = false;
        bool fullToybox = true;
        bool variety = true;
        foreach (string id in CataloguePolicy.LaunchContentIds)
        {
            bool owned = _progress.IsToolUnlocked(id);
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

        ObserveCharacterArc(activeCharacterId, _progress.Mood);
    }

    /// <summary>Reports one accepted positive-pain event from the authoritative impact pipeline.</summary>
    public void RecordDamage(
        string contentId,
        float pain,
        long milliCredits,
        double nowSeconds,
        Guid? activeCharacterId)
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

        EvaluatePersistentState(activeCharacterId);
    }

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

    public void RecordCustomization(Guid? activeCharacterId, CustomizationArea area)
    {
        if (area == CustomizationArea.None)
            return;

        string character = CharacterKey(activeCharacterId);
        string key = CustomizationPrefix + character;
        int prior = 0;
        _ = int.TryParse(_store.Value(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out prior);
        int updated = prior | (int)area;
        _store.SetValue(key, updated.ToString(CultureInfo.InvariantCulture));
        if ((updated & (int)CustomizationArea.All) == (int)CustomizationArea.All)
            _store.Qualify(AchievementIds.MakeItYours);
    }

    /// <summary>
    /// Records an actually present decoration category. The rule engine owns the complete category
    /// set, so callers cannot accidentally qualify Home Sweet Home with an incomplete checklist.
    /// </summary>
    public void RecordEnvironmentCategory(DecorationCategory category)
    {
        int prior = 0;
        _ = int.TryParse(
            _store.Value(HomeCategoriesMaskKey),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out prior);

        int updated = prior | CategoryBit(category);
        _store.SetValue(HomeCategoriesMaskKey, updated.ToString(CultureInfo.InvariantCulture));
        if ((updated & RequiredHomeCategoriesMask) == RequiredHomeCategoriesMask)
            _store.Qualify(AchievementIds.HomeSweetHome);
    }

    private void ObserveCharacterArc(Guid? activeCharacterId, float mood)
    {
        string current = CharacterKey(activeCharacterId);
        string? lowCharacter = _store.Value(CharacterArcLowKey);

        // Mood is shared gameplay state, so an arc is valid only while one character identity is
        // continuously active between the low and high endpoints.
        if (!string.IsNullOrEmpty(lowCharacter) &&
            !string.Equals(lowCharacter, current, StringComparison.Ordinal))
        {
            _store.SetValue(CharacterArcLowKey, string.Empty);
            lowCharacter = null;
        }

        if (mood <= -100.0f)
        {
            _store.SetValue(CharacterArcLowKey, current);
            return;
        }

        if (mood >= 100.0f && string.Equals(lowCharacter, current, StringComparison.Ordinal))
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
            throw new ArgumentOutOfRangeException(nameof(category), category, "Decoration category cannot be represented in the persisted bit mask.");
        return 1 << index;
    }

    private static string CharacterKey(Guid? id) => id?.ToString("N") ?? "builtin";
}
