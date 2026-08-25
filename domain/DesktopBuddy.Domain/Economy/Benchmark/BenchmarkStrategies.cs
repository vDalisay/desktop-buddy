using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>
/// Benchmark shoppers built from the current authored shop catalogue. Only the completionist
/// trace is used for income/pacing telemetry; the alternative shoppers exercise the no-
/// prerequisite/free-choice rule without maintaining a second hard-coded progression list.
/// </summary>
public static class BenchmarkStrategies
{
    public const string CompletionistId = "completionist_in_order";

    private static readonly IReadOnlyList<string> PreferredSaveTargets = new[]
    {
        ContentIds.ToolPistol,
        ContentIds.ToolGrenade,
        ContentIds.ToolFireSprayer,
        ContentIds.ToolShotgun,
    };

    private static readonly IReadOnlyList<string> PreferredSkippedRegulars = new[]
    {
        ContentIds.ToolBaseball,
        ContentIds.ToolMeal,
        ContentIds.ToolSoccerBall,
    };

    /// <summary>Builds strategies against exactly the entries the current shop exposes.</summary>
    public static IReadOnlyList<BenchmarkStrategy> ForCatalogue(ToolCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        IReadOnlyList<string> order = BenchmarkSchedule.PurchaseOrder(catalogue);
        var all = new List<BenchmarkStrategy>
        {
            new(CompletionistId, order),
        };

        foreach (string target in PreferredSaveTargets)
        {
            if (Contains(order, target))
                all.Add(new BenchmarkStrategy($"save_for_{Suffix(target)}", First(order, target)));
        }

        all.Add(new BenchmarkStrategy("skip_regulars", Without(order, PreferredSkippedRegulars)));
        if (Contains(order, ContentIds.ToolPowerGrab))
        {
            all.Add(new BenchmarkStrategy(
                "power_grab_preference",
                First(order, ContentIds.ToolPowerGrab, skip: ContentIds.ToolMeal)));
        }

        return all;
    }

    private static IReadOnlyList<string> First(
        IReadOnlyList<string> order,
        string contentId,
        string? skip = null)
    {
        var result = new List<string>(order.Count) { contentId };
        foreach (string id in order)
        {
            if (id != contentId && id != skip)
                result.Add(id);
        }

        return result;
    }

    private static IReadOnlyList<string> Without(
        IReadOnlyList<string> order,
        IReadOnlyList<string> excluded)
    {
        var result = new List<string>(order.Count);
        foreach (string id in order)
        {
            if (!Contains(excluded, id))
                result.Add(id);
        }

        return result;
    }

    private static bool Contains(IReadOnlyList<string> values, string value)
    {
        foreach (string candidate in values)
        {
            if (string.Equals(candidate, value, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Suffix(string contentId) =>
        contentId.StartsWith("tool.", StringComparison.Ordinal)
            ? contentId["tool.".Length..]
            : contentId;
}
