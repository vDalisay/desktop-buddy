using System.Collections.Generic;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>
/// The seven benchmark shoppers (M5 Tasks 11–13 §4.3). Only
/// <see cref="CompletionistId"/> is judged against the §1.1 target times; the rest exist to
/// prove the shop has no prerequisite graph — any visible item may be saved for, and
/// earlier cheaper ones may be skipped forever.
/// </summary>
public static class BenchmarkStrategies
{
    public const string CompletionistId = "completionist_in_order";

    /// <summary>The high-value items each <c>save_for_*</c> strategy heads straight for.</summary>
    public static readonly IReadOnlyList<string> SaveTargets = new[]
    {
        ContentIds.ToolPistol,
        ContentIds.ToolGrenade,
        ContentIds.ToolFireSprayer,
        ContentIds.ToolShotgun,
    };

    /// <summary>
    /// The regulars <c>skip_regulars</c> never buys, and the single earlier regular
    /// <c>power_grab_preference</c> walks past — a completed run that still does not own it
    /// is the evidence that ownership is not a chain.
    /// </summary>
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

    /// <summary>The §1.1 order with one entry pulled to the front, and optionally one dropped.</summary>
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
