using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Steam Demo DEMO-4 gate for the shared pullback predictor. The math test is deliberately
/// collision-free: the guide promises the authored ballistic horizon, not a collision-aware
/// landing solver. The lab check proves Baseball and Grenade both use the same launcher seam.
/// </summary>
public sealed class BallisticTrajectoryPredictionScenario : IScenario
{
    public string Id => "ballistic_trajectory_prediction";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        var input = new BallisticTrajectoryPredictor.Input(
            new Vector2(50.0f, 120.0f),
            new Vector2(900.0f, -650.0f),
            980.0f,
            0.8f,
            1.0f / 120.0f);
        Vector2 baseline = BallisticTrajectoryPredictor.Predict(input, 1.0f);
        Vector2 repeated = BallisticTrajectoryPredictor.Predict(input, 1.0f);
        checks.Add(new StartupCheck(
            "ballistic_prediction_is_deterministic",
            baseline.IsEqualApprox(repeated),
            $"first={baseline} repeat={repeated}"));

        var sparse = new Vector2[6];
        var dense = new Vector2[24];
        BallisticTrajectoryPredictor.Sample(input, 1.0f, sparse);
        BallisticTrajectoryPredictor.Sample(input, 1.0f, dense);
        checks.Add(new StartupCheck(
            "guide_density_does_not_change_landing_prediction",
            sparse[^1].DistanceTo(dense[^1]) < 0.001f &&
            dense[^1].DistanceTo(baseline) < 0.001f,
            $"sparse={sparse[^1]} dense={dense[^1]} baseline={baseline}"));

        var strongerGravity = input with { Gravity = 1_400.0f };
        Vector2 gravityResult = BallisticTrajectoryPredictor.Predict(strongerGravity, 1.0f);
        checks.Add(new StartupCheck(
            "changed_gravity_moves_prediction_downward",
            gravityResult.Y > baseline.Y + 50.0f,
            $"normal_y={baseline.Y:F2} strong_y={gravityResult.Y:F2}"));

        var strongerDamp = input with { LinearDamp = 3.0f };
        Vector2 dampResult = BallisticTrajectoryPredictor.Predict(strongerDamp, 1.0f);
        checks.Add(new StartupCheck(
            "changed_object_damping_changes_prediction",
            MathF.Abs(dampResult.X - input.Start.X) < MathF.Abs(baseline.X - input.Start.X),
            $"normal_dx={baseline.X - input.Start.X:F2} damped_dx={dampResult.X - input.Start.X:F2}"));

        var strongerLaunch = input with { InitialVelocity = input.InitialVelocity * 1.35f };
        Vector2 launchResult = BallisticTrajectoryPredictor.Predict(strongerLaunch, 1.0f);
        checks.Add(new StartupCheck(
            "changed_pull_profile_velocity_changes_prediction",
            launchResult.X > baseline.X + 100.0f,
            $"normal_x={baseline.X:F2} strong_x={launchResult.X:F2}"));

        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(tree, 10.0f, 500.0f);
        if (lab is null)
        {
            checks.Add(new StartupCheck("shared_launcher_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        bool hasBaseball = false;
        bool hasGrenade = false;
        foreach (LooseObjectProfile profile in lab.Launcher.LaunchableProfiles)
        {
            if (!GodotObject.IsInstanceValid(profile))
                continue;
            hasBaseball |= profile.ContentId == ContentIds.ToolBaseball;
            hasGrenade |= profile.ContentId == ContentIds.ToolGrenade;
        }
        checks.Add(new StartupCheck(
            "baseball_and_grenade_share_pullback_launcher",
            hasBaseball && hasGrenade,
            $"baseball={hasBaseball} grenade={hasGrenade} profiles={lab.Launcher.LaunchableProfiles.Count}"));

        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new ScenarioResult(passed, checks, messages);
    }
}
