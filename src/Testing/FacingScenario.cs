using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M3.6 Task 2 gate (`facing_follows_walk`): sustained seeded walking commits the
/// matching three-quarter side after the hysteresis streak and eases to the accepted
/// yaw without overshoot; an engaged care cursor flips the side deterministically with
/// a monotonic zero-crossing turn; and a Tracking cut snaps the DISPLAYED yaw to zero
/// while the committed side is remembered. All assertions are semantic (side, model
/// yaw, applied yaw) — never pixels.
/// </summary>
public sealed class FacingScenario : IScenario
{
    private const int WalkObservationBudgetTicks = 14400;

    public string Id => "facing_follows_walk";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("facing_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);

        checks.Add(await CheckWalkCommitsMatchingSide(tree, lab, messages));
        checks.Add(await CheckInteractionBiasFlipsSide(tree, lab, messages));
        checks.Add(await CheckTrackingSnapsDisplayedYaw(tree, lab, messages));

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<StartupCheck> CheckWalkCommitsMatchingSide(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        FacingController facing = lab.Facing;
        float yawTarget = facing.Profile.FacingYawDegrees;
        int commitTicks = facing.Profile.FacingWalkCommitTicks;
        float deadband = facing.Profile.FacingWalkDeadband;

        await ScenarioSteps.WaitForStanding(tree, lab, 1800);

        // Track the walk-direction streak ourselves from the same arbitrated intent the
        // controller reads; when the streak crosses the hysteresis bound, the model must
        // have committed the matching side, and the eased yaw must reach the accepted
        // magnitude with the matching sign without ever overshooting it.
        int streakTicks = 0;
        float streakSign = 0.0f;
        bool commitObserved = false;
        bool commitMatchesSign = false;
        bool reachedTarget = false;
        bool neverOvershot = true;
        float committedSign = 0.0f;
        for (int tick = 0; tick < WalkObservationBudgetTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            neverOvershot &= Mathf.Abs(facing.CurrentYawDegrees) <= yawTarget + 0.001f;

            float walk = lab.Buddy.CurrentDriveIntent.WalkDirection;
            if (Mathf.Abs(walk) > deadband)
            {
                float sign = Mathf.Sign(walk);
                if (sign == streakSign)
                {
                    streakTicks++;
                }
                else
                {
                    streakSign = sign;
                    streakTicks = 1;
                }
            }
            else
            {
                streakTicks = 0;
                streakSign = 0.0f;
            }

            if (!commitObserved && streakTicks >= commitTicks)
            {
                commitObserved = true;
                committedSign = streakSign;
                commitMatchesSign =
                    (streakSign > 0.0f && facing.CommittedSide == FacingSide.Right) ||
                    (streakSign < 0.0f && facing.CommittedSide == FacingSide.Left);
            }

            if (commitObserved && !reachedTarget)
            {
                // The side may legitimately re-commit later; accept reaching either
                // committed target as long as sign and magnitude agree with the model.
                float yaw = facing.CurrentYawDegrees;
                float expectedSign = facing.CommittedSide == FacingSide.Right ? 1.0f : -1.0f;
                reachedTarget = facing.CommittedSide != FacingSide.Frontal &&
                    Mathf.Abs(yaw) >= yawTarget - 0.5f &&
                    Mathf.Sign(yaw) == expectedSign;
            }

            if (commitObserved && reachedTarget)
            {
                break;
            }
        }

        bool passed = commitObserved && commitMatchesSign && reachedTarget && neverOvershot;
        messages.Add($"walk_commit sign={committedSign} side={facing.CommittedSide} " +
            $"yaw={facing.CurrentYawDegrees:F2}");
        return new StartupCheck("facing_commits_on_sustained_walk", passed,
            $"commit_observed={commitObserved} sign_matches={commitMatchesSign} " +
            $"reached_target={reachedTarget} never_overshot={neverOvershot} " +
            $"side={facing.CommittedSide} yaw={facing.CurrentYawDegrees:F2}");
    }

    private static async Task<StartupCheck> CheckInteractionBiasFlipsSide(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        FacingController facing = lab.Facing;
        float yawTarget = facing.Profile.FacingYawDegrees;
        lab.Pipeline.SelectTool(ToolId.Pet);

        // Engaged care cursor to the buddy's right: the side commits immediately
        // regardless of what autonomy is doing (interaction outranks walk).
        bool rightCommitted = false;
        for (int tick = 0; tick < 240 && !rightCommitted; tick++)
        {
            lab.CareStroke.SetStroke(
                true, lab.Buddy.Rig.Head.GlobalPosition + new Vector2(10.0f, 0.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            rightCommitted = facing.CommittedSide == FacingSide.Right;
        }

        // Let the turn settle on the right target so the flip must cross zero.
        for (int tick = 0; tick < 120 &&
            Mathf.Abs(facing.CurrentYawDegrees - yawTarget) > 0.5f; tick++)
        {
            lab.CareStroke.SetStroke(
                true, lab.Buddy.Rig.Head.GlobalPosition + new Vector2(10.0f, 0.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        // Flip the cursor to the left side and watch the whole eased turn: monotonic
        // (never increasing), crosses zero exactly once, never overshoots either target.
        bool leftCommitted = false;
        bool crossedZero = false;
        bool monotonic = true;
        bool neverOvershot = true;
        float previous = facing.CurrentYawDegrees;
        for (int tick = 0; tick < 240; tick++)
        {
            lab.CareStroke.SetStroke(
                true, lab.Buddy.Rig.Head.GlobalPosition + new Vector2(-10.0f, 0.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            float yaw = facing.CurrentYawDegrees;
            leftCommitted |= facing.CommittedSide == FacingSide.Left;
            monotonic &= yaw <= previous + 0.001f;
            neverOvershot &= Mathf.Abs(yaw) <= yawTarget + 0.001f;
            crossedZero |= yaw < 0.0f && previous >= 0.0f;
            previous = yaw;
            if (yaw <= -(yawTarget - 0.5f))
            {
                break;
            }
        }

        lab.CareStroke.SetStroke(false, Vector2.Zero);
        lab.Pipeline.SelectTool(ToolId.Grab);

        bool passed = rightCommitted && leftCommitted && crossedZero &&
            monotonic && neverOvershot;
        messages.Add($"interaction_flip right={rightCommitted} left={leftCommitted} " +
            $"crossed_zero={crossedZero} yaw={facing.CurrentYawDegrees:F2}");
        return new StartupCheck("facing_interaction_bias_flips_side", passed,
            $"right={rightCommitted} left={leftCommitted} crossed_zero={crossedZero} " +
            $"monotonic={monotonic} never_overshot={neverOvershot}");
    }

    private static async Task<StartupCheck> CheckTrackingSnapsDisplayedYaw(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        FacingController facing = lab.Facing;
        FacingSide sideBefore = facing.CommittedSide;

        // A controlled strike forces Tracking through the real impact cooldown: the
        // DISPLAYED yaw (facing scaled by the blend weight) must snap to zero on the
        // next rendered frame while the committed side survives the ragdoll cut.
        AcceptedImpact? impact =
            await ScenarioSteps.StrikePartAtSpeed(tree, lab, lab.Buddy.Rig.Torso, 2000.0f);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool tracking = lab.PosePipeline.Mode == PresentationPoseMode.Tracking;
        bool displayedSnapped = Mathf.Abs(lab.VisualPresenter.AppliedYawDegrees) < 0.001f;
        bool sideRemembered = facing.CommittedSide == sideBefore &&
            sideBefore != FacingSide.Frontal;

        bool passed = impact is not null && tracking && displayedSnapped && sideRemembered;
        messages.Add($"tracking_snap displayed={lab.VisualPresenter.AppliedYawDegrees:F4} " +
            $"side={facing.CommittedSide}");
        return new StartupCheck("facing_tracking_snaps_displayed_yaw", passed,
            $"impact={impact is not null} tracking={tracking} " +
            $"displayed_snapped={displayedSnapped} side_remembered={sideRemembered}");
    }
}
