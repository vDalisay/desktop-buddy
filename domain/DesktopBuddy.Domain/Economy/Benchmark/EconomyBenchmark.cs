using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Interaction;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>
/// The authored economy tuning one run replays against — the same four values the shipped
/// <c>PainConversionProfile</c> and <c>MoodEconomyProfile</c> Resources carry. It is one
/// type so the runner and the report fingerprint cannot sample different numbers.
/// </summary>
public readonly record struct BenchmarkEconomy(
    PainCurve PainCurve,
    double CashPerPain,
    float MinimumImpulse,
    double PassiveCreditsPerSecond);

/// <summary>
/// Replays a behaviour trace through the <b>production</b> economy path — the same
/// <see cref="ImpactRouter"/>, <see cref="PainCurve"/>, <see cref="RewardLedger"/>,
/// <see cref="PassiveIncome"/>, and <see cref="CataloguePolicy"/> the running game uses,
/// all reached through the one <see cref="BuddyProgressState"/> aggregate that owns them.
/// There is no payout arithmetic in this file: an expression that computed credits here
/// would be a second economy.
/// </summary>
public static class EconomyBenchmark
{
    /// <summary>
    /// Passive income and mood drift are integrated in fixed slices, matching the shipped
    /// <c>MoodEconomyProfile.ForegroundUpdateSeconds</c> cadence.
    /// </summary>
    public const double AccrualSliceSeconds = 1.0;

    public static BenchmarkResult Run(
        IReadOnlyList<BenchmarkEvent> trace,
        BenchmarkStrategy strategy,
        ToolCatalogue catalogue,
        BenchmarkEconomy economy,
        int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(economy.PainCurve);

        var progress = new BuddyProgressState(economy.CashPerPain);
        var router = new ImpactRouter(minimumImpulse: economy.MinimumImpulse);
        var passive = new PassiveIncome(economy.PassiveCreditsPerSecond);
        var purchases = new List<BenchmarkPurchase>();

        double now = 0.0;
        double activeSeconds = 0.0;
        double backgroundSeconds = 0.0;
        bool background = false;
        long activeIncome = 0;
        long passiveIncome = 0;
        long largestEvent = 0;
        int duplicateRejections = 0;

        foreach (BenchmarkEvent traced in trace)
        {
            // Time advances in slices so mood drift and the mood-scaled passive rate stay
            // coupled the way the lifecycle coordinator couples them.
            while (now < traced.AtSeconds)
            {
                double slice = Math.Min(AccrualSliceSeconds, traced.AtSeconds - now);
                progress.DriftMood(slice);
                long earned = passive.Accrue(progress.Mood, slice);
                progress.Deposit(earned);
                passiveIncome += earned;
                if (background)
                    backgroundSeconds += slice;
                else
                    activeSeconds += slice;
                now += slice;
                if (earned > 0)
                    BuyWhatWeCan(progress, catalogue, strategy, now, purchases);
            }

            switch (traced.Kind)
            {
                case BenchmarkEventKind.ActiveStart:
                    background = false;
                    break;

                case BenchmarkEventKind.BackgroundStart:
                    background = true;
                    break;

                case BenchmarkEventKind.Care:
                    progress.ApplyCareMood(traced.Magnitude);
                    break;

                case BenchmarkEventKind.Contact:
                    var part = (BuddyPart)traced.BodyRegion;
                    ImpactSample? accepted = router.Offer(new ContactSample(
                        SourceIdFor(traced.ContentId),
                        traced.ContentId,
                        part,
                        traced.Magnitude,
                        traced.Magnitude,
                        traced.AtSeconds));
                    if (accepted is null)
                    {
                        // A sub-threshold graze never opened an episode, so it is a miss,
                        // not a rejected repeat; only the latter is the dedup evidence.
                        if (traced.Magnitude >= economy.MinimumImpulse)
                            duplicateRejections++;
                        break;
                    }

                    ImpactSample impact = accepted.Value;
                    long milli = progress.AcceptDamage(
                        impact.ContentId,
                        economy.PainCurve.PainFor(impact.Impulse),
                        PayoutRegions.Of(impact.TargetPart),
                        DamageConsciousness.Conscious,
                        impact.TimeSeconds);
                    activeIncome += milli;
                    largestEvent = Math.Max(largestEvent, milli);
                    if (milli > 0)
                        BuyWhatWeCan(progress, catalogue, strategy, traced.AtSeconds, purchases);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(trace), traced.Kind, "Unknown benchmark event kind.");
            }
        }

        return new BenchmarkResult(
            seed,
            strategy.Id,
            now,
            activeSeconds,
            backgroundSeconds,
            activeIncome,
            passiveIncome,
            duplicateRejections,
            purchases,
            progress.BalanceMilliCredits,
            progress.Snapshot().UnlockedToolIds,
            largestEvent);
    }

    /// <summary>
    /// Buys the first still-unowned entry of the strategy's order, repeatedly, while the
    /// balance allows. It never skips a stalled entry: saving up for an expensive item is
    /// exactly "the first unowned one is unaffordable right now".
    /// </summary>
    private static void BuyWhatWeCan(
        BuddyProgressState progress,
        ToolCatalogue catalogue,
        BenchmarkStrategy strategy,
        double now,
        List<BenchmarkPurchase> purchases)
    {
        while (true)
        {
            string? next = null;
            foreach (string contentId in strategy.PurchaseOrder)
            {
                if (!progress.IsToolUnlocked(contentId))
                {
                    next = contentId;
                    break;
                }
            }

            if (next is null)
                return;

            PurchaseResult result = progress.Purchase(next, catalogue);
            if (!result.Succeeded)
                return;

            purchases.Add(new BenchmarkPurchase(next, now, result.PriceMilliCredits));
        }
    }

    /// <summary>
    /// The router's episode key is (source interaction, part). A trace carries no instance
    /// ids, so the tool identity is the source: two contacts from the same tool on the same
    /// part inside the re-arm window are the duplicate the router must reject. FNV-1a, not
    /// <see cref="string.GetHashCode()"/>, because the framework's hash is randomized per
    /// process and a report must be byte-identical across runs.
    /// </summary>
    private static int SourceIdFor(string contentId)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in contentId ?? string.Empty)
            {
                hash = (hash ^ character) * 16777619;
            }

            return (int)hash;
        }
    }
}
