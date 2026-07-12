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
        bool allRunsSettled = true;
        bool allContainedFinite = true;
        int firstUnsettledRun = -1;
        int firstEscapedRun = -1;
        // Runs 0-4 share `seed`; runs 5-9 each use a distinct neighbouring seed.
        // Final pose is sampled after a seeded autonomous drive window (not at the
        // first settle tick) so the envelope measures driven repeatability, not the
        // trivial spawn-and-settle pose that is nearly seed-invariant.
        var finalPositions = new Vector2[10];
        for (int run = 0; run < 10; run++)
        {
            ulong runSeed = run < 5 ? seed : seed + (ulong)(run - 4);
            BuddyLab lab = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn").Instantiate<BuddyLab>();
            tree.Root.AddChild(lab); await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            lab.Controls.Reseed(runSeed);
            int settled = bounds.MaximumSettleTicks;
            bool runSettled = false;
            for (int tick = 0; tick < bounds.MaximumSettleTicks; tick++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
                foreach (LinkTelemetry link in lab.Buddy.Constraints.Telemetry) maxStrain = Mathf.Max(maxStrain, link.Strain);
                if (lab.Buddy.Standing.Snapshot.IsStable) { settled = tick + 1; runSettled = true; break; }
            }
            allRunsSettled &= runSettled;
            if (!runSettled)
            {
                if (firstUnsettledRun < 0) firstUnsettledRun = run;
                messages.Add($"run={run} seed={runSeed} did_not_settle");
            }
            else
            {
                minSettle = Mathf.Min(minSettle, settled); maxSettle = Mathf.Max(maxSettle, settled);
            }

            // Drive the seeded autonomous motion so the recorded outcome reflects it.
            bool runContainedFinite = true;
            for (int tick = 0; tick < bounds.AutonomyObservationTicks; tick++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
                foreach (LinkTelemetry link in lab.Buddy.Constraints.Telemetry) maxStrain = Mathf.Max(maxStrain, link.Strain);
                runContainedFinite &= lab.Buddy.Rig.AllBodiesFinite();
            }
            runContainedFinite &= lab.Buddy.Recovery.AllBodiesInsideSafeBounds();
            if (!runContainedFinite && firstEscapedRun < 0) firstEscapedRun = run;
            allContainedFinite &= runContainedFinite;

            finalPositions[run] = lab.Buddy.Rig.Torso.GlobalPosition;
            messages.Add($"run={run} seed={runSeed} settle={settled} final=({finalPositions[run].X:F1},{finalPositions[run].Y:F1})");
            lab.QueueFree(); await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        float allSpread = MaxPairwiseSpread(finalPositions, 0, 10);
        float sameSeedSpread = MaxPairwiseSpread(finalPositions, 0, 5);
        checks.Add(new StartupCheck("repeat_all_runs_settled", allRunsSettled, allRunsSettled ? "all 10 runs settled" : $"first_unsettled_run={firstUnsettledRun}"));
        checks.Add(new StartupCheck("repeat_settle_within_bound", allRunsSettled && maxSettle <= bounds.MaximumSettleTicks, $"max={maxSettle} bound={bounds.MaximumSettleTicks}"));
        float settleSpread = allRunsSettled ? maxSettle - minSettle : float.PositiveInfinity;
        checks.Add(new StartupCheck("repeat_settle_spread", settleSpread <= bounds.MaximumSettleTickSpread, $"spread={settleSpread:F0}"));
        checks.Add(new StartupCheck("repeat_contained_finite", allContainedFinite, allContainedFinite ? "all runs finite+contained through autonomy" : $"first_escaped_run={firstEscapedRun}"));
        checks.Add(new StartupCheck("repeat_same_seed_pose_spread", sameSeedSpread <= bounds.MaximumSameSeedPoseSpread, $"spread={sameSeedSpread:F3} bound={bounds.MaximumSameSeedPoseSpread:F1}"));
        checks.Add(new StartupCheck("repeat_pose_spread", allSpread <= bounds.MaximumFinalPoseSpread, $"spread={allSpread:F3} bound={bounds.MaximumFinalPoseSpread:F1}"));
        checks.Add(new StartupCheck("repeat_strain_bound", maxStrain <= bounds.MaximumLinkStrain, $"max={maxStrain:F4}"));
        bool passed = true; foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static float MaxPairwiseSpread(Vector2[] positions, int start, int end)
    {
        float spread = 0;
        for (int i = start; i < end; i++)
            for (int j = i + 1; j < end; j++)
                spread = Mathf.Max(spread, positions[i].DistanceTo(positions[j]));
        return spread;
    }
}
