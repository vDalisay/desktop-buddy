using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Persistence;
using Godot;
using FileAccess = Godot.FileAccess;

namespace DesktopBuddy.Testing;

/// <summary>
/// Read-only verification hook invoked only after the normal production Bootstrap has fully
/// composed the sandbox. Unlike TestRunner journeys this never substitutes a persistence store or
/// constructs a parallel coordinator; it observes the exact RunContext production created for the
/// tagged debug export, writes a normal journey verdict, and terminates the verification process.
/// Shipping exports compile the entire Testing tree out.
/// </summary>
public static class ProductionBootstrapJourneyProbe
{
    public static bool Run(
        RunnerArguments args,
        SandboxRoot sandbox,
        RunContext context,
        string saveRoot)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(context);

        string id = args.BootstrapJourneyId ?? string.Empty;
        int phaseIndex = args.BootstrapJourneyPhase ?? 0;
        ulong seed = args.Seed ?? 0;
        var stopwatch = Stopwatch.StartNew();
        var checks = new List<StartupCheck>();
        bool passed = true;

        try
        {
            string path = $"res://tests/journeys/{id}.json";
            if (!FileAccess.FileExists(path))
                return Finish(false, "journey_file_exists", path);

            string text;
            using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read))
                text = file.GetAsText();
            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("phases", out JsonElement phases) ||
                phases.ValueKind != JsonValueKind.Array ||
                phaseIndex < 0 || phaseIndex >= phases.GetArrayLength())
            {
                return Finish(false, "journey_phase_exists", phaseIndex.ToString());
            }

            JsonElement phase = phases[phaseIndex];
            JsonElement setup = phase.TryGetProperty("setup", out JsonElement configuredSetup)
                ? configuredSetup
                : default;
            long expectedWallet = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("expected_wallet_milli", out JsonElement wallet) && wallet.TryGetInt64(out long walletValue)
                    ? walletValue
                    : -1;
            long expectedKeyboard = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("expected_work_keyboard", out JsonElement keyboard) && keyboard.TryGetInt64(out long keyboardValue)
                    ? keyboardValue
                    : -1;

            var state = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["sandbox_composed"] = sandbox.IsInsideTree(),
                ["tag_initial_demo"] = DemoScope.IsSteamDemo && !DemoScope.IsNextFestDemo && !DemoScope.IncludesScenes,
                ["tag_next_fest"] = DemoScope.IsSteamDemo && DemoScope.IsNextFestDemo && DemoScope.IncludesScenes,
                ["split_scene_progress"] = context.SceneProgress is not null && context.UsesSplitSceneProgress,
                ["legacy_scene_progress"] = context.SceneProgress is null && !context.UsesSplitSceneProgress,
                ["scene_manifest_exists"] = System.IO.File.Exists(Path.Combine(saveRoot, SceneProgressTransactionStore.ManifestFileName)),
                ["wallet_preserved"] = expectedWallet < 0 || context.PlayerProgress.BalanceMilliCredits == expectedWallet,
                ["work_preserved"] = expectedKeyboard < 0 ||
                    (context.WorkProgress is not null && context.WorkProgress.Lifetime.KeyboardPresses == expectedKeyboard),
            };

            if (phase.TryGetProperty("assertions", out JsonElement assertions) && assertions.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement assertion in assertions.EnumerateArray())
                {
                    string predicate = assertion.TryGetProperty("predicate", out JsonElement p)
                        ? p.GetString() ?? string.Empty
                        : string.Empty;
                    bool expected = !assertion.TryGetProperty("equals", out JsonElement e) || e.GetBoolean();
                    bool known = state.TryGetValue(predicate, out bool actual);
                    bool ok = known && actual == expected;
                    checks.Add(new StartupCheck(
                        $"assert:{predicate}",
                        ok,
                        known ? $"expected={expected} actual={actual}" : "unknown production-bootstrap predicate"));
                    passed &= ok;
                }
            }
            else
            {
                checks.Add(new StartupCheck("journey_has_assertions", false, "phase has no assertions array"));
                passed = false;
            }
        }
        catch (Exception exception)
        {
            checks.Add(new StartupCheck("bootstrap_probe_exception", false, exception.ToString()));
            passed = false;
        }

        return Finish(passed, null, null);

        bool Finish(bool ok, string? extraName, string? detail)
        {
            if (extraName is not null)
                checks.Add(new StartupCheck(extraName, ok, detail ?? string.Empty));
            stopwatch.Stop();
            VerdictWriter.Write(
                "journey",
                $"{id}_production_phase_{phaseIndex + 1}",
                seed,
                ok,
                checks,
                new[] { $"seed={seed}", $"saveRoot={saveRoot}" },
                stopwatch.ElapsedMilliseconds,
                args.ArtifactsDir);
            Log.Info("BootstrapJourney", $"Production Bootstrap phase {phaseIndex + 1} {(ok ? "PASSED" : "FAILED")}.");
            return ok;
        }
    }
}