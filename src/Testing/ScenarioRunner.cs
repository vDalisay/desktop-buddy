using System;
using System.Collections.Generic;
using System.Diagnostics;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Hosts a single headless scenario from the <c>--scenario=&lt;id&gt; --seed=&lt;n&gt;</c>
/// runner protocol (ARCHITECTURE.md Section 22). Resolves the scenario from
/// <see cref="ScenarioCatalog"/>, runs it, emits a verdict, and quits with exit
/// code 0 (pass) or 1 (fail); an unknown scenario id exits 3.
/// </summary>
public partial class ScenarioRunner : Node
{
    private RunnerArguments _args = new();

    public void Configure(RunnerArguments args) => _args = args;

    public override async void _Ready()
    {
        string id = _args.ScenarioId ?? string.Empty;
        ulong seed = _args.Seed ?? 0;
        var stopwatch = Stopwatch.StartNew();

        IScenario? scenario = ScenarioCatalog.Find(id);
        if (scenario is null)
        {
            Log.Error("Scenario", $"Unknown scenario '{id}'. Known: {string.Join(", ", ScenarioCatalog.Ids)}");
            VerdictWriter.Write("scenario", id, seed, false,
                new[] { new StartupCheck("scenario_known", false, id) },
                new[] { "unknown scenario id" }, stopwatch.ElapsedMilliseconds, _args.ArtifactsDir);
            QuitSafely(3);
            return;
        }

        Log.Info("Scenario", $"Running scenario '{id}' seed={seed}.");

        // Yield one frame so we leave the initial _Ready setup cascade before a
        // scenario adds nodes to the tree root (add_child fails while a parent is
        // "busy setting up children").
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        ScenarioResult result;
        try
        {
            ScenarioArtifacts.Directory = _args.ArtifactsDir;
            result = await scenario.RunAsync(GetTree(), seed);
        }
        catch (Exception e)
        {
            Log.Error("Scenario", $"Scenario '{id}' threw: {e}");
            VerdictWriter.Write("scenario", id, seed, false,
                new[] { new StartupCheck("scenario_threw", false, e.Message) },
                new[] { e.GetType().Name }, stopwatch.ElapsedMilliseconds, _args.ArtifactsDir);
            QuitSafely(1);
            return;
        }

        ScenarioArtifacts.Directory = null;
        stopwatch.Stop();
        VerdictWriter.Write("scenario", id, seed, result.Passed, result.Checks, result.Messages,
            stopwatch.ElapsedMilliseconds, _args.ArtifactsDir);
        Log.Info("Scenario", $"Scenario '{id}' {(result.Passed ? "PASSED" : "FAILED")}.");
        QuitSafely(result.Passed ? 0 : 1);
    }

    private void QuitSafely(int exitCode)
    {
        GodotInteropShutdown.PrepareForQuit();
        GetTree().Quit(exitCode);
    }
}
