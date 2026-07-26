using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Seeded walk/jump actuation and consciousness-profile regression.</summary>
public sealed class AutonomousMotionScenario : IScenario
{
    private const int SettleTimeoutTicks = 720;
    // Wall-aware autonomy can legitimately spend several long calm goals idling at an
    // edge before the seeded draw selects the away direction. Keep the observation long
    // enough for both accepted seeds without weakening the two-direction requirement.
    private const int MotionObservationTicks = 3_600;

    public string Id => "autonomous_motion";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("autonomy_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();

        // The shipped profile has timer jumping OFF (owner decision 2026-07-20).
        // Task 3 exercises jump actuation through the approved obstacle+trait gate instead.
        AutonomousMotionProfile shipped = lab.Buddy.AutonomousMotion.Profile;
        bool shippedAmbientJumps = shipped.AmbientJumpsEnabled;
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Buddy.ReseedAutonomy(seed);
        lab.Progress.SeedTraits(new BuddyTraits(100));

        bool initiallyStanding = await WaitForStanding(tree, lab, SettleTimeoutTicks);
        checks.Add(new StartupCheck(
            "autonomy_starts_from_standing",
            initiallyStanding,
            $"stable_ticks={lab.Buddy.Standing.Snapshot.StableTicks}"));

        checks.Add(new StartupCheck(
            "shipped_profile_disables_ambient_jumping",
            !shippedAmbientJumps,
            $"ambient_jumps_enabled={shippedAmbientJumps}"));

        Vector2 start = lab.Buddy.Rig.Torso.GlobalPosition;
        float minimumTorsoY = start.Y;
        float maximumHorizontalDelta = 0.0f;
        bool sawLeftForce = false;
        bool sawRightForce = false;
        bool sawJumpImpulse = false;
        // Keep observing for an apex window after the first jump impulse: the impulse
        // becomes visible at takeoff, but the torso needs ~1 s of flight to reach its
        // rise apex. Breaking the instant the impulse appears samples the launch pose,
        // not the peak (this masked a real 8 px rise as 5.67 px once the standing-fix
        // reordered the seeded schedule to make the jump the last condition met).
        const int JumpApexWindowTicks = 150;
        int jumpTick = -1;
        // Gait: while walking, the feet must visibly step — alternate support and
        // lift clear of the floor — rather than slide flat (owner feel review).
        bool leftLifted = false, leftPlanted = false, rightLifted = false, rightPlanted = false;
        float leftMinY = float.PositiveInfinity, leftMaxY = float.NegativeInfinity;
        float rightMinY = float.PositiveInfinity, rightMaxY = float.NegativeInfinity;
        float previousWalkDirection = 0.0f;
        int idleStopTicks = -1;
        float idleStopOriginX = 0.0f;
        float idleStopMaximumTravel = 0.0f;
        float idleStopFinalSpeed = float.PositiveInfinity;
        bool idleStopObserved = false;
        bool idleStopBounded = false;
        LooseObjectBody? hopObstacle = null;
        for (int tick = 0; tick < MotionObservationTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            float locomotionX = lab.Buddy.ActiveDrive.LastLocomotionForce.X;
            sawLeftForce |= locomotionX < 0.0f;
            sawRightForce |= locomotionX > 0.0f;
            if (lab.Buddy.ActiveDrive.LastJumpImpulse > 0.0f && jumpTick < 0)
            {
                jumpTick = tick;
            }

            sawJumpImpulse |= lab.Buddy.ActiveDrive.LastJumpImpulse > 0.0f;
            Vector2 position = lab.Buddy.Rig.Torso.GlobalPosition;
            minimumTorsoY = Mathf.Min(minimumTorsoY, position.Y);
            maximumHorizontalDelta = Mathf.Max(maximumHorizontalDelta, Mathf.Abs(position.X - start.X));

            float walkDirection = lab.Buddy.AutonomousMotion.Intent.WalkDirection;
            bool grounded = lab.Buddy.Standing.Snapshot.SupportContactCount > 0;
            if (hopObstacle is null && jumpTick < 0 && grounded &&
                lab.Buddy.Standing.Snapshot.IsStable &&
                !Mathf.IsZeroApprox(walkDirection))
            {
                hopObstacle = lab.SpawnLooseObject(
                    lab.SafeObjectProfile,
                    lab.Buddy.Rig.Torso.GlobalPosition +
                        new Vector2(Mathf.Sign(walkDirection) * 50.0f, 0.0f));
                if (hopObstacle is not null)
                    hopObstacle.Freeze = true;
            }
            if (jumpTick >= 0 && GodotObject.IsInstanceValid(hopObstacle))
            {
                lab.Objects.Unregister(hopObstacle!);
                hopObstacle!.QueueFree();
                hopObstacle = null;
            }
            if (!idleStopObserved && idleStopTicks < 0 && !Mathf.IsZeroApprox(previousWalkDirection) &&
                Mathf.IsZeroApprox(walkDirection) && grounded)
            {
                idleStopObserved = true;
                idleStopTicks = 0;
                idleStopOriginX = MassCenterX(lab.Buddy.Rig);
            }
            if (idleStopTicks >= 0)
            {
                idleStopMaximumTravel = Mathf.Max(idleStopMaximumTravel,
                    Mathf.Abs(MassCenterX(lab.Buddy.Rig) - idleStopOriginX));
                idleStopFinalSpeed = Mathf.Abs(MassCenterVelocityX(lab.Buddy.Rig));
                if (++idleStopTicks >= 4)
                {
                    idleStopBounded = idleStopMaximumTravel <= 1.25f && idleStopFinalSpeed <= 2.0f;
                    idleStopTicks = -2;
                }
            }
            previousWalkDirection = walkDirection;

            if (walkDirection != 0.0f &&
                lab.Buddy.ActiveDrive.LastJumpImpulse <= 0.0f)
            {
                PuppetPartBody lf = lab.Buddy.Rig.LeftFoot, rf = lab.Buddy.Rig.RightFoot;
                leftLifted |= !lf.HasSupportContact; leftPlanted |= lf.HasSupportContact;
                rightLifted |= !rf.HasSupportContact; rightPlanted |= rf.HasSupportContact;
                leftMinY = Mathf.Min(leftMinY, lf.GlobalPosition.Y); leftMaxY = Mathf.Max(leftMaxY, lf.GlobalPosition.Y);
                rightMinY = Mathf.Min(rightMinY, rf.GlobalPosition.Y); rightMaxY = Mathf.Max(rightMaxY, rf.GlobalPosition.Y);
            }

            bool feetAlternate = leftLifted && leftPlanted && rightLifted && rightPlanted;
            bool jumpApexCaptured = jumpTick >= 0 && tick >= jumpTick + JumpApexWindowTicks;
            if (sawLeftForce && sawRightForce && feetAlternate && jumpApexCaptured &&
                maximumHorizontalDelta >= 8.0f && idleStopBounded)
            {
                break;
            }
        }

        bool feetAlternateSupport = leftLifted && leftPlanted && rightLifted && rightPlanted;
        float leftLift = leftMaxY - leftMinY, rightLift = rightMaxY - rightMinY;
        const float clearanceBound = 6.0f; // visible step lift (px), not a slide
        checks.Add(new StartupCheck(
            "seeded_autonomy_feet_alternate_support", feetAlternateSupport,
            $"L(lift={leftLifted},plant={leftPlanted}) R(lift={rightLifted},plant={rightPlanted})"));
        checks.Add(new StartupCheck(
            "seeded_autonomy_feet_step_clear", leftLift >= clearanceBound && rightLift >= clearanceBound,
            $"leftLift={leftLift:F1} rightLift={rightLift:F1} bound={clearanceBound:F1}"));

        checks.Add(new StartupCheck(
            "seeded_autonomy_walks_both_directions",
            sawLeftForce && sawRightForce,
            $"left={sawLeftForce} right={sawRightForce} max_dx={maximumHorizontalDelta:F2}"));
        checks.Add(new StartupCheck(
            "seeded_autonomy_applies_whole_body_jump",
            sawJumpImpulse && lab.Buddy.ActiveDrive.JumpImpulseCount > 0 && minimumTorsoY < start.Y - 8.0f,
            $"jump_count={lab.Buddy.ActiveDrive.JumpImpulseCount} rise={start.Y - minimumTorsoY:F2}"));
        checks.Add(new StartupCheck(
            "seeded_autonomy_moves_physical_rig",
            maximumHorizontalDelta >= 8.0f,
            $"max_dx={maximumHorizontalDelta:F2}"));
        checks.Add(new StartupCheck(
            "grounded_walk_stops_without_coast",
            idleStopObserved && idleStopBounded,
            $"observed={idleStopObserved} travel_4_ticks={idleStopMaximumTravel:F2} " +
            $"final_speed={idleStopFinalSpeed:F2}"));

        lab.Buddy.SetConsciousness(Consciousness.Unconscious);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool activeOutputsDisabled = !lab.Buddy.ActiveDrive.ActiveOutputsEnabled &&
                                     Mathf.IsZeroApprox(lab.Buddy.ActiveDrive.LastUprightTorque) &&
                                     lab.Buddy.ActiveDrive.LastBalanceForce.IsZeroApprox() &&
                                     lab.Buddy.ActiveDrive.LastLocomotionForce.IsZeroApprox() &&
                                     Mathf.IsZeroApprox(lab.Buddy.ActiveDrive.LastJumpImpulse) &&
                                     lab.Buddy.AutonomousMotion.Intent.IsSuppressed;
        checks.Add(new StartupCheck(
            "unconscious_profile_disables_active_outputs",
            activeOutputsDisabled,
            $"enabled={lab.Buddy.ActiveDrive.ActiveOutputsEnabled} suppressed={lab.Buddy.AutonomousMotion.Intent.IsSuppressed}"));

        Vector2 beforeImpulse = lab.Buddy.Rig.Torso.GlobalPosition;
        lab.Buddy.Rig.Torso.ApplyCentralImpulse(new Vector2(700.0f, -350.0f));
        // Peak excursion, not final displacement: better-damped links (feel Task 1)
        // let the impulse-driven torso swing out and settle back near its start, so
        // the endpoint understates the passive response. Peak proves the rig is not
        // frozen while unconscious.
        float peakTravel = 0.0f;
        for (int tick = 0; tick < 30; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            peakTravel = Mathf.Max(peakTravel, lab.Buddy.Rig.Torso.GlobalPosition.DistanceTo(beforeImpulse));
        }

        bool passivePhysicsContinued = peakTravel > 1.0f &&
                                       lab.Buddy.Constraints.Telemetry.Count > 0;
        checks.Add(new StartupCheck(
            "unconscious_profile_preserves_passive_physics",
            passivePhysicsContinued,
            $"peak_travel={peakTravel:F2} links={lab.Buddy.Constraints.Telemetry.Count}"));

        lab.Buddy.SetConsciousness(Consciousness.Conscious);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck(
            "conscious_profile_restores_active_drive",
            lab.Buddy.ActiveDrive.ActiveOutputsEnabled && !lab.Buddy.AutonomousMotion.Intent.IsSuppressed,
            $"enabled={lab.Buddy.ActiveDrive.ActiveOutputsEnabled} suppressed={lab.Buddy.AutonomousMotion.Intent.IsSuppressed}"));

        lab.QueueFree();
        // Let Godot release the scenario-local Resource and its native validation
        // arrays before TestRunner quits the engine. Immediate process teardown can
        // otherwise race a Godot.Collections.Array finalizer after a long run.
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<bool> WaitForStanding(SceneTree tree, BuddyLab lab, int timeoutTicks)
    {
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.Standing.Snapshot.IsStable)
            {
                return true;
            }
        }

        return false;
    }

    private static float MassCenterX(PuppetRig rig)
    {
        float weighted = 0.0f;
        float totalMass = 0.0f;
        foreach (PuppetPartBody part in rig.Parts)
        {
            weighted += part.GlobalPosition.X * part.Mass;
            totalMass += part.Mass;
        }
        return weighted / totalMass;
    }

    private static float MassCenterVelocityX(PuppetRig rig)
    {
        float weighted = 0.0f;
        float totalMass = 0.0f;
        foreach (PuppetPartBody part in rig.Parts)
        {
            weighted += part.LinearVelocity.X * part.Mass;
            totalMass += part.Mass;
        }
        return weighted / totalMass;
    }
}
