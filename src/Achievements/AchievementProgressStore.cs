using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Cloud-eligible, account-like achievement qualification stored inside progress.json. The Steam
/// Demo writes exactly the same state as the full game; only the platform publisher is disabled in
/// the Demo. This makes Demo -> full-game reconciliation a normal save handoff.
/// </summary>
public sealed class AchievementProgressStore
{
    public const string Prefix = "achievements.v1.";
    private const string QualifiedPrefix = Prefix + "qualified.";
    private const string CounterPrefix = Prefix + "counter.";
    private const string ValuePrefix = Prefix + "value.";

    private readonly BuddyProgressState _progress;

    public AchievementProgressStore(BuddyProgressState progress)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    public event Action<AchievementDefinition>? Qualified;

    public bool IsQualified(string achievementId) =>
        TryGet(QualifiedPrefix + achievementId, out string? value) && value == "1";

    public IReadOnlyList<string> QualifiedIds => AchievementCatalog.Baseline
        .Where(definition => IsQualified(definition.Id))
        .Select(definition => definition.Id)
        .ToArray();

    public bool Qualify(string achievementId)
    {
        AchievementDefinition definition = AchievementCatalog.Get(achievementId);
        if (IsQualified(achievementId))
            return false;
        _progress.SetExtensionValue(QualifiedPrefix + achievementId, "1");
        Qualified?.Invoke(definition);
        return true;
    }

    public long Counter(string name)
    {
        if (!TryGet(CounterPrefix + name, out string? value) ||
            !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ||
            parsed < 0)
        {
            return 0;
        }
        return parsed;
    }

    public long IncrementCounter(string name, long increment = 1)
    {
        if (increment <= 0)
            return Counter(name);
        long current = Counter(name);
        long next = current > long.MaxValue - increment ? long.MaxValue : current + increment;
        SetCounter(name, next);
        return next;
    }

    public void SetCounter(string name, long value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Counter name is required.", nameof(name));
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        _progress.SetExtensionValue(
            CounterPrefix + name,
            value.ToString(CultureInfo.InvariantCulture));
    }

    public string? Value(string name) =>
        TryGet(ValuePrefix + name, out string? value) ? value : null;

    public void SetValue(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Value name is required.", nameof(name));
        _progress.SetExtensionValue(ValuePrefix + name, value ?? string.Empty);
    }

    public static IReadOnlyDictionary<string, string> PreserveAchievementValues(
        ProgressExtensionData? extensions)
    {
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        if (extensions?.Values is null)
            return kept;
        foreach ((string key, string value) in extensions.Values)
            if (key.StartsWith(Prefix, StringComparison.Ordinal))
                kept[key] = value;
        return kept;
    }

    private bool TryGet(string key, out string? value)
    {
        if (_progress.Extensions?.Values is { } values && values.TryGetValue(key, out string? found))
        {
            value = found;
            return true;
        }
        value = null;
        return false;
    }
}
