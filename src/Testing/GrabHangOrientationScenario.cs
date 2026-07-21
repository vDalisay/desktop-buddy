using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Physics;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Testing;

/// <summary>
/// Unsupported foot and hand grabs rotate the spring frame so the puppet's
/// mass hangs below the acquired part, then ordinary drive recovers on release.
/// </summary>
public sealed class GrabHangOrientationScenario : IScenario
{
    private const int HoldTicks = 600;
    private const int LatestSettleTick = 420;
    private const int StableSettleTicks = 30;
    private const int RecoveryBudgetTicks = 1200;
    private const float HighestPartTolerance = 8.0f;
    private const float TorsoAngleTolerance = 0.4f;
    private const float OvershootTrackingRange = 1.5f;
    private const float OvershootMinimumError = 0.03f;

    private static readonly BuddyPartId[] GrabbedParts =
    {
        BuddyPartId.LeftFoot,
        BuddyPartId.RightFoot,
        BuddyPartId.LeftHand,
    };

    public string Id => "grab_hang_orientation";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            return new ScenarioResult(false,
                new[] { new StartupCheck("grab_hang_orientation_scene_loadable", false, "buddy_lab") },
                messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Buddy.ReseedAutonomy(seed);
        lab.Buddy.ActiveDrive.SuppressLocomotion = true;

        bool allGrabbed = true;
        bool allPassive = true;
        bool allFinite = true;
        bool allHighest = true;
        bool allAnglesAligned = true;
        bool allTorqueObserved = true;
        bool allOvershot = true;
        bool allSettledOnTime = true;
        bool allRecovered = true;
        foreach (BuddyPartId partId in GrabbedParts)
        {
            HangObservation observation = await ObserveHang(tree, lab, partId);
            allGrabbed &= observation.Grabbed;
            allPassive &= observation.Passive;
            allFinite &= observation.Finite;
            allHighest &= observation.GrabbedPartHighest;
            allAnglesAligned &= observation.MaximumAngleError <= TorsoAngleTolerance;
            allTorqueObserved &= observation.MaximumTorque > 0.0f;
            allOvershot &= observation.Overshot;
            allSettledOnTime &= observation.SettledAtTick is >= 0 and <= LatestSettleTick;
            allRecovered &= observation.DriveResumed && observation.StandingRecovered;
            messages.Add($"grab_{partId}={observation}");
        }

        checks.Add(new StartupCheck("hang_orientation_parts_acquired", allGrabbed,
            $"grabbed={allGrabbed}"));
        checks.Add(new StartupCheck("hang_orientation_drive_remains_passive", allPassive,
            $"passive={allPassive}"));
        checks.Add(new StartupCheck("hang_orientation_grabbed_part_is_highest", allHighest,
            $"highest={allHighest} tolerance={HighestPartTolerance:F1}px"));
        checks.Add(new StartupCheck("hang_orientation_torso_matches_mass_frame", allAnglesAligned,
            $"aligned={allAnglesAligned} tolerance={TorsoAngleTolerance:F2}rad"));
        checks.Add(new StartupCheck("hang_orientation_alignment_torque_runs", allTorqueObserved,
            $"torque_observed={allTorqueObserved}"));
        checks.Add(new StartupCheck("hang_orientation_overshoots_before_settling", allOvershot,
            $"overshot={allOvershot}"));
        checks.Add(new StartupCheck("hang_orientation_settles_in_feel_window", allSettledOnTime,
            $"settled={allSettledOnTime} latest={LatestSettleTick}ticks"));
        checks.Add(new StartupCheck("hang_orientation_bodies_stay_finite", allFinite,
            $"finite={allFinite}"));
        checks.Add(new StartupCheck("hang_orientation_release_recovers_standing", allRecovered,
            $"recovered={allRecovered} budget={RecoveryBudgetTicks}ticks"));

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<HangObservation> ObserveHang(
        SceneTree tree,
        BuddyLab lab,
        BuddyPartId partId)
    {
        lab.Grab.Release();
        lab.Buddy.Rig.ResetToSafePose(new Vector2(240.0f, 240.0f));
        lab.Buddy.Standing.Reset();
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        PuppetPartBody target = lab.Buddy.Rig.GetPart(partId);
        bool grabbed = lab.Grab.TryGrab(target, target.GlobalPosition);
        float maximumTorque = 0.0f;
        bool passive = grabbed;
        bool finite = grabbed;
        int consecutiveSettledTicks = 0;
        int settledAtTick = -1;
        int lastTrackedErrorSign = 0;
        bool overshot = false;
        for (int tick = 0; tick < HoldTicks; tick++)
        {
            lab.Grab.MoveCursor(new Vector2(240.0f, 145.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.Standing.Snapshot.SupportContactCount == 0)
            {
                passive &= !lab.Buddy.ActiveDrive.ActiveOutputsEnabled &&
                    lab.Buddy.ActiveDrive.LastUprightTorque == 0.0f &&
                    lab.Buddy.ActiveDrive.LastHeadUprightTorque == 0.0f &&
                    lab.Buddy.ActiveDrive.LastBalanceForce.IsZeroApprox() &&
                    lab.Buddy.ActiveDrive.LastLocomotionForce.IsZeroApprox() &&
                    lab.Buddy.ActiveDrive.LastResistanceForce.IsZeroApprox();
                maximumTorque = Mathf.Max(
                    maximumTorque,
                    Mathf.Abs(lab.Buddy.ActiveDrive.LastHangAlignTorque));

                float currentHighestError = HighestError(lab, target);
                (float signedAngleError, _) = SignedAngleErrorFromCurrentMassFrame(lab, partId);
                float currentAngleError = Mathf.Abs(signedAngleError);
                if (currentAngleError <= OvershootTrackingRange &&
                    currentAngleError >= OvershootMinimumError)
                {
                    int currentSign = Math.Sign(signedAngleError);
                    overshot |= lastTrackedErrorSign != 0 && currentSign != lastTrackedErrorSign;
                    lastTrackedErrorSign = currentSign;
                }
                bool currentFootOrdering =
                    partId is not (BuddyPartId.LeftFoot or BuddyPartId.RightFoot) ||
                    lab.Buddy.Rig.Head.GlobalPosition.Y > lab.Buddy.Rig.Torso.GlobalPosition.Y;
                bool currentlySettled = currentHighestError <= HighestPartTolerance &&
                    currentAngleError <= TorsoAngleTolerance && currentFootOrdering;
                consecutiveSettledTicks = currentlySettled ? consecutiveSettledTicks + 1 : 0;
                if (settledAtTick < 0 && consecutiveSettledTicks >= StableSettleTicks)
                    settledAtTick = tick - StableSettleTicks + 1;
            }
            finite &= lab.Buddy.Rig.AllBodiesFinite() && PresentationSocketsFinite(lab);
        }

        float highestError = HighestError(lab, target);
        bool grabbedPartHighest = highestError <= HighestPartTolerance;
        (float signedAngleErrorAtEnd, float targetAngle) =
            SignedAngleErrorFromCurrentMassFrame(lab, partId);
        float maximumAngleError = Mathf.Abs(signedAngleErrorAtEnd);
        float torsoAngle = lab.Buddy.Rig.Torso.GlobalRotation;
        bool footHeadBelowTorso = partId is not (BuddyPartId.LeftFoot or BuddyPartId.RightFoot) ||
            lab.Buddy.Rig.Head.GlobalPosition.Y > lab.Buddy.Rig.Torso.GlobalPosition.Y;
        grabbedPartHighest &= footHeadBelowTorso;

        lab.Grab.Release();
        bool driveResumed = false;
        bool standingRecovered = false;
        for (int tick = 0; tick < RecoveryBudgetTicks && !standingRecovered; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            driveResumed |= lab.Buddy.ActiveDrive.ActiveOutputsEnabled;
            standingRecovered = lab.Buddy.Standing.Snapshot.IsStable;
        }

        return new HangObservation(
            grabbed,
            passive,
            finite,
            grabbedPartHighest,
            highestError,
            footHeadBelowTorso,
            maximumAngleError,
            targetAngle,
            torsoAngle,
            maximumTorque,
            overshot,
            settledAtTick,
            driveResumed,
            standingRecovered);
    }

    private static float HighestError(BuddyLab lab, PuppetPartBody grabbed)
    {
        float minimumOtherY = float.PositiveInfinity;
        foreach (PuppetPartBody body in lab.Buddy.Rig.Parts)
        {
            if (!ReferenceEquals(body, grabbed))
                minimumOtherY = Mathf.Min(minimumOtherY, body.GlobalPosition.Y);
        }

        return grabbed.GlobalPosition.Y - minimumOtherY;
    }

    private static bool PresentationSocketsFinite(BuddyLab lab)
    {
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            Node3D socket = lab.VisualPresenter.GetPartSocket((BuddyPartId)index);
            if (!socket.GlobalPosition.IsFinite() || !socket.GlobalRotation.IsFinite())
                return false;
        }

        return true;
    }

    private static (float Error, float Target) SignedAngleErrorFromCurrentMassFrame(
        BuddyLab lab,
        BuddyPartId grabbedPart)
    {
        float totalMass = 0.0f;
        Vector2 restCenter = Vector2.Zero;
        Vector2 worldCenter = Vector2.Zero;
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            PuppetPartDefinition definition = lab.Buddy.Rig.Profile.FindPart((BuddyPartId)index)!;
            PuppetPartBody body = lab.Buddy.Rig.GetPart((BuddyPartId)index);
            totalMass += definition.Mass;
            restCenter += definition.RestPosition * definition.Mass;
            worldCenter += body.GlobalPosition * definition.Mass;
        }
        restCenter /= totalMass;
        worldCenter /= totalMass;

        PuppetPartDefinition grabbedDefinition = lab.Buddy.Rig.Profile.FindPart(grabbedPart)!;
        Vector2 restDirection = restCenter - grabbedDefinition.RestPosition;
        Vector2 actualDirection = worldCenter - lab.Grab.CurrentGrab.CursorAnchor;
        HangFrameResult expected = HangFrame.Evaluate(new HangFrameInput(
            new NumericsVector2(restDirection.X, restDirection.Y),
            new NumericsVector2(actualDirection.X, actualDirection.Y)));
        return expected.IsValid
            ? (HangFrame.WrapAngle(expected.Angle - lab.Buddy.Rig.Torso.GlobalRotation),
               expected.Angle)
            : (float.PositiveInfinity, 0.0f);
    }

    private readonly record struct HangObservation(
        bool Grabbed,
        bool Passive,
        bool Finite,
        bool GrabbedPartHighest,
        float HighestError,
        bool FootHeadBelowTorso,
        float MaximumAngleError,
        float TargetAngle,
        float TorsoAngle,
        float MaximumTorque,
        bool Overshot,
        int SettledAtTick,
        bool DriveResumed,
        bool StandingRecovered);
}
