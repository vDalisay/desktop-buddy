using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Owner-feedback B2 gate: the room owner injects its walkable bounds into autonomy,
/// and ambient goals at either margin include only idle or motion away from the wall.
/// </summary>
public sealed class AutonomyWallScenario : IScenario
{
    private const int ObservationTicksPerWall = 600;

    public string Id => "autonomy_respects_walls";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("wall_autonomy_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        AutonomousMotionProfile shipped = lab.Buddy.AutonomousMotion.Profile;
        lab.Buddy.AutonomousMotion.Profile = new AutonomousMotionProfile
        {
            ResourceName = "ScenarioFastWallChoices",
            MinimumIdleTicks = 12,
            MaximumIdleTicks = 24,
            MinimumWalkTicks = 12,
            MaximumWalkTicks = 24,
            MinimumJumpIntervalTicks = shipped.MinimumJumpIntervalTicks,
            MaximumJumpIntervalTicks = shipped.MaximumJumpIntervalTicks,
            IdleWeight = shipped.IdleWeight,
            WalkLeftWeight = shipped.WalkLeftWeight,
            WalkRightWeight = shipped.WalkRightWeight,
            WallAvoidMarginPixels = shipped.WallAvoidMarginPixels,
            WallLookAheadSeconds = shipped.WallLookAheadSeconds,
            AmbientJumpsEnabled = false,
        };
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);
        bool standing = await ScenarioSteps.WaitForStanding(tree, lab, 1800);

        EdgeObservation left = await ObserveEdge(tree, lab, left: true);
        lab.Controls.Reseed(seed ^ 0xA11CEUL);
        EdgeObservation right = await ObserveEdge(tree, lab, left: false);
        ApproachObservation leftApproach = await ApproachWall(tree, lab, left: true);
        ApproachObservation rightApproach = await ApproachWall(tree, lab, left: false);

        bool noIntoWall = left.IntoWallTicks == 0 && right.IntoWallTicks == 0;
        bool choicesVary = left.SawIdle && left.SawAwayWalk &&
            right.SawIdle && right.SawAwayWalk;
        checks.Add(new StartupCheck("autonomy_never_walks_into_blocked_wall",
            standing && noIntoWall,
            $"standing={standing} left={left.IntoWallTicks}/{left.Samples} " +
            $"right={right.IntoWallTicks}/{right.Samples}"));
        checks.Add(new StartupCheck("autonomy_wall_choices_still_vary",
            choicesVary,
            $"left_idle={left.SawIdle} left_away={left.SawAwayWalk} " +
            $"right_idle={right.SawIdle} right_away={right.SawAwayWalk}"));
        bool stoppedShort = leftApproach.StoppingObserved && rightApproach.StoppingObserved &&
            leftApproach.MinimumClearance > 0.5f && rightApproach.MinimumClearance > 0.5f;
        checks.Add(new StartupCheck("forward_wall_sensor_stops_before_contact", stoppedShort,
            $"left={leftApproach} right={rightApproach}"));
        messages.Add($"wall_observation left={left} right={right} " +
            $"left_approach={leftApproach} right_approach={rightApproach}");

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<ApproachObservation> ApproachWall(
        SceneTree tree, BuddyLab lab, bool left)
    {
        AutonomousMotionProfile profile = lab.Buddy.AutonomousMotion.Profile;
        profile.IdleWeight = 0;
        profile.WalkLeftWeight = left ? 100 : 0;
        profile.WalkRightWeight = left ? 0 : 100;
        lab.Buddy.ReseedAutonomy(left ? 0x1E17UL : 0xA11FUL);
        HoldRigAtClearance(lab, 70.0f, left);
        float velocity = left ? -55.0f : 55.0f;
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
            part.LinearVelocity = new Vector2(velocity, part.LinearVelocity.Y);

        bool stoppingObserved = false;
        float minimumClearance = float.PositiveInfinity;
        for (int tick = 0; tick < 180; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            stoppingObserved |= lab.Buddy.AutonomousMotion.IsWallStopping;
            float clearance = left
                ? lab.Buddy.AutonomousMotion.LeftWallClearance
                : lab.Buddy.AutonomousMotion.RightWallClearance;
            minimumClearance = Mathf.Min(minimumClearance, clearance);
        }
        return new ApproachObservation(stoppingObserved, minimumClearance);
    }

    private static async Task<EdgeObservation> ObserveEdge(
        SceneTree tree, BuddyLab lab, bool left)
    {
        float margin = lab.Buddy.AutonomousMotion.Profile.WallAvoidMarginPixels;
        int intoWallTicks = 0;
        int samples = 0;
        bool sawIdle = false;
        bool sawAway = false;

        for (int tick = 0; tick < ObservationTicksPerWall; tick++)
        {
            HoldRigAtClearance(lab, margin - 1.0f, left);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            float direction = lab.Buddy.AutonomousMotion.Intent.WalkDirection;
            samples++;
            intoWallTicks += left ? (direction < 0.0f ? 1 : 0) : (direction > 0.0f ? 1 : 0);
            sawIdle |= direction == 0.0f;
            sawAway |= left ? direction > 0.0f : direction < 0.0f;
        }

        return new EdgeObservation(intoWallTicks, samples, sawIdle, sawAway);
    }

    private static void HoldRigAtClearance(BuddyLab lab, float targetClearance, bool left)
    {
        float bodyEdge = left ? float.PositiveInfinity : float.NegativeInfinity;
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
        {
            bodyEdge = left
                ? Mathf.Min(bodyEdge, part.GlobalPosition.X - part.Radius)
                : Mathf.Max(bodyEdge, part.GlobalPosition.X + part.Radius);
        }
        float targetEdge = left
            ? lab.Boundaries.InnerBounds.Position.X + targetClearance
            : lab.Boundaries.InnerBounds.End.X - targetClearance;
        float offset = targetEdge - bodyEdge;
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
        {
            part.GlobalPosition += new Vector2(offset, 0.0f);
            part.LinearVelocity = new Vector2(0.0f, part.LinearVelocity.Y);
        }
    }

    private readonly record struct EdgeObservation(
        int IntoWallTicks,
        int Samples,
        bool SawIdle,
        bool SawAwayWalk);

    private readonly record struct ApproachObservation(
        bool StoppingObserved,
        float MinimumClearance);
}
