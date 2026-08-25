using System;
using System.Collections.Generic;
using System.Globalization;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>One proof obligation and what it measured.</summary>
public readonly record struct ObligationCheck(string Id, bool Passed, string Detail);

/// <summary>
/// Structural economy obligations evaluated against the current shipped catalogue and the
/// representative seeded behaviour traces. Historical minute targets are intentionally not proof
/// obligations: authored catalogue data is the current tuning source of truth.
/// </summary>
public static class BenchmarkObligations
{
    public const double PassiveShareMinimum = 0.20;
    public const double PassiveShareMaximum = 0.30;

    public static IReadOnlyList<ObligationCheck> Evaluate(
        IReadOnlyList<BenchmarkResult> results,
        ToolCatalogue catalogue,
        BenchmarkEconomy economy)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(catalogue);

        var completionist = new List<BenchmarkResult>();
        foreach (BenchmarkResult result in results)
        {
            if (result.StrategyId == BenchmarkStrategies.CompletionistId)
                completionist.Add(result);
        }

        return new[]
        {
            ActiveDominatesPassive(completionist),
            PassiveShareOfActive(completionist, economy),
            CurrentShopPriceLadder(catalogue),
            NoDoubleMilestoneSkip(results, catalogue),
            DedupPaysPositiveZeroPositive(catalogue, economy),
            EveryItemMayBeBoughtFirst(catalogue),
            FingerprintTracksPricesOnly(catalogue, economy),
        };
    }

    private static ObligationCheck ActiveDominatesPassive(IReadOnlyList<BenchmarkResult> completionist)
    {
        bool passed = completionist.Count > 0;
        double worst = double.MaxValue;
        foreach (BenchmarkResult result in completionist)
        {
            passed &= result.ActiveIncomeMilliCredits > result.PassiveIncomeMilliCredits;
            worst = Math.Min(
                worst,
                result.ActiveIncomeMilliCredits - (double)result.PassiveIncomeMilliCredits);
        }

        return new ObligationCheck(
            "active_income_dominates_passive",
            passed,
            $"smallest active-minus-passive margin {Number(worst / 1000.0)} credits " +
            $"across {completionist.Count} completionist runs");
    }

    private static ObligationCheck PassiveShareOfActive(
        IReadOnlyList<BenchmarkResult> completionist,
        BenchmarkEconomy economy)
    {
        var rates = new List<double>(completionist.Count);
        foreach (BenchmarkResult result in completionist)
        {
            if (result.ActiveSeconds > 0.0)
            {
                rates.Add(result.ActiveIncomeMilliCredits / 1000.0 /
                          (result.ActiveSeconds / 60.0));
            }
        }

        double activeRate = BenchmarkReport.Median(rates);
        // Peak mood doubles the base rate (PassiveIncome.MoodMultiplier at +100).
        double peakPassiveRate = economy.PassiveCreditsPerSecond * 60.0 * 2.0;
        double share = activeRate > 0.0 ? peakPassiveRate / activeRate : double.NaN;
        return new ObligationCheck(
            "peak_passive_is_20_to_30_percent_of_active",
            share >= PassiveShareMinimum && share <= PassiveShareMaximum,
            $"peak passive {Number(peakPassiveRate)} cr/min vs active {Number(activeRate)} " +
            $"cr/active-min = {Number(share * 100.0)}%");
    }

    private static ObligationCheck CurrentShopPriceLadder(ToolCatalogue catalogue)
    {
        long previous = -1;
        string previousId = "<start>";
        foreach (string contentId in BenchmarkSchedule.PurchasableOrder)
        {
            if (!catalogue.TryGet(contentId, out CatalogueEntry entry) || !entry.HasValidPrice)
            {
                return new ObligationCheck(
                    "current_shop_prices_follow_authored_ladder",
                    false,
                    $"'{contentId}' is missing or has no valid authored price");
            }

            if (entry.PriceMilliCredits < previous)
            {
                return new ObligationCheck(
                    "current_shop_prices_follow_authored_ladder",
                    false,
                    $"'{contentId}' ({Number(entry.PriceMilliCredits / 1000.0)}cr) is cheaper than " +
                    $"earlier '{previousId}' ({Number(previous / 1000.0)}cr)");
            }

            previous = entry.PriceMilliCredits;
            previousId = contentId;
        }

        return new ObligationCheck(
            "current_shop_prices_follow_authored_ladder",
            BenchmarkSchedule.PurchasableOrder.Count > 0,
            $"{BenchmarkSchedule.PurchasableOrder.Count} current purchasables are non-decreasing by authored price");
    }

    private static ObligationCheck NoDoubleMilestoneSkip(
        IReadOnlyList<BenchmarkResult> results,
        ToolCatalogue catalogue)
    {
        long largest = 0;
        foreach (BenchmarkResult result in results)
            largest = Math.Max(largest, result.LargestSingleEventMilliCredits);

        long tightestPair = long.MaxValue;
        IReadOnlyList<string> order = BenchmarkSchedule.PurchasableOrder;
        for (int index = 0; index + 1 < order.Count; index++)
        {
            if (!catalogue.TryGet(order[index], out CatalogueEntry first) ||
                !catalogue.TryGet(order[index + 1], out CatalogueEntry second))
            {
                return new ObligationCheck(
                    "no_ordinary_event_skips_two_milestones",
                    false,
                    "current benchmark purchase order contains an item missing from the catalogue");
            }

            tightestPair = Math.Min(tightestPair, first.PriceMilliCredits + second.PriceMilliCredits);
        }

        bool hasPair = tightestPair != long.MaxValue;
        return new ObligationCheck(
            "no_ordinary_event_skips_two_milestones",
            hasPair && largest < tightestPair,
            $"largest single payout {Number(largest / 1000.0)} credits vs tightest adjacent " +
            $"pair {(hasPair ? Number(tightestPair / 1000.0) : "n/a")} credits");
    }

    private static ObligationCheck DedupPaysPositiveZeroPositive(
        ToolCatalogue catalogue,
        BenchmarkEconomy economy)
    {
        const string tool = ContentIds.ToolBoxingGlove;
        var events = new[]
        {
            new BenchmarkEvent(0.0, BenchmarkEventKind.Contact, tool, 3000.0f, 0),
            new BenchmarkEvent(0.05, BenchmarkEventKind.Contact, tool, 3000.0f, 0),
            new BenchmarkEvent(1.0, BenchmarkEventKind.Contact, tool, 3000.0f, 0),
        };

        var strategy = new BenchmarkStrategy("dedup_probe", Array.Empty<string>());
        long first = Replay(events, 1);
        long second = Replay(events, 2);
        long third = Replay(events, 3);
        int rejections = EconomyBenchmark
            .Run(Prefix(events, 2), strategy, catalogue, economy)
            .DuplicateContactRejections;

        return new ObligationCheck(
            "duplicate_contact_pays_zero_between_two_positives",
            first > 0 && second == first && third > second && rejections == 1,
            $"+{Number(first / 1000.0)}, +{Number((second - first) / 1000.0)}, " +
            $"+{Number((third - second) / 1000.0)} credits; rejections={rejections}");

        long Replay(IReadOnlyList<BenchmarkEvent> trace, int count) => EconomyBenchmark
            .Run(Prefix(trace, count), strategy, catalogue, economy)
            .ActiveIncomeMilliCredits;
    }

    private static IReadOnlyList<BenchmarkEvent> Prefix(IReadOnlyList<BenchmarkEvent> trace, int count)
    {
        var prefix = new List<BenchmarkEvent>(count);
        for (int index = 0; index < count; index++)
            prefix.Add(trace[index]);
        return prefix;
    }

    private static ObligationCheck EveryItemMayBeBoughtFirst(ToolCatalogue catalogue)
    {
        var failures = new List<string>();
        int tested = 0;
        foreach (string contentId in BenchmarkSchedule.PurchasableOrder)
        {
            if (!catalogue.TryGet(contentId, out CatalogueEntry entry) || !entry.HasValidPrice)
            {
                failures.Add($"{contentId}: missing/invalid price");
                continue;
            }

            tested++;
            var progress = new BuddyProgressState(cashPerPain: 1.0);
            progress.Deposit(entry.PriceMilliCredits);
            PurchaseResult purchase = progress.Purchase(contentId, catalogue);
            if (!purchase.Succeeded ||
                purchase.PriceMilliCredits != entry.PriceMilliCredits ||
                !progress.IsToolUnlocked(contentId) ||
                progress.BalanceMilliCredits != 0)
            {
                failures.Add(
                    $"{contentId}: status={purchase.Status}, charged={purchase.PriceMilliCredits}, " +
                    $"owned={progress.IsToolUnlocked(contentId)}, balance={progress.BalanceMilliCredits}");
            }
        }

        return new ObligationCheck(
            "any_current_shop_item_may_be_bought_first",
            tested == BenchmarkSchedule.PurchasableOrder.Count && tested > 0 && failures.Count == 0,
            failures.Count == 0
                ? $"{tested}/{BenchmarkSchedule.PurchasableOrder.Count} current shop entries can be the first exactly-funded purchase"
                : string.Join("; ", failures));
    }

    private static ObligationCheck FingerprintTracksPricesOnly(
        ToolCatalogue catalogue,
        BenchmarkEconomy economy)
    {
        string baseline = BenchmarkFingerprint.OfEconomy(catalogue, economy);
        var repriced = new List<CatalogueEntry>(catalogue.Count);
        bool bumped = false;
        foreach (CatalogueEntry entry in catalogue.Entries)
        {
            if (!bumped && !entry.IsStarting && entry.HasValidPrice)
            {
                repriced.Add(entry with
                {
                    PriceMilliCredits = entry.PriceMilliCredits + RewardLedger.MilliCreditsPerCredit,
                });
                bumped = true;
                continue;
            }

            repriced.Add(entry);
        }

        string afterPriceEdit = BenchmarkFingerprint.OfEconomy(new ToolCatalogue(repriced), economy);
        string traceOne = BenchmarkFingerprint.OfTrace(BenchmarkTraceGenerator.Generate(1));
        string traceOneAgain = BenchmarkFingerprint.OfTrace(BenchmarkTraceGenerator.Generate(1));
        string traceTwo = BenchmarkFingerprint.OfTrace(BenchmarkTraceGenerator.Generate(7));

        return new ObligationCheck(
            "fingerprint_moves_with_price_not_with_seed",
            bumped &&
            afterPriceEdit != baseline &&
            traceOne == traceOneAgain &&
            traceOne != traceTwo,
            $"economy {baseline} -> {afterPriceEdit} after a +1 credit edit; trace seed 1 " +
            $"{traceOne} stable, seed 7 {traceTwo}");
    }

    private static string Number(double value) =>
        value.ToString("F2", CultureInfo.InvariantCulture);
}
