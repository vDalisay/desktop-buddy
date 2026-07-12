using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Thirty simulated minutes at 120 Hz, accelerated by debug-only engine time scaling.</summary>
public sealed class IdleSoakScenario : IScenario
{
    public const int FullTicks = 30 * 60 * 120;
    private readonly int _ticks;
    public IdleSoakScenario(int ticks = FullTicks) => _ticks = ticks;
    public string Id => _ticks == FullTicks ? "idle_soak" : "idle_soak_ci";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}", $"ticks={_ticks}", "real fixed-step pump; CI uses three-minute variant, full run is nightly/manual" };
        BuddyLab lab = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn").Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);
        if (!string.IsNullOrEmpty(ScenarioArtifacts.Directory)) lab.EnableTelemetry(ScenarioArtifacts.Directory, Id);
        bool finite = true, awake = true;
        float maximumStrain = 0;
        for (int tick = 0; tick < _ticks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            finite &= lab.Buddy.Rig.AllBodiesFinite();
            foreach (PuppetPartBody part in lab.Buddy.Rig.Parts) awake &= !part.Sleeping;
            foreach (LinkTelemetry link in lab.Buddy.Constraints.Telemetry) maximumStrain = Mathf.Max(maximumStrain, link.Strain);
            if (!finite) break;
        }
        lab.TelemetryRecorder?.Complete();
        checks.Add(new StartupCheck("idle_soak_bodies_finite", finite, $"ticks={_ticks}"));
        checks.Add(new StartupCheck("idle_soak_bodies_awake", awake, "CanSleep=false"));
        checks.Add(new StartupCheck("idle_soak_connected", maximumStrain <= 1.1f, $"max_strain={maximumStrain:F4}"));
        checks.Add(new StartupCheck("idle_soak_contained", lab.Buddy.Recovery.AllBodiesInsideSafeBounds(), "all bodies inside room"));
        checks.Add(new StartupCheck("idle_soak_standing_capable", lab.Buddy.Standing.Snapshot.SupportContactCount > 0, $"supports={lab.Buddy.Standing.Snapshot.SupportContactCount}"));
        if (lab.TelemetryRecorder is not null)
            checks.Add(new StartupCheck("idle_soak_envelope_written", System.IO.File.Exists(lab.TelemetryRecorder.EnvelopePath), lab.TelemetryRecorder.EnvelopePath));
        lab.QueueFree();
        bool passed = true; foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
