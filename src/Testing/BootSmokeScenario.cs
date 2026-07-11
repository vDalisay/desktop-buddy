using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Milestone 0 boot smoke scenario (ROADMAP.md / AGENT_VERIFICATION_AND_E2E.md
/// Section 7): launch to sandbox composition, assert startup validation passed,
/// and confirm the sandbox root composed. It is the single scenario CI runs on
/// every push as the headless smoke gate.
/// </summary>
public sealed class BootSmokeScenario : IScenario
{
    public string Id => "boot_smoke";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        StartupReport report = StartupValidator.Validate();
        var checks = new List<StartupCheck>(report.Checks);
        var messages = new List<string> { $"seed={seed}" };

        var packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
        bool loaded = packed is not null;
        checks.Add(new StartupCheck("sandbox_scene_loadable", loaded, "res://scenes/sandbox.tscn"));

        bool composed = false;
        if (loaded)
        {
            Node instance = packed!.Instantiate();
            tree.Root.AddChild(instance);

            // Let _Ready run and one process frame elapse so composition settles.
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            composed = instance is SandboxRoot && instance.IsInsideTree();
            checks.Add(new StartupCheck("sandbox_composed", composed,
                $"{instance.GetType().Name} insideTree={instance.IsInsideTree()}"));

            instance.QueueFree();
        }
        else
        {
            checks.Add(new StartupCheck("sandbox_composed", false, "scene not loaded"));
        }

        bool passed = report.Ok && loaded && composed;
        return new ScenarioResult(passed, checks, messages);
    }
}
