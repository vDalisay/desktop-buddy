using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>One §1.1 slot as the completionist sweep actually measured it.</summary>
public readonly record struct ScheduleRow(
    string ContentId,
    long PriceMilliCredits,
    double TargetMinutes,
    double MedianMinutes,
    int SeedsThatBought,
    int SeedsRun)
{
    public bool BoughtEverywhere => SeedsRun > 0 && SeedsThatBought == SeedsRun;

    /// <summary>Signed fraction the median is off target; <c>NaN</c> when a seed never bought it.</summary>
    public double Deviation => BoughtEverywhere && TargetMinutes > 0.0
        ? (MedianMinutes - TargetMinutes) / TargetMinutes
        : double.NaN;

    public bool InBand => Math.Abs(Deviation) <= BenchmarkSchedule.ToleranceFraction;
}

/// <summary>Everything one report prints. The scenario supplies it; only it does file IO.</summary>
public sealed record BenchmarkReportInput(
    string EconomyFingerprint,
    IReadOnlyList<KeyValuePair<int, string>> TraceFingerprints,
    IReadOnlyList<BenchmarkResult> Results,
    IReadOnlyList<ObligationCheck> Obligations,
    ToolCatalogue Catalogue);

/// <summary>
/// Renders a benchmark sweep as deterministic JSON and Markdown: same data, stable key
/// order, invariant-culture fixed-decimal numbers. Two runs of the same seeds must produce
/// byte-identical text, so a diff only ever shows an economy change.
/// </summary>
public static class BenchmarkReport
{
    public const int Version = 1;

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return double.NaN;

        var sorted = new List<double>(values);
        sorted.Sort();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>The completionist schedule table — the only rows judged against §1.1.</summary>
    public static IReadOnlyList<ScheduleRow> Summarize(
        IReadOnlyList<BenchmarkResult> results,
        ToolCatalogue catalogue)
    {
        var completionist = new List<BenchmarkResult>();
        foreach (BenchmarkResult result in results)
        {
            if (result.StrategyId == BenchmarkStrategies.CompletionistId)
                completionist.Add(result);
        }

        var rows = new List<ScheduleRow>(BenchmarkSchedule.Targets.Count);
        foreach (ScheduleTarget target in BenchmarkSchedule.Targets)
        {
            var minutes = new List<double>(completionist.Count);
            foreach (BenchmarkResult result in completionist)
            {
                foreach (BenchmarkPurchase purchase in result.Purchases)
                {
                    if (purchase.ContentId == target.ContentId)
                    {
                        minutes.Add(purchase.AtSeconds / 60.0);
                        break;
                    }
                }
            }

            catalogue.TryGet(target.ContentId, out CatalogueEntry entry);
            rows.Add(new ScheduleRow(
                target.ContentId,
                entry.PriceMilliCredits,
                target.TargetMinutes,
                Median(minutes),
                minutes.Count,
                completionist.Count));
        }

        return rows;
    }

    public static bool AllInBand(IReadOnlyList<ScheduleRow> rows)
    {
        bool all = rows.Count > 0;
        foreach (ScheduleRow row in rows)
            all &= row.InBand;
        return all;
    }

    public static bool AllObligationsPassed(IReadOnlyList<ObligationCheck> obligations)
    {
        bool all = obligations.Count > 0;
        foreach (ObligationCheck obligation in obligations)
            all &= obligation.Passed;
        return all;
    }

    public static string Json(BenchmarkReportInput input)
    {
        IReadOnlyList<ScheduleRow> rows = Summarize(input.Results, input.Catalogue);
        var text = new StringBuilder();
        text.Append("{\n");
        text.Append($"  \"report_version\": {Version},\n");
        text.Append($"  \"economy_fingerprint\": {Quote(input.EconomyFingerprint)},\n");
        text.Append("  \"trace_fingerprints\": {\n");
        for (int index = 0; index < input.TraceFingerprints.Count; index++)
        {
            KeyValuePair<int, string> trace = input.TraceFingerprints[index];
            text.Append($"    \"{trace.Key}\": {Quote(trace.Value)}");
            text.Append(index + 1 < input.TraceFingerprints.Count ? ",\n" : "\n");
        }

        text.Append("  },\n");

        text.Append("  \"completionist_schedule\": [\n");
        for (int index = 0; index < rows.Count; index++)
        {
            ScheduleRow row = rows[index];
            text.Append("    {");
            text.Append($"\"content_id\": {Quote(row.ContentId)}, ");
            text.Append($"\"price_credits\": {Fixed(row.PriceMilliCredits / 1000.0)}, ");
            text.Append($"\"target_minutes\": {Fixed(row.TargetMinutes)}, ");
            text.Append($"\"median_minutes\": {Fixed(row.MedianMinutes)}, ");
            text.Append($"\"deviation_percent\": {Fixed(row.Deviation * 100.0)}, ");
            text.Append($"\"seeds_that_bought\": {row.SeedsThatBought}, ");
            text.Append($"\"in_band\": {Bool(row.InBand)}}}");
            text.Append(index + 1 < rows.Count ? ",\n" : "\n");
        }

        text.Append("  ],\n");

        text.Append("  \"obligations\": [\n");
        for (int index = 0; index < input.Obligations.Count; index++)
        {
            ObligationCheck obligation = input.Obligations[index];
            text.Append(
                $"    {{\"id\": {Quote(obligation.Id)}, \"passed\": {Bool(obligation.Passed)}, " +
                $"\"detail\": {Quote(obligation.Detail)}}}");
            text.Append(index + 1 < input.Obligations.Count ? ",\n" : "\n");
        }

        text.Append("  ],\n");

        text.Append("  \"runs\": [\n");
        for (int index = 0; index < input.Results.Count; index++)
        {
            AppendRunJson(text, input.Results[index]);
            text.Append(index + 1 < input.Results.Count ? ",\n" : "\n");
        }

        text.Append("  ]\n}\n");
        return text.ToString();
    }

    private static void AppendRunJson(StringBuilder text, BenchmarkResult result)
    {
        text.Append("    {\n");
        text.Append($"      \"seed\": {result.Seed},\n");
        text.Append($"      \"strategy\": {Quote(result.StrategyId)},\n");
        text.Append($"      \"running_minutes\": {Fixed(result.RunningSeconds / 60.0)},\n");
        text.Append($"      \"active_minutes\": {Fixed(result.ActiveSeconds / 60.0)},\n");
        text.Append($"      \"background_minutes\": {Fixed(result.BackgroundSeconds / 60.0)},\n");
        text.Append($"      \"active_credits\": {Fixed(result.ActiveIncomeMilliCredits / 1000.0)},\n");
        text.Append($"      \"passive_credits\": {Fixed(result.PassiveIncomeMilliCredits / 1000.0)},\n");
        text.Append($"      \"total_credits\": {Fixed(result.TotalIncomeMilliCredits / 1000.0)},\n");
        text.Append($"      \"duplicate_rejections\": {result.DuplicateContactRejections},\n");
        text.Append($"      \"largest_event_credits\": {Fixed(result.LargestSingleEventMilliCredits / 1000.0)},\n");
        text.Append($"      \"ending_balance_credits\": {Fixed(result.EndingBalanceMilliCredits / 1000.0)},\n");
        text.Append("      \"purchases\": [");
        for (int index = 0; index < result.Purchases.Count; index++)
        {
            BenchmarkPurchase purchase = result.Purchases[index];
            text.Append(
                $"{{\"content_id\": {Quote(purchase.ContentId)}, " +
                $"\"at_minutes\": {Fixed(purchase.AtSeconds / 60.0)}, " +
                $"\"price_credits\": {Fixed(purchase.PriceMilliCredits / 1000.0)}}}");
            if (index + 1 < result.Purchases.Count)
                text.Append(", ");
        }

        text.Append("],\n");
        text.Append("      \"ending_ownership\": [");
        for (int index = 0; index < result.EndingOwnership.Count; index++)
        {
            text.Append(Quote(result.EndingOwnership[index]));
            if (index + 1 < result.EndingOwnership.Count)
                text.Append(", ");
        }

        text.Append("]\n    }");
    }

    public static string Markdown(BenchmarkReportInput input)
    {
        IReadOnlyList<ScheduleRow> rows = Summarize(input.Results, input.Catalogue);
        var text = new StringBuilder();
        text.Append("# M5 economy benchmark\n\n");
        text.Append($"Report version {Version}. Economy fingerprint `{input.EconomyFingerprint}`.\n\n");
        text.Append("| Seed | Trace fingerprint |\n|---|---|\n");
        foreach (KeyValuePair<int, string> trace in input.TraceFingerprints)
            text.Append($"| {trace.Key} | `{trace.Value}` |\n");

        text.Append("\n## Completionist schedule (median of all seeds)\n\n");
        text.Append("| Item | Price | Target min | Median min | Deviation | In band |\n");
        text.Append("|---|---:|---:|---:|---:|---|\n");
        foreach (ScheduleRow row in rows)
        {
            text.Append(
                $"| {row.ContentId} | {Fixed(row.PriceMilliCredits / 1000.0)} | " +
                $"{Fixed(row.TargetMinutes)} | {Fixed(row.MedianMinutes)} | " +
                $"{Fixed(row.Deviation * 100.0)}% | {(row.InBand ? "yes" : "NO")} |\n");
        }

        text.Append("\n## Proof obligations\n\n| Obligation | Result | Detail |\n|---|---|---|\n");
        foreach (ObligationCheck obligation in input.Obligations)
        {
            text.Append(
                $"| {obligation.Id} | {(obligation.Passed ? "pass" : "FAIL")} | " +
                $"{obligation.Detail} |\n");
        }

        text.Append("\n## Runs\n\n");
        text.Append("| Strategy | Seed | Run min | Active min | Active cr | Passive cr | ");
        text.Append("Dup rejects | Largest cr | End balance | Bought |\n");
        text.Append("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|\n");
        foreach (BenchmarkResult result in input.Results)
        {
            text.Append(
                $"| {result.StrategyId} | {result.Seed} | {Fixed(result.RunningSeconds / 60.0)} | " +
                $"{Fixed(result.ActiveSeconds / 60.0)} | " +
                $"{Fixed(result.ActiveIncomeMilliCredits / 1000.0)} | " +
                $"{Fixed(result.PassiveIncomeMilliCredits / 1000.0)} | " +
                $"{result.DuplicateContactRejections} | " +
                $"{Fixed(result.LargestSingleEventMilliCredits / 1000.0)} | " +
                $"{Fixed(result.EndingBalanceMilliCredits / 1000.0)} | " +
                $"{result.Purchases.Count} |\n");
        }

        text.Append("\n## Purchase timelines\n\n");
        foreach (BenchmarkResult result in input.Results)
        {
            text.Append($"- **{result.StrategyId}** seed {result.Seed}: ");
            if (result.Purchases.Count == 0)
            {
                text.Append("nothing bought");
            }
            else
            {
                for (int index = 0; index < result.Purchases.Count; index++)
                {
                    BenchmarkPurchase purchase = result.Purchases[index];
                    text.Append(
                        $"{purchase.ContentId} @{Fixed(purchase.AtSeconds / 60.0)}m " +
                        $"({Fixed(purchase.PriceMilliCredits / 1000.0)}cr)");
                    if (index + 1 < result.Purchases.Count)
                        text.Append(", ");
                }
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static string Fixed(double value) => double.IsNaN(value)
        ? "null"
        : value.ToString("F2", CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
