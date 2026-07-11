using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using Godot;
using FileAccess = Godot.FileAccess;

namespace DesktopBuddy.Testing;

/// <summary>
/// Runs an end-to-end journey from <c>tests/journeys/&lt;id&gt;.json</c> via the
/// <c>--journey=&lt;id&gt;</c> entrypoint (AGENT_VERIFICATION_AND_E2E.md Section 3).
/// Journeys assert on read-only state with an explicit timeout; there are no
/// fixed sleeps and no retries. Milestone 0 ships the boot smoke journey and a
/// minimal predicate set (<c>startup_ok</c>, <c>sandbox_composed</c>); the step
/// vocabulary grows with the milestones that need it.
/// </summary>
public partial class JourneyRunner : Node
{
    private RunnerArguments _args = new();

    public void Configure(RunnerArguments args) => _args = args;

    public override async void _Ready()
    {
        string id = _args.JourneyId ?? string.Empty;
        string path = $"res://tests/journeys/{id}.json";
        var stopwatch = Stopwatch.StartNew();

        if (!FileAccess.FileExists(path))
        {
            Fail(id, _args.Seed ?? 0, stopwatch, "journey_file_exists", path, 3);
            return;
        }

        string text;
        using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read))
        {
            text = file.GetAsText();
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException e)
        {
            Fail(id, _args.Seed ?? 0, stopwatch, "journey_json_valid", e.Message, 3);
            return;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            ulong seed = ResolveSeed(root);
            Log.Info("Journey", $"Running journey '{id}' seed={seed}.");

            Dictionary<string, bool> state = await ComputeStateAsync();

            var checks = new List<StartupCheck>();
            bool passed = true;

            if (root.TryGetProperty("assertions", out JsonElement assertions) &&
                assertions.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement assertion in assertions.EnumerateArray())
                {
                    string predicate = assertion.GetProperty("predicate").GetString() ?? "";
                    bool expected = !assertion.TryGetProperty("equals", out JsonElement eq) || eq.GetBoolean();

                    if (!state.TryGetValue(predicate, out bool actual))
                    {
                        checks.Add(new StartupCheck($"assert:{predicate}", false, "unknown predicate"));
                        passed = false;
                        continue;
                    }

                    bool ok = actual == expected;
                    checks.Add(new StartupCheck($"assert:{predicate}", ok, $"expected={expected} actual={actual}"));
                    passed &= ok;
                }
            }
            else
            {
                checks.Add(new StartupCheck("journey_has_assertions", false, "no assertions array"));
                passed = false;
            }

            stopwatch.Stop();
            VerdictWriter.Write("journey", id, seed, passed, checks, new[] { $"seed={seed}" },
                stopwatch.ElapsedMilliseconds, _args.ArtifactsDir);
            Log.Info("Journey", $"Journey '{id}' {(passed ? "PASSED" : "FAILED")}.");
            GetTree().Quit(passed ? 0 : 1);
        }
    }

    private ulong ResolveSeed(JsonElement root)
    {
        if (_args.Seed is ulong fromArgs)
        {
            return fromArgs;
        }

        if (root.TryGetProperty("setup", out JsonElement setup) &&
            setup.TryGetProperty("seed", out JsonElement seed) &&
            seed.TryGetUInt64(out ulong value))
        {
            return value;
        }

        return 0;
    }

    private async System.Threading.Tasks.Task<Dictionary<string, bool>> ComputeStateAsync()
    {
        var state = new Dictionary<string, bool>(StringComparer.Ordinal);

        // Yield one frame so we leave the initial _Ready setup cascade before
        // adding the sandbox to the tree root.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        StartupReport report = StartupValidator.Validate();
        state["startup_ok"] = report.Ok;

        var packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
        bool composed = false;
        if (packed is not null)
        {
            Node instance = packed.Instantiate();
            GetTree().Root.AddChild(instance);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            composed = instance is SandboxRoot && instance.IsInsideTree();
            instance.QueueFree();
        }

        state["sandbox_composed"] = composed;
        return state;
    }

    private void Fail(string id, ulong seed, Stopwatch stopwatch, string check, string detail, int exitCode)
    {
        Log.Error("Journey", $"Journey '{id}' failed: {check} ({detail}).");
        VerdictWriter.Write("journey", id, seed, false,
            new[] { new StartupCheck(check, false, detail) },
            new[] { "journey setup failure" }, stopwatch.ElapsedMilliseconds, _args.ArtifactsDir);
        GetTree().Quit(exitCode);
    }
}
