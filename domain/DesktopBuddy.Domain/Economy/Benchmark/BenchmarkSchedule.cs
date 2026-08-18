using System.Collections.Generic;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>The official cumulative unlock time for one benchmarked purchasable, in running minutes.</summary>
public readonly record struct ScheduleTarget(string ContentId, double TargetMinutes);

/// <summary>
/// The existing owner-accepted 209-minute M5 completionist benchmark remains the calibration
/// baseline for the twelve established paid tools while the Demo progression pass introduces
/// Pet, Tickle, and Boxing Glove as new early purchasables. Those three have provisional Demo
/// prices and are deliberately kept outside this legacy benchmark until the final Demo pacing
/// gate approves a replacement whole-catalogue schedule.
///
/// <para>This separation is intentional: changing the fresh-save ownership contract must not
/// silently redefine the previously accepted 209-minute targets. The Demo implementation can
/// become structurally correct first, then the owner can judge the materially subjective final
/// pacing with benchmark evidence instead of receiving hidden price changes.</para>
/// </summary>
public static class BenchmarkSchedule
{
    /// <summary>Each median cumulative purchase time must land inside ±15% of its target.</summary>
    public const double ToleranceFraction = 0.15;

    private static readonly double[] TargetMinutes =
    {
        3.0, 7.0, 13.0, 21.0, 41.0, 52.0, 76.0, 104.0, 120.0, 138.0, 184.0, 209.0,
    };

    /// <summary>
    /// The twelve entries covered by the accepted M5 schedule. Pet, Tickle, and Boxing Glove
    /// are now paid Demo entries but are not folded into this schedule until DEMO-9 pacing.
    /// </summary>
    public static readonly IReadOnlyList<string> PurchasableOrder = new[]
    {
        ContentIds.ToolBaseball,
        ContentIds.ToolBaseballBat,
        ContentIds.ToolMeal,
        ContentIds.ToolNerfBlaster,
        ContentIds.ToolPistol,
        ContentIds.ToolSoccerBall,
        ContentIds.ToolGrenade,
        ContentIds.ToolFireSprayer,
        ContentIds.ToolPowerGrab,
        ContentIds.ToolRepairKit,
        ContentIds.ToolShotgun,
        ContentIds.ToolDrink,
    };

    /// <summary>The benchmarked purchasables paired with their cumulative minute targets.</summary>
    public static readonly IReadOnlyList<ScheduleTarget> Targets = BuildTargets();

    private static IReadOnlyList<ScheduleTarget> BuildTargets()
    {
        if (PurchasableOrder.Count != TargetMinutes.Length)
        {
            throw new System.InvalidOperationException(
                $"The accepted M5 benchmark has {PurchasableOrder.Count} entries but " +
                $"{TargetMinutes.Length} time targets.");
        }

        var targets = new List<ScheduleTarget>(TargetMinutes.Length);
        for (int index = 0; index < PurchasableOrder.Count; index++)
        {
            targets.Add(new ScheduleTarget(PurchasableOrder[index], TargetMinutes[index]));
        }

        return targets;
    }
}
