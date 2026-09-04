using System;
using System.Collections.Generic;
using System.Globalization;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>One proof obligation and what it measured.</summary>
public readonly record struct ObligationCheck(string Id, bool Passed, string Detail);

/// <summary>
/// Economy invariants evaluated against the shipped catalogue and production economy path.
/// These deliberately avoid owning a second set of prices or unlock-time targets: authored
/// catalogue data is the source of truth, while the representative trace measures income,
/// passive share, event size, deduplication, and determinism.
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
            NoDoubleMilestoneSkip(results, catalogue),
            DedupPaysPositiveZeroPositive(catalogue, economy),
            FreeChoiceWithoutPrerequisites(catalogue, economy),
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

    private static ObligationCheck NoDoubleMilestoneSkip(
        IReadOnlyList<BenchmarkResult> results,
        ToolCatalogue catalogue)
    {
        long largest = 0;
        foreach (BenchmarkResult result in results)
            largest = Math.Max(largest, result.LargestSingleEventMilliCredits);

        // Compare a normal payout with two adjacent CURRENT shop prices. This keeps the guard
        // tied to the real progression ladder even when items are added or repriced.
        IReadOnlyList<CatalogueEntry> shop = CataloguePolicy.ShopEntries(catalogue);
        long tightestPair = long.MaxValue;
        for (int index = 0; index + 1 < shop.Count; index++)
        {
            tightestPair = Math.Min(
                tightestPair,
                shop[index].PriceMilliCredits + shop[index + 1].PriceMilliCredits);
        }

        bool passed = shop.Count >= 2 && tightestPair != long.MaxValue && largest < tightestPair;
        return new ObligationCheck(
            "no_ordinary_event_skips_two_milestones",
            passed,
            $"largest single payout {Number(largest / 1000.0)} credits vs tightest adjacent " +
            $"current-shop pair {Number(tightestPair / 1000.0)} credits");
    }

    private static ObligationCheck DedupPaysPositiveZeroPositive(
        ToolCatalogue catalogue,
        BenchmarkEconomy economy)
    {
        // Three prefixes of one trace, replayed through the real router and ledger: a hit,
        // the same tool still resting on the same part, then a genuine second hit after the
        // re-arm window. The middle prefix must add nothing.
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

    private static ObligationCheck FreeChoiceWithoutPrerequisites(
        ToolCatalogue catalogue,
        BenchmarkEconomy economy)
    {
        IReadOnlyList<CatalogueEntry> shop = CataloguePolicy.ShopEntries(catalogue);
        var failures = new List<string>();

        // This is the actual gameplay rule we care about. Give a fresh save exactly one item's
        // authored price and attempt that item directly through BuddyProgressState.Purchase.
        // A 209-minute synthetic trace no longer decides whether a 10,000-credit item has a
        // prerequisite; affordability and prerequisite freedom are separate concerns.
        foreach (CatalogueEntry entry in shop)
        {
            var progress = new BuddyProgressState(
                economy.CashPerPain,
                initialBalanceMilliCredits: entry.PriceMilliCredits);
            PurchaseResult result = progress.Purchase(entry.ContentId, catalogue);
            bool bought = result.Succeeded &&
                          result.PriceMilliCredits == entry.PriceMilliCredits &&
                          result.BalanceMilliCredits == 0 &&
                          progress.IsToolUnlocked(entry.ContentId);
            if (!bought)
            {
                failures.Add(
                    $"{entry.ContentId}: status={result.Status} authored={entry.PriceMilliCredits} " +
                    $"charged={result.PriceMilliCredits} balance={result.BalanceMilliCredits}");
            }
        }

        return new ObligationCheck(
            "any_item_may_be_bought_first",
            shop.Count > 0 && failures.Count == 0,
            failures.Count == 0
                ? $"all {shop.Count} current shop entries independently buy from a fresh save " +
                  "at their authored price with no prerequisite ownership"
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
