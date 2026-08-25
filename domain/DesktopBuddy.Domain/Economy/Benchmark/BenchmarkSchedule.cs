using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>
/// One current shop row. <see cref="TargetMinutes"/> is intentionally <see cref="double.NaN"/>:
/// the former 209-minute M5 targets were retired when the Demo catalogue was repriced and
/// expanded, and CI must not invent replacement pacing targets that the game does not author.
/// </summary>
public readonly record struct ScheduleTarget(string ContentId, double TargetMinutes);

/// <summary>
/// Resolves benchmark purchase order from the same catalogue projection the running shop uses.
/// The old M5 benchmark hard-coded an eleven-item/209-minute schedule; that stopped representing
/// the game after the Demo economy pass. Current benchmark code reads authored catalogue data
/// and treats purchase times as telemetry rather than owner-approved targets.
/// </summary>
public static class BenchmarkSchedule
{
    /// <summary>
    /// Compatibility snapshot for report rendering. The IDs come from the authoritative launch
    /// catalogue contract and carry no fabricated timing target.
    /// </summary>
    public static readonly IReadOnlyList<string> PurchasableOrder = BuildLaunchOrder();

    /// <summary>Current purchasables with no hard-coded timing target.</summary>
    public static readonly IReadOnlyList<ScheduleTarget> Targets = BuildTargets();

    /// <summary>Current visible purchasable tools, in their authored runtime shop order.</summary>
    public static IReadOnlyList<string> PurchaseOrder(ToolCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        IReadOnlyList<CatalogueEntry> entries = CataloguePolicy.ShopEntries(catalogue);
        var order = new List<string>(entries.Count);
        foreach (CatalogueEntry entry in entries)
            order.Add(entry.ContentId);
        return order;
    }

    private static IReadOnlyList<string> BuildLaunchOrder()
    {
        var order = new List<string>();
        foreach (string contentId in CataloguePolicy.LaunchContentIds)
        {
            bool starting = false;
            foreach (string startingId in CataloguePolicy.NewSaveUnlockedContentIds)
                starting |= string.Equals(contentId, startingId, StringComparison.Ordinal);
            if (!starting)
                order.Add(contentId);
        }

        return order;
    }

    private static IReadOnlyList<ScheduleTarget> BuildTargets()
    {
        var targets = new List<ScheduleTarget>(PurchasableOrder.Count);
        foreach (string contentId in PurchasableOrder)
            targets.Add(new ScheduleTarget(contentId, double.NaN));
        return targets;
    }
}
