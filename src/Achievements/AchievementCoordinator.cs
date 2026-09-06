using System;
using System.Collections.Generic;
using System.Globalization;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Achievements;

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
/// Engine-independent achievement rules. Gameplay adapters report semantic events; this class is
/// the only place that turns them into persistent qualification. Steam is deliberately absent.
/// </summary>
public sealed class AchievementCoordinator
{
    private const string BoxingHitsCounter = "boxing_glove_hits";
    private const string CharacterArcLowKey = "character_arc.low_character";
    private const string CustomizationPrefix = "customization.";
    private const string HomeCategoriesKey = "home.categories";
    private const double RubeWindowSeconds = 5.0;

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

    /// <summary>Reconciles rules that are fully reconstructible from durable gameplay state.</summary>
    public void EvaluatePersistentState(Guid? activeCharacterId = null)
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

    /// <summary>Reports one scored positive-pain event from the authoritative impact pipeline.</summary>
    public void RecordDamage(string contentId, float pain, long milliCredits, double nowSeconds)
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

        EvaluatePersistentState();
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

    public void RecordEnvironmentCategory(string categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            return;
        var categories = new HashSet<string>(StringComparer.Ordinal);
        string? saved = _store.Value(HomeCategoriesKey);
        if (!string.IsNullOrWhiteSpace(saved))
            foreach (string item in saved.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                categories.Add(item);
        categories.Add(categoryId);
        _store.SetValue(HomeCategoriesKey, string.Join("|", categories));
    }

    public void EvaluateHomeSweetHome(IEnumerable<string> requiredCategoryIds)
    {
        ArgumentNullException.ThrowIfNull(requiredCategoryIds);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        string? saved = _store.Value(HomeCategoriesKey);
        if (!string.IsNullOrWhiteSpace(saved))
            foreach (string item in saved.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                placed.Add(item);

        bool any = false;
        foreach (string required in requiredCategoryIds)
        {
            any = true;
            if (!placed.Contains(required))
                return;
        }
        if (any)
            _store.Qualify(AchievementIds.HomeSweetHome);
    }

    private void ObserveCharacterArc(Guid? activeCharacterId, float mood)
    {
        string current = CharacterKey(activeCharacterId);
        if (mood <= -100.0f)
        {
            _store.SetValue(CharacterArcLowKey, current);
            return;
        }

        if (mood >= 100.0f && string.Equals(_store.Value(CharacterArcLowKey), current, StringComparison.Ordinal))
            _store.Qualify(AchievementIds.CharacterArc);
    }

    private static string CharacterKey(Guid? id) => id?.ToString("N") ?? "builtin";
}