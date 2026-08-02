using System.Collections.Generic;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>The official cumulative unlock time for one purchasable, in running minutes.</summary>
public readonly record struct ScheduleTarget(string ContentId, double TargetMinutes);

/// <summary>
/// The owner-locked 209-minute completionist schedule (M5 Tasks 11–13 §1.1). These are
/// product targets, not tuning: prices move to meet them, never the other way round. The
/// order itself is not restated here — it is read from
/// <see cref="CataloguePolicy.LaunchContentIds"/>, so the schedule and the catalogue cannot
/// drift apart.
/// </summary>
public static class BenchmarkSchedule
{
    /// <summary>Each median cumulative purchase time must land inside ±15% of its target.</summary>
    public const double ToleranceFraction = 0.15;

    private static readonly double[] TargetMinutes =
    {
        3.0, 7.0, 13.0, 21.0, 41.0, 52.0, 76.0, 104.0, 120.0, 138.0, 184.0, 209.0,
    };

    /// <summary>The twelve purchasables in §1.1 order (the four starting tools removed).</summary>
    public static readonly IReadOnlyList<string> PurchasableOrder = BuildPurchasableOrder();

    /// <summary>The twelve purchasables paired with their cumulative minute targets.</summary>
    public static readonly IReadOnlyList<ScheduleTarget> Targets = BuildTargets();

    private static IReadOnlyList<string> BuildPurchasableOrder()
    {
        var starting = new HashSet<string>(
            CataloguePolicy.NewSaveUnlockedContentIds,
            System.StringComparer.Ordinal);
        var order = new List<string>(TargetMinutes.Length);
        foreach (string contentId in CataloguePolicy.LaunchContentIds)
        {
            if (!starting.Contains(contentId))
                order.Add(contentId);
        }

        return order;
    }

    private static IReadOnlyList<ScheduleTarget> BuildTargets()
    {
        if (PurchasableOrder.Count != TargetMinutes.Length)
        {
            throw new System.InvalidOperationException(
                $"The launch catalogue offers {PurchasableOrder.Count} purchasables but the " +
                $"§1.1 schedule has {TargetMinutes.Length} targets.");
        }

        var targets = new List<ScheduleTarget>(TargetMinutes.Length);
        for (int index = 0; index < PurchasableOrder.Count; index++)
        {
            targets.Add(new ScheduleTarget(PurchasableOrder[index], TargetMinutes[index]));
        }

        return targets;
    }
}
