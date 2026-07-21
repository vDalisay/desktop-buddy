using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M3.6 Task 3 gate: every activity resolves to a real clip in the animation library
/// (`activity_clip_mapping`), walk-dressing phase advances proportionally to MEASURED
/// travel and freezes outside the walk (`walk_cycle_speed_match`), and the eat clip is
/// item-agnostic — two different item visuals ride the same hand ItemSocket through the
/// same clip (`eat_clip_item_agnostic`). Semantic assertions only, never pixels.
/// </summary>
public sealed class ActivityClipsScenario : IScenario
{
    private const double FixedDelta = 1.0 / 120.0;

    public string Id => "activity_clips";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("activity_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);

        checks.Add(CheckClipMapping(lab, messages));
        checks.Add(await CheckEatFacesFood(tree, lab, messages));
        checks.Add(await CheckWalkCycleSpeedMatch(tree, lab, messages));
        checks.Add(await CheckEatClipItemAgnostic(tree, lab, messages));
        checks.Add(await CheckEatFiveBiteSequence(tree, lab, messages));
        checks.Add(await CheckEatReachesAndStands(tree, lab, messages));
        checks.Add(await CheckImpactInterruptsEatReach(tree, lab, messages));

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<StartupCheck> CheckEatFiveBiteSequence(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        ActivityAnimator animator = lab.Activities;
        animator.AttachItemVisual(new MeshInstance3D
        {
            Name = "FiveBiteItem",
            Mesh = new SphereMesh { Radius = 3.0f, Height = 6.0f },
        });
        var scales = new List<float>();
        Action<int, int> onBite = (_, _) => scales.Add(animator.ItemSocket.Scale.X);
        lab.Buddy.Activity.EatBiteCompleted += onBite;
        lab.Buddy.SetBehaviorActivity(ActivityId.Eat);

        bool sawChest = false;
        bool sawMouth = false;
        bool handsStayedInFront = true;
        bool mouthClearanceValid = true;
        bool initialHeadShareObserved = false;
        bool finalLowerCommanded = false;
        bool finalLowerPhysicallyReached = false;
        int totalTicks = lab.Buddy.Activity.Profile.EatChestHoldTicks +
            (lab.Buddy.Activity.Profile.EatBiteCount * lab.Buddy.Activity.Profile.EatBiteCycleTicks) +
            lab.Buddy.Activity.Profile.EatFinalLowerHoldTicks;
        for (int tick = 0; tick < totalTicks + 2; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            sawChest |= lab.Buddy.Activity.EatLift < 0.05f;
            sawMouth |= lab.Buddy.Activity.EatLift > 0.95f;
            Node3D torsoSocket = lab.VisualPresenter.GetPartSocket(BuddyPartId.Torso);
            Node3D leftSocket = lab.VisualPresenter.GetPartSocket(BuddyPartId.LeftHand);
            Node3D rightSocket = lab.VisualPresenter.GetPartSocket(BuddyPartId.RightHand);
            if (lab.Buddy.Activity.Current == ActivityId.Eat)
            {
                float faceDepth = lab.VisualPresenter.FacePlate?.GlobalPosition.Z ?? float.PositiveInfinity;
                handsStayedInFront &= leftSocket.GlobalPosition.Z > faceDepth &&
                    rightSocket.GlobalPosition.Z > faceDepth;
                if (lab.Buddy.Activity.EatLift > 0.95f)
                {
                    // Includes the chew mouth's lower stroke and peak visual head bob.
                    const float MouthBottomScreenOffsetY = 13.0f;
                    float mouthY = lab.Buddy.Rig.Head.GlobalPosition.Y + MouthBottomScreenOffsetY;
                    float leftGap = (lab.Buddy.Rig.LeftHand.GlobalPosition.Y -
                        lab.Buddy.Rig.LeftHand.Radius) - mouthY;
                    float rightGap = (lab.Buddy.Rig.RightHand.GlobalPosition.Y -
                        lab.Buddy.Rig.RightHand.Radius) - mouthY;
                    mouthClearanceValid &= leftGap >= -1.0f && leftGap <= 8.0f &&
                        rightGap >= -1.0f && rightGap <= 8.0f;
                }
            }
            if (lab.Buddy.Activity.EatLift < 0.05f &&
                lab.Buddy.ActiveDrive.LastRightHandReachReactionForce.Length() > 0.01f)
            {
                float share = lab.Buddy.ActiveDrive.LastActivityHeadReactionForce.Length() /
                    lab.Buddy.ActiveDrive.LastRightHandReachReactionForce.Length();
                initialHeadShareObserved |= Mathf.Abs(share - 0.25f) < 0.01f;
            }
            if (lab.Buddy.Activity.EatFinalLowering > 0.95f)
            {
                float actualCenterY = (lab.Buddy.CurrentDriveIntent.LeftActivityHandTarget.Y +
                    lab.Buddy.CurrentDriveIntent.RightActivityHandTarget.Y) * 0.5f;
                float expectedCenterY = lab.Buddy.Rig.Torso.GlobalPosition.Y +
                    lab.Buddy.ActiveDrive.Profile.EatFinalLowerTargetOffset.Y;
                finalLowerCommanded |= Mathf.Abs(actualCenterY - expectedCenterY) < 1.0f;
                float physicalCenterY = (lab.Buddy.Rig.LeftHand.GlobalPosition.Y +
                    lab.Buddy.Rig.RightHand.GlobalPosition.Y) * 0.5f;
                finalLowerPhysicallyReached |= Mathf.Abs(physicalCenterY - expectedCenterY) < 4.0f;
            }
        }
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Buddy.Activity.EatBiteCompleted -= onBite;

        bool exactScales = scales.Count == 5;
        for (int index = 0; index < scales.Count; index++)
            exactScales &= Mathf.Abs(scales[index] - ((4 - index) / 5.0f)) < 0.01f;
        bool completed = lab.Buddy.Activity.Current == ActivityId.None &&
            animator.ItemSocket.GetChildCount() == 0;
        bool passed = sawChest && sawMouth && handsStayedInFront && mouthClearanceValid &&
            initialHeadShareObserved && finalLowerCommanded && finalLowerPhysicallyReached &&
            exactScales && completed;
        messages.Add($"eat_five_bites count={scales.Count} scales={string.Join(',', scales)} " +
            $"chest={sawChest} mouth={sawMouth} front={handsStayedInFront} " +
            $"mouth_clear={mouthClearanceValid} " +
            $"half_head={initialHeadShareObserved} final_lower_cmd={finalLowerCommanded} " +
            $"final_lower_body={finalLowerPhysicallyReached} completed={completed}");
        return new StartupCheck("eat_exact_five_bites", passed,
            $"count={scales.Count} chest={sawChest} mouth={sawMouth} front={handsStayedInFront} " +
            $"mouth_clear={mouthClearanceValid} " +
            $"half_head={initialHeadShareObserved} final_lower_cmd={finalLowerCommanded} " +
            $"final_lower_body={finalLowerPhysicallyReached} " +
            $"scales={string.Join(',', scales)} completed={completed}");
    }

    private static async Task<StartupCheck> CheckEatFacesFood(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        lab.Facing.SetDevelopmentSide(1);
        for (int frame = 0; frame < 90; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool startedSideways = lab.Facing.CommittedSide == FacingSide.Right &&
            lab.Facing.CurrentYawDegrees > 25.0f;

        lab.Buddy.SetBehaviorActivity(ActivityId.Eat);
        for (int frame = 0; frame < 90; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool frontalDuringEat = Mathf.Abs(lab.Facing.CurrentYawDegrees) < 0.5f;
        bool sideRemembered = lab.Facing.CommittedSide == FacingSide.Right;

        lab.Buddy.SetBehaviorActivity(ActivityId.None);
        for (int frame = 0; frame < 90; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool restoredAfterEat = lab.Facing.CurrentYawDegrees > 25.0f;
        lab.Facing.SetDevelopmentSide(0);

        bool passed = startedSideways && frontalDuringEat && sideRemembered && restoredAfterEat;
        messages.Add($"eat_facing side_start={startedSideways} frontal={frontalDuringEat} " +
            $"remembered={sideRemembered} restored={restoredAfterEat}");
        return new StartupCheck("eat_faces_food_frontal", passed,
            $"side_start={startedSideways} frontal={frontalDuringEat} " +
            $"remembered={sideRemembered} restored={restoredAfterEat}");
    }

    private static StartupCheck CheckClipMapping(BuddyLab lab, List<string> messages)
    {
        ActivityAnimator animator = lab.Activities;
        var names = new List<string>();
        bool passed = true;
        foreach (ActivityId activity in new[]
        {
            ActivityId.IdleBreathe, ActivityId.WalkCycle, ActivityId.JumpAnticipation,
            ActivityId.Wave, ActivityId.Eat,
        })
        {
            string name = ActivityAnimator.ClipNameFor(activity);
            bool resolved = !string.IsNullOrEmpty(name) && animator.HasClip(activity);
            passed &= resolved;
            names.Add($"{activity}:{name}:{resolved}");
        }

        messages.Add($"clip_mapping {string.Join(" ", names)}");
        return new StartupCheck("activity_clip_mapping", passed, string.Join(",", names));
    }

    private static async Task<StartupCheck> CheckWalkCycleSpeedMatch(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        ActivityAnimator animator = lab.Activities;
        float pixelsPerCycle = animator.Profile.ActivityWalkCyclePixels;
        await ScenarioSteps.WaitForStanding(tree, lab, 1800);

        // Accumulate measured travel and unwrapped phase over real walk-dressing frames;
        // outside those frames the phase must not move at all.
        double expectedCycles = 0.0;
        double actualCycles = 0.0;
        bool frozenOutsideWalk = true;
        float previousPhase = animator.WalkPhase;
        int walkFrames = 0;
        for (int frame = 0; frame < 14400 && expectedCycles < 0.75; frame++)
        {
            float speedBefore = Mathf.Abs(lab.Buddy.Rig.Torso.LinearVelocity.X);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            float phase = animator.WalkPhase;
            float delta = phase - previousPhase;
            if (delta < -0.5f)
            {
                delta += 1.0f;
            }

            if (animator.Current == ActivityId.WalkCycle)
            {
                walkFrames++;
                expectedCycles += speedBefore * FixedDelta / pixelsPerCycle;
                actualCycles += delta;
            }
            else
            {
                frozenOutsideWalk &= Mathf.Abs(delta) < 0.000001f;
            }

            previousPhase = phase;
        }

        bool enoughTravel = expectedCycles >= 0.25;
        double ratio = enoughTravel ? actualCycles / expectedCycles : 0.0;
        bool proportional = enoughTravel && ratio > 0.85 && ratio < 1.15;

        bool passed = enoughTravel && proportional && frozenOutsideWalk;
        messages.Add($"walk_cycle frames={walkFrames} expected={expectedCycles:F3} " +
            $"actual={actualCycles:F3} ratio={ratio:F3} frozen={frozenOutsideWalk}");
        return new StartupCheck("walk_cycle_speed_match", passed,
            $"walk_frames={walkFrames} expected_cycles={expectedCycles:F3} " +
            $"actual_cycles={actualCycles:F3} ratio={ratio:F3} frozen_outside={frozenOutsideWalk}");
    }

    private static async Task<StartupCheck> CheckEatClipItemAgnostic(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        ActivityAnimator animator = lab.Activities;

        (bool ate, bool rode, string clip) first = await EatWithItem(
            tree, lab, new SphereMesh { Radius = 3.0f, Height = 6.0f });
        (bool ate, bool rode, string clip) second = await EatWithItem(
            tree, lab, new BoxMesh { Size = new Vector3(5.0f, 5.0f, 5.0f) });
        animator.ClearItemVisual();

        bool sameClip = first.clip == "eat" && second.clip == "eat";
        bool passed = first.ate && first.rode && second.ate && second.rode && sameClip;
        messages.Add($"eat_item_agnostic sphere=({first.ate},{first.rode}) " +
            $"box=({second.ate},{second.rode}) clip={first.clip}/{second.clip}");
        return new StartupCheck("eat_clip_item_agnostic", passed,
            $"sphere_ate={first.ate} sphere_rode={first.rode} box_ate={second.ate} " +
            $"box_rode={second.rode} same_clip={sameClip}");
    }

    private static async Task<(bool ate, bool rode, string clip)> EatWithItem(
        SceneTree tree, BuddyLab lab, Mesh mesh)
    {
        ActivityAnimator animator = lab.Activities;
        var visual = new MeshInstance3D { Name = "ItemVisual", Mesh = mesh };
        animator.AttachItemVisual(visual);
        lab.Buddy.SetBehaviorActivity(ActivityId.Eat);

        bool ate = false;
        bool rode = true;
        string clip = string.Empty;
        int validSamples = 0;
        Node3D leftHandSocket = lab.VisualPresenter.GetPartSocket(
            DesktopBuddy.Buddy.Physics.BuddyPartId.LeftHand);
        Node3D rightHandSocket = lab.VisualPresenter.GetPartSocket(
            DesktopBuddy.Buddy.Physics.BuddyPartId.RightHand);
        for (int frame = 0; frame < 600 && validSamples < 20; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (animator.Current != ActivityId.Eat)
            {
                continue;
            }

            validSamples++;
            ate = true;
            clip = animator.CurrentClipName;
            Vector3 handMidpoint = (leftHandSocket.GlobalPosition + rightHandSocket.GlobalPosition) * 0.5f;
            rode &= visual.GlobalPosition.DistanceTo(animator.ItemSocket.GlobalPosition) < 0.01f &&
                animator.ItemSocket.GlobalPosition.DistanceTo(handMidpoint) < 0.01f;
        }

        lab.Buddy.SetBehaviorActivity(ActivityId.None);
        return (ate && validSamples >= 20, rode, clip);
    }

    private static async Task<StartupCheck> CheckEatReachesAndStands(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        await ScenarioSteps.WaitForStanding(tree, lab, 1200);
        Vector2 torsoStart = lab.Buddy.Rig.Torso.GlobalPosition;
        lab.Buddy.SetBehaviorActivity(ActivityId.Eat);

        float maximumTorsoTravel = 0.0f;
        float minimumLeftTargetDistance = float.PositiveInfinity;
        float minimumRightTargetDistance = float.PositiveInfinity;
        float maximumReachForce = 0.0f;
        bool walkHeld = true;
        for (int tick = 0; tick < 180; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            minimumLeftTargetDistance = Mathf.Min(minimumLeftTargetDistance,
                lab.Buddy.Rig.LeftHand.GlobalPosition.DistanceTo(
                    lab.Buddy.CurrentDriveIntent.LeftActivityHandTarget));
            minimumRightTargetDistance = Mathf.Min(minimumRightTargetDistance,
                lab.Buddy.Rig.RightHand.GlobalPosition.DistanceTo(
                    lab.Buddy.CurrentDriveIntent.RightActivityHandTarget));
            maximumTorsoTravel = Mathf.Max(maximumTorsoTravel,
                Mathf.Abs(lab.Buddy.Rig.Torso.GlobalPosition.X - torsoStart.X));
            maximumReachForce = Mathf.Max(
                maximumReachForce,
                lab.Buddy.ActiveDrive.LastRightHandReachForce.Length());
            walkHeld &= Mathf.IsZeroApprox(lab.Buddy.CurrentDriveIntent.WalkDirection);
        }

        lab.Buddy.SetBehaviorActivity(ActivityId.None);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool released = !lab.Buddy.CurrentDriveIntent.ActivityHandReachActive &&
            lab.Buddy.ActiveDrive.LastRightHandReachForce.IsZeroApprox();
        bool passed = walkHeld && maximumTorsoTravel < 20.0f &&
            minimumLeftTargetDistance < 18.0f && minimumRightTargetDistance < 18.0f &&
            maximumReachForce > 0.0f && released;
        messages.Add($"eat_physics walk_held={walkHeld} torso_travel={maximumTorsoTravel:F1} " +
            $"hand_targets={minimumLeftTargetDistance:F1}/{minimumRightTargetDistance:F1} " +
            $"peak_force={maximumReachForce:F0} released={released}");
        return new StartupCheck("eat_reaches_and_stands", passed,
            $"walk_held={walkHeld} torso_travel={maximumTorsoTravel:F1} " +
            $"hand_targets={minimumLeftTargetDistance:F1}/{minimumRightTargetDistance:F1} " +
            $"peak_force={maximumReachForce:F0} released={released}");
    }

    private static async Task<StartupCheck> CheckImpactInterruptsEatReach(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        await ScenarioSteps.WaitForStanding(tree, lab, 1200);
        lab.Buddy.SetBehaviorActivity(ActivityId.Eat);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool reachedBefore = lab.Buddy.CurrentDriveIntent.ActivityHandReachActive;
        DesktopBuddy.Interaction.AcceptedImpact? impact = await ScenarioSteps.StrikePart(
            tree,
            lab,
            lab.Buddy.Rig.Head,
            (int)DesktopBuddy.Domain.Tools.ToolId.BoxingGlove,
            730_002);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool cut = lab.Buddy.Activity.Current == ActivityId.None &&
            !lab.Buddy.CurrentDriveIntent.ActivityHandReachActive &&
            lab.Buddy.ActiveDrive.LastRightHandReachForce.IsZeroApprox();
        messages.Add($"eat_impact_cut before={reachedBefore} impact={impact is not null} cut={cut}");
        return new StartupCheck("eat_reach_cuts_on_impact",
            reachedBefore && impact is not null && cut,
            $"before={reachedBefore} impact={impact is not null} cut={cut}");
    }
}
