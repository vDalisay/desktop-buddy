using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Laboratory;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class RepeatEnvelopeScenario : IScenario
{
    public string Id => "repeat_envelope";
    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        EnvelopeBoundsProfile bounds = GD.Load<EnvelopeBoundsProfile>("res://data/buddy/lab_envelope_bounds.tres");
        var checks = new List<StartupCheck>(); var messages = new List<string> { $"seed={seed}", "same=5 different=5" };
        float minSettle = float.PositiveInfinity, maxSettle = 0, maxStrain = 0;
        var finalPositions = new Vector2[10];
        for (int run = 0; run < 10; run++)
        {
            BuddyLab lab = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn").Instantiate<BuddyLab>();
            tree.Root.AddChild(lab); await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            lab.Controls.Reseed(run < 5 ? seed : seed + (ulong)(run - 4));
            int settled = bounds.MaximumSettleTicks;
            for (int tick = 0; tick < bounds.MaximumSettleTicks; tick++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
                foreach (LinkTelemetry link in lab.Buddy.Constraints.Telemetry) maxStrain = Mathf.Max(maxStrain, link.Strain);
                if (lab.Buddy.Standing.Snapshot.IsStable) { settled = tick + 1; break; }
            }
            minSettle = Mathf.Min(minSettle, settled); maxSettle = Mathf.Max(maxSettle, settled);
            finalPositions[run] = lab.Buddy.Rig.Torso.GlobalPosition;
            lab.QueueFree(); await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        float poseSpread = 0;
        for (int i = 0; i < finalPositions.Length; i++) for (int j = i + 1; j < finalPositions.Length; j++)
            poseSpread = Mathf.Max(poseSpread, finalPositions[i].DistanceTo(finalPositions[j]));
        checks.Add(new StartupCheck("repeat_settle_within_bound", maxSettle <= bounds.MaximumSettleTicks, $"max={maxSettle} bound={bounds.MaximumSettleTicks}"));
        checks.Add(new StartupCheck("repeat_settle_spread", maxSettle - minSettle <= bounds.MaximumSettleTickSpread, $"spread={maxSettle-minSettle:F0}"));
        checks.Add(new StartupCheck("repeat_pose_spread", poseSpread <= bounds.MaximumFinalPoseSpread, $"spread={poseSpread:F3}"));
        checks.Add(new StartupCheck("repeat_strain_bound", maxStrain <= bounds.MaximumLinkStrain, $"max={maxStrain:F4}"));
        bool passed = true; foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
