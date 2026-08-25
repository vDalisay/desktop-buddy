using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>
/// Current benchmark purchase order. The launch catalogue is the source of truth: the benchmark
/// deliberately derives its purchasables from <see cref="CataloguePolicy.LaunchContentIds"/>
/// instead of carrying a second, historical progression schedule.
///
/// <para>The old M5 209-minute target table was a tuning snapshot, not a permanent gameplay
/// contract. Authored catalogue prices/order may evolve; benchmark reports fingerprint and observe
/// those changes without rejecting the current game for disagreeing with an obsolete target.</para>
/// </summary>
public static class BenchmarkSchedule
{
    /// <summary>Every current non-starting launch tool, in the same order as the shipped shop.</summary>
    public static readonly IReadOnlyList<string> PurchasableOrder = BuildPurchasableOrder();

    private static IReadOnlyList<string> BuildPurchasableOrder()
    {
        var starting = new HashSet<string>(CataloguePolicy.NewSaveUnlockedContentIds, StringComparer.Ordinal);
        var purchasable = new List<string>(CataloguePolicy.LaunchContentIds.Count);
        foreach (string contentId in CataloguePolicy.LaunchContentIds)
        {
            if (!starting.Contains(contentId))
                purchasable.Add(contentId);
        }

        return purchasable;
    }
}
