using System;
using System.Collections.Generic;
using System.Globalization;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>One proof obligation and what it measured.</summary>
public readonly record struct ObligationCheck(string Id, bool Passed, string Detail);

/// <summary>
/// The six M5 §4.3 proof obligations, evaluated over a whole benchmark sweep. They are
/// facts about the run, not about the code, so they are asserted from the measured results
/// rather than restated as expectations in a test.
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
            FreeChoiceWithoutPrerequisites(results, catalogue),
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

        // Skipping "more than one milestone" means one payout covering two adjacent slots
        // at once, so the tightest adjacent pair is the ceiling an ordinary event must stay
        // under.
        long tightestPair = long.MaxValue;
        IReadOnlyList<string> order = BenchmarkSchedule.PurchasableOrder;
        for (int index = 0; index + 1 < order.Count; index++)
        {
            catalogue.TryGet(order[index], out CatalogueEntry first);
            catalogue.TryGet(order[index + 1], out CatalogueEntry second);
            tightestPair = Math.Min(tightestPair, first.PriceMilliCredits + second.PriceMilliCredits);
        }

        return new ObligationCheck(
            "no_ordinary_event_skips_two_milestones",
            largest < tightestPair,
            $"largest single payout {Number(largest / 1000.0)} credits vs tightest adjacent " +
            $"pair {Number(tightestPair / 1000.0)} credits");
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
        IReadOnlyList<BenchmarkResult> results,
        ToolCatalogue catalogue)
    {
        bool passed = true;
        int completionistRuns = 0;
        int saveRuns = 0;
        var failures = new List<string>();

        foreach (BenchmarkResult result in results)
        {
            if (result.StrategyId == BenchmarkStrategies.CompletionistId)
            {
                completionistRuns++;
                if (result.Purchases.Count != BenchmarkSchedule.PurchasableOrder.Count)
                {
                    passed = false;
                    failures.Add(
                        $"{result.StrategyId}/seed {result.Seed} bought " +
                        $"{result.Purchases.Count} of {BenchmarkSchedule.PurchasableOrder.Count}");
                }

                continue;
            }

            if (!result.StrategyId.StartsWith("save_for_", StringComparison.Ordinal))
                continue;

            saveRuns++;
            string target = result.Purchases.Count > 0 ? result.Purchases[0].ContentId : "<none>";
            if (!BoughtTargetBeforeACheaperEarlierItem(result, catalogue, out string detail))
            {
                passed = false;
                failures.Add($"{result.StrategyId}/seed {result.Seed}: {detail} (first={target})");
            }
        }

        return new ObligationCheck(
            "any_item_may_be_bought_first",
            passed && completionistRuns > 0 && saveRuns > 0,
            failures.Count == 0
                ? $"{completionistRuns} completionist runs bought all twelve; {saveRuns} " +
                  "save runs bought their target ahead of a cheaper earlier item"
                : string.Join("; ", failures));
    }

    private static bool BoughtTargetBeforeACheaperEarlierItem(
        BenchmarkResult result,
        ToolCatalogue catalogue,
        out string detail)
    {
        detail = "no purchases";
        if (result.Purchases.Count == 0)
            return false;

        string target = result.Purchases[0].ContentId;
        long targetPrice = result.Purchases[0].PriceMilliCredits;
        IReadOnlyList<string> order = BenchmarkSchedule.PurchasableOrder;

        foreach (string contentId in order)
        {
            if (contentId == target)
                break;
            if (!catalogue.TryGet(contentId, out CatalogueEntry entry) ||
                entry.PriceMilliCredits >= targetPrice)
            {
                continue;
            }

            int boughtAt = -1;
            for (int index = 0; index < result.Purchases.Count; index++)
            {
                if (result.Purchases[index].ContentId == contentId)
                {
                    boughtAt = index;
                    break;
                }
            }

            if (boughtAt != 0)
            {
                detail = $"'{target}' bought before cheaper earlier '{contentId}'";
                return true;
            }
        }

        detail = $"nothing cheaper and earlier than '{target}' was left unbought";
        return false;
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
