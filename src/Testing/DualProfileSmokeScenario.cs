using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Laboratory;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class DualProfileSmokeScenario : IScenario
{
    public string Id => "dual_profile_smoke";
    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>(); var messages = new List<string> { $"seed={seed}", "ticks=1200" };
        DualProfileLab lab = GD.Load<PackedScene>("res://scenes/dual_profile_lab.tscn").Instantiate<DualProfileLab>();
        tree.Root.AddChild(lab); await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.BuddyA.ReseedAutonomy(seed); lab.BuddyB.ReseedAutonomy(seed);
        bool finite = true;
        for (int i = 0; i < 1200; i++) { await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame); finite &= lab.BuddyA.Rig.AllBodiesFinite() && lab.BuddyB.Rig.AllBodiesFinite(); }
        checks.Add(new StartupCheck("dual_profiles_initialized", lab.BuddyA.IsInitialized && lab.BuddyB.IsInitialized, "A+B"));
        checks.Add(new StartupCheck("dual_profiles_finite_ten_seconds", finite, "1200 ticks"));
        lab.QueueFree(); bool passed = true; foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
