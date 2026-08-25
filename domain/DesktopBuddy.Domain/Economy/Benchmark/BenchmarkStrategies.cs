using System.Collections.Generic;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>
/// Representative benchmark shoppers. The completionist follows the current authored shop order;
/// the alternatives exercise different saving/skipping patterns for diagnostic traces. Free-choice
/// itself is proved directly against every current purchasable by <see cref="BenchmarkObligations"/>,
/// so an expensive item does not have to be reachable inside the fixed representative session.
/// </summary>
public static class BenchmarkStrategies
{
    public const string CompletionistId = "completionist_in_order";

    /// <summary>High-value items whose save-first behaviour is useful in benchmark reports.</summary>
    public static readonly IReadOnlyList<string> SaveTargets = new[]
    {
        ContentIds.ToolPistol,
        ContentIds.ToolGrenade,
        ContentIds.ToolFireSprayer,
        ContentIds.ToolShotgun,
    };

    /// <summary>Regular entries omitted by the skip strategy.</summary>
    public static readonly IReadOnlyList<string> SkippedRegulars = new[]
    {
        ContentIds.ToolBaseball,
        ContentIds.ToolMeal,
        ContentIds.ToolSoccerBall,
    };

    public static readonly IReadOnlyList<BenchmarkStrategy> All = Build();

    private static IReadOnlyList<BenchmarkStrategy> Build()
    {
        var all = new List<BenchmarkStrategy>
        {
            new(CompletionistId, BenchmarkSchedule.PurchasableOrder),
        };

        foreach (string target in SaveTargets)
        {
            all.Add(new BenchmarkStrategy($"save_for_{Suffix(target)}", First(target)));
        }

        all.Add(new BenchmarkStrategy("skip_regulars", Without(SkippedRegulars)));
        all.Add(new BenchmarkStrategy(
            "power_grab_preference",
            First(ContentIds.ToolPowerGrab, skip: ContentIds.ToolMeal)));
        return all;
    }

    private static IReadOnlyList<string> First(string contentId, string? skip = null)
    {
        var order = new List<string> { contentId };
        foreach (string id in BenchmarkSchedule.PurchasableOrder)
        {
            if (id != contentId && id != skip)
                order.Add(id);
        }

        return order;
    }

    private static IReadOnlyList<string> Without(IReadOnlyList<string> excluded)
    {
        var order = new List<string>(BenchmarkSchedule.PurchasableOrder.Count);
        foreach (string id in BenchmarkSchedule.PurchasableOrder)
        {
            bool drop = false;
            foreach (string skip in excluded)
                drop |= id == skip;
            if (!drop)
                order.Add(id);
        }

        return order;
    }

    private static string Suffix(string contentId) =>
        contentId.StartsWith("tool.", System.StringComparison.Ordinal)
            ? contentId["tool.".Length..]
            : contentId;
}
