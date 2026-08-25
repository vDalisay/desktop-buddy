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
/// Replays seeded representative behaviour through the production economy path against the
/// <b>shipped</b> Resources. The current authored catalogue is the tuning source of truth.
///
/// <para>The fixed 209-minute trace remains valuable as a deterministic income/mechanics sample,
/// but it is no longer treated as a promise that every currently priced shop item must be bought
/// inside the historical M5 timing table. Current shop reachability/free choice is proved directly
/// by the benchmark obligations instead.</para>
///
/// <para>All file IO for the benchmark lives here — the domain returns values only. The tuning
/// Resources are loaded from the same paths the game references, so the fingerprint covers what
/// the game actually ships.</para>
/// </summary>
public sealed class EconomyCalibrationScenario : IScenario
{
    public const string PainProfilePath = "res://data/buddy/lab_pain_conversion.tres";
    public const string MoodEconomyPath = "res://data/buddy/m4_mood_economy.tres";

    /// <summary>The committed calibration seeds. Every strategy runs against all five.</summary>
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

        // A second sweep from scratch: same seeds, same Resources, byte-identical text.
        string repeated = BenchmarkReport.Json(Sweep(catalogue, economy));
        checks.Add(new StartupCheck(
            "report_is_byte_identical_across_runs",
            repeated == json,
            $"{json.Length} characters"));

        foreach (ObligationCheck obligation in report.Obligations)
            checks.Add(new StartupCheck(obligation.Id, obligation.Passed, obligation.Detail));

        IReadOnlyList<ScheduleRow> observations = BenchmarkReport.Summarize(report.Results, catalogue);
        int reachedEverywhere = 0;
        foreach (ScheduleRow row in observations)
            reachedEverywhere += row.BoughtEverywhere ? 1 : 0;

        messages.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"representative_session_observed={reachedEverywhere}/{observations.Count} current shop entries in every seed; timings are diagnostic only"));
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

        var results = new List<BenchmarkResult>(Seeds.Length * BenchmarkStrategies.All.Count);
        foreach (BenchmarkStrategy strategy in BenchmarkStrategies.All)
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
