using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy.Benchmark;
using DesktopBuddy.Economy;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Replays seeded representative-session traces through the production economy path against
/// the shipped Resources. The scenario validates current authored catalogue/economy invariants,
/// report determinism, and measured income behavior. Purchase-time medians are telemetry only:
/// the retired eleven-item/209-minute M5 pacing table is not a second source of game tuning.
///
/// <para>All file IO for the benchmark lives here — the domain returns values only. The two
/// tuning Resources are loaded from the same paths <c>sandbox.tscn</c> and
/// <c>buddy_lab.tscn</c> reference, so the fingerprint covers what the game actually ships.</para>
/// </summary>
public sealed class EconomyCalibrationScenario : IScenario
{
    public const string PainProfilePath = "res://data/buddy/lab_pain_conversion.tres";
    public const string MoodEconomyPath = "res://data/buddy/m4_mood_economy.tres";

    /// <summary>The committed representative-session seeds. Every strategy runs against all five.</summary>
    public static readonly int[] Seeds = { 1, 7, 13, 29, 101 };

    public string Id => "economy_calibration";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        var pain = GD.Load<PainConversionProfile>(PainProfilePath);
        var mood = GD.Load<MoodEconomyProfile>(MoodEconomyPath);
        if (pain is null || mood is null)
        {
            checks.Add(new StartupCheck(
                "economy_resources_loadable", false, $"{PainProfilePath}, {MoodEconomyPath}"));
            return Task.FromResult(new ScenarioResult(false, checks, messages));
        }

        ToolCatalogue catalogue = CatalogueLoader.Catalogue;
        var errors = new List<string>();
        foreach (string error in pain.Validate())
            errors.Add(error);
        foreach (string error in CataloguePolicy.ValidateLaunchCatalogue(catalogue))
            errors.Add(error);
        if (!mood.IsRuntimeValid)
            errors.Add("mood economy profile is invalid");

        checks.Add(new StartupCheck(
            "shipped_economy_data_valid",
            errors.Count == 0,
            errors.Count == 0 ? "pain, passive, and catalogue all valid" : string.Join("; ", errors)));
        if (errors.Count > 0)
            return Task.FromResult(new ScenarioResult(false, checks, messages));

        var economy = new BenchmarkEconomy(
            pain.BuildCurve(),
            pain.CashPerPain,
            pain.MinimumImpulse,
            mood.NeutralCreditsPerMinute / 60.0);

        BenchmarkReportInput report = Sweep(catalogue, economy);
        string json = BenchmarkReport.Json(report);
        string markdown = BenchmarkReport.Markdown(report);

        // A second sweep from scratch: same seeds, same shipped Resources, byte-identical text.
        string repeated = BenchmarkReport.Json(Sweep(catalogue, economy));
        checks.Add(new StartupCheck(
            "report_is_byte_identical_across_runs",
            repeated == json,
            $"{json.Length} characters"));

        IReadOnlyList<ScheduleRow> rows = BenchmarkReport.Summarize(report.Results, catalogue);
        IReadOnlyList<CatalogueEntry> shop = CataloguePolicy.ShopEntries(catalogue);
        bool reportMatchesShop = rows.Count == shop.Count;
        if (reportMatchesShop)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                reportMatchesShop &= rows[index].ContentId == shop[index].ContentId;
                reportMatchesShop &= rows[index].PriceMilliCredits == shop[index].PriceMilliCredits;
            }
        }

        checks.Add(new StartupCheck(
            "report_tracks_current_shop_catalogue",
            reportMatchesShop,
            $"report_rows={rows.Count} current_shop_entries={shop.Count}"));

        // Observed purchase times remain useful evidence when balancing, but there is no
        // fabricated pass/fail target. A high-priced item not reached in this 209-minute
        // representative session is reported as NaN rather than treated as a prerequisite bug.
        foreach (ScheduleRow row in rows)
        {
            messages.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"observed_{row.ContentId.Replace('.', '_')}=" +
                $"median={row.MedianMinutes:F2}m price={row.PriceMilliCredits / 1000}cr " +
                $"bought={row.SeedsThatBought}/{row.SeedsRun}"));
        }

        foreach (ObligationCheck obligation in report.Obligations)
            checks.Add(new StartupCheck(obligation.Id, obligation.Passed, obligation.Detail));

        messages.Add($"economy_fingerprint={report.EconomyFingerprint}");
        messages.Add(Write(json, markdown));

        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return Task.FromResult(new ScenarioResult(passed, checks, messages));
    }

    private static BenchmarkReportInput Sweep(ToolCatalogue catalogue, BenchmarkEconomy economy)
    {
        var traces = new Dictionary<int, IReadOnlyList<BenchmarkEvent>>(Seeds.Length);
        var fingerprints = new List<KeyValuePair<int, string>>(Seeds.Length);
        foreach (int seed in Seeds)
        {
            IReadOnlyList<BenchmarkEvent> trace = BenchmarkTraceGenerator.Generate(seed);
            traces[seed] = trace;
            fingerprints.Add(new KeyValuePair<int, string>(seed, BenchmarkFingerprint.OfTrace(trace)));
        }

        IReadOnlyList<BenchmarkStrategy> strategies = BenchmarkStrategies.ForCatalogue(catalogue);
        var results = new List<BenchmarkResult>(Seeds.Length * strategies.Count);
        foreach (BenchmarkStrategy strategy in strategies)
        {
            foreach (int seed in Seeds)
                results.Add(EconomyBenchmark.Run(traces[seed], strategy, catalogue, economy, seed));
        }

        return new BenchmarkReportInput(
            BenchmarkFingerprint.OfEconomy(catalogue, economy),
            fingerprints,
            results,
            BenchmarkObligations.Evaluate(results, catalogue, economy),
            catalogue);
    }

    private static string Write(string json, string markdown)
    {
        string directory = ScenarioArtifacts.Directory ?? ".artifacts/economy_calibration";
        Directory.CreateDirectory(directory);
        string jsonPath = Path.Combine(directory, "economy_benchmark.json");
        string markdownPath = Path.Combine(directory, "economy_benchmark.md");
        File.WriteAllText(jsonPath, json);
        File.WriteAllText(markdownPath, markdown);
        return $"artifacts={jsonPath}, {markdownPath}";
    }
}
