using System;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Physics;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// Translates standing/recovery state into bounded physical torque and forces.
/// It does not choose autonomous behavior and never assigns transforms.
/// </summary>
[GlobalClass]
public partial class ActiveDriveComponent : Node
{
    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public StandingDetector Standing { get; set; } = null!;
    [Export] public RecoveryComponent Recovery { get; set; } = null!;
    [Export] public ActiveDriveProfile Profile { get; set; } = null!;
    [Export] public ConsciousnessDriveProfile ConsciousProfile { get; set; } = null!;
    [Export] public ConsciousnessDriveProfile UnconsciousProfile { get; set; } = null!;

    public float LastUprightTorque { get; private set; }
    public float LastHangAlignTorque { get; private set; }
    public float LastHeadUprightTorque { get; private set; }
    public Vector2 LastBalanceForce { get; private set; }
    public Vector2 LastLocomotionForce { get; private set; }
    public Vector2 LastGaitForce { get; private set; }
    public float LastJumpImpulse { get; private set; }
    public Vector2 LastResistanceForce { get; private set; }
    public Vector2 LastLeftGuardForce { get; private set; }
    public Vector2 LastRightGuardForce { get; private set; }
    public Vector2 LastGuardReactionForce { get; private set; }
    public Vector2 LastGuardCounterImpulse { get; private set; }
    public Vector2 LastLeftPanicHandForce { get; private set; }
    public Vector2 LastRightPanicHandForce { get; private set; }
    public Vector2 LastRightHandReachForce { get; private set; }
    public Vector2 LastLeftHandReachForce { get; private set; }
    public Vector2 LastRightHandReachReactionForce { get; private set; }
    public Vector2 LastActivityHeadReactionForce { get; private set; }
    public Vector2 LastActivityTorsoReactionForce { get; private set; }
    public Vector2 LastStationaryForce { get; private set; }
    public Vector2 LastLeftObjectHandForce { get; private set; }
    public Vector2 LastRightObjectHandForce { get; private set; }
    public Vector2 LastObjectScoopDipForce { get; private set; }
    public Vector2 LastObjectReleaseImpulse { get; private set; }
    public int ObjectTossCount { get; private set; }
    public int ObjectDiscardCount { get; private set; }
    public int HeadRightingDelayTicksRemaining { get; private set; }
    public int GuardAbsorptionCount { get; private set; }
    public int JumpImpulseCount { get; private set; }
    public bool ActiveOutputsEnabled { get; private set; }
    public bool IsInitialized { get; private set; }

    private int _gaitTick;
    private bool _jumpPending;
    private int _jumpCrouchRemaining;
    private float _pendingJumpDirection;
    private float _pendingJumpScale = 1.0f;
    private float _pendingJumpHorizontalRatio;
    private Vector2 _leftFootRest;
    private Vector2 _rightFootRest;
    private readonly Vector2[] _hangRestDirections =
        new Vector2[PuppetRigProfile.RequiredPartCount];
    private float _totalMass;

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Rig) || !Rig.IsInitialized ||
            !GodotObject.IsInstanceValid(Standing) || !Standing.IsInitialized ||
            !GodotObject.IsInstanceValid(Recovery) || !Recovery.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0 ||
            !GodotObject.IsInstanceValid(ConsciousProfile) || ConsciousProfile.Validate().Count > 0 ||
            !GodotObject.IsInstanceValid(UnconsciousProfile) || UnconsciousProfile.Validate().Count > 0 ||
            UnconsciousProfile.ActiveDriveEnabled)
        {
            throw new InvalidOperationException("ActiveDriveComponent dependencies are incomplete or invalid.");
        }

        // Foot rest offsets relative to the torso, for the gait's step targets.
        Vector2 torsoRest = Rig.Profile.FindPart(BuddyPartId.Torso)!.RestPosition;
        _leftFootRest = Rig.Profile.FindPart(BuddyPartId.LeftFoot)!.RestPosition - torsoRest;
        _rightFootRest = Rig.Profile.FindPart(BuddyPartId.RightFoot)!.RestPosition - torsoRest;

        Vector2 restCenterOfMass = Vector2.Zero;
        _totalMass = 0.0f;
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            PuppetPartDefinition part = Rig.Profile.FindPart((BuddyPartId)index)!;
            restCenterOfMass += part.RestPosition * part.Mass;
            _totalMass += part.Mass;
        }
        restCenterOfMass /= _totalMass;
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            PuppetPartDefinition part = Rig.Profile.FindPart((BuddyPartId)index)!;
            _hangRestDirections[index] = restCenterOfMass - part.RestPosition;
        }

        IsInitialized = true;
    }

    public void PhysicsTick(
        Consciousness consciousness,
        DriveIntent intent,
        BuddyPartId? grabbedPart,
        Vector2 grabWorldAnchor)
    {
        LastUprightTorque = 0.0f;
        LastHangAlignTorque = 0.0f;
        LastHeadUprightTorque = 0.0f;
        LastBalanceForce = Vector2.Zero;
        LastLocomotionForce = Vector2.Zero;
        LastGaitForce = Vector2.Zero;
        LastJumpImpulse = 0.0f;
        LastResistanceForce = Vector2.Zero;
        LastLeftGuardForce = Vector2.Zero;
        LastRightGuardForce = Vector2.Zero;
        LastGuardReactionForce = Vector2.Zero;
        LastLeftPanicHandForce = Vector2.Zero;
        LastRightPanicHandForce = Vector2.Zero;
        LastRightHandReachForce = Vector2.Zero;
        LastLeftHandReachForce = Vector2.Zero;
        LastRightHandReachReactionForce = Vector2.Zero;
        LastActivityHeadReactionForce = Vector2.Zero;
        LastActivityTorsoReactionForce = Vector2.Zero;
        LastStationaryForce = Vector2.Zero;
        LastLeftObjectHandForce = Vector2.Zero;
        LastRightObjectHandForce = Vector2.Zero;
        LastObjectScoopDipForce = Vector2.Zero;
        LastObjectReleaseImpulse = Vector2.Zero;

        if (HeadRightingDelayTicksRemaining > 0)
            HeadRightingDelayTicksRemaining--;

        ConsciousnessDriveProfile mode = consciousness == Consciousness.Conscious
            ? ConsciousProfile
            : UnconsciousProfile;
        bool dangled = grabbedPart is not null && Standing.Snapshot.SupportContactCount == 0;
        ActiveOutputsEnabled = mode.ActiveDriveEnabled && !dangled;
        float assistanceRamp = SuppressRecovery || dangled ? 0.0f : Recovery.State.AssistanceRamp;
        if (dangled)
        {
            // Airborne grab is a passive ragdoll, exactly like unconscious drive.
            // The one bounded torque below emulates the rotation that off-center
            // structural anchors would create; every ordinary active output stays off.
            ApplyHangAlignment(grabbedPart!.Value, grabWorldAnchor);
            ResetGaitState();
            return;
        }
        if (!ActiveOutputsEnabled)
        {
            ResetGaitState();
            return;
        }

        ApplyUprightTorque(assistanceRamp, mode);
        ApplyHeadUprightTorque(mode);
        ApplyBalanceForce(mode);
        if (assistanceRamp > 0.0f)
        {
            ApplySelfRightForce(assistanceRamp, mode);
            ResetGaitState();
            return;
        }

        if (intent.ResistanceStrength > 0.0f)
        {
            // Strain against the tether, then fall through to locomotion and the panic hands.
            // This used to return early and reset the gait, which is why a resisting buddy
            // slid sideways as one lump with dead feet (owner feel note 2026-07-25). The
            // whole-body force is now a strain assist on top of real stepping, not a
            // replacement for it.
            ApplyResistance(intent, mode);
        }

        // Lab affordance: isolate the passive rig + upright/balance response from
        // autonomous walking/jumping (used by passive-structure regressions).
        if (SuppressLocomotion)
        {
            ResetGaitState();
            return;
        }

        bool groundedIdle = Mathf.IsZeroApprox(intent.WalkDirection) &&
            Standing.Snapshot.SupportContactCount > 0 && !intent.JumpRequested;
        if (intent.StationaryActive || groundedIdle)
        {
            ApplyStationaryBrake(mode);
            ResetGaitState();
        }
        else
        {
            ApplyLocomotion(intent.WalkDirection, intent.LocomotionScale, mode);
        }
        if (intent.GuardActive)
            ApplyGuardHands(intent, mode);
        else if (intent.PanicLeftHandActive || intent.PanicRightHandActive)
            ApplyPanicHands(intent, mode);
        else if (intent.ActivityHandReachActive)
            ApplyActivityHandReach(intent, mode);
        else if (intent.ObjectCommand.DrivesHands)
            ApplyObjectHandReach(intent.ObjectCommand, mode);
        ApplyObjectBodyCommand(intent);
        UpdateJump(intent, mode);
    }

    private void ApplyObjectHandReach(
        in ObjectDriveCommand command,
        ConsciousnessDriveProfile mode)
    {
        Vector2 targetVelocity = Rig.Torso.LinearVelocity;
        LastLeftObjectHandForce = DriveHandToTarget(
            Rig.LeftHand,
            command.LeftHandTarget,
            targetVelocity,
            command.HandStiffness,
            command.HandDamping,
            command.MaximumHandForce,
            mode);
        LastRightObjectHandForce = DriveHandToTarget(
            Rig.RightHand,
            command.RightHandTarget,
            targetVelocity,
            command.HandStiffness,
            command.HandDamping,
            command.MaximumHandForce,
            mode);
        Rig.Torso.ApplyCentralForce(-(LastLeftObjectHandForce + LastRightObjectHandForce));

        if (command.Action == ObjectDriveAction.Scoop && command.DipForce > 0.0f)
        {
            // A slight bend, not a squat: bounded force only, never a transform write.
            LastObjectScoopDipForce = new Vector2(0.0f, command.DipForce * mode.LocomotionScale);
            Rig.Torso.ApplyCentralForce(LastObjectScoopDipForce);
            Rig.Head.ApplyCentralForce(LastObjectScoopDipForce * 0.4f);
        }
        else
        {
            LastObjectScoopDipForce = Vector2.Zero;
        }
    }

    /// <summary>
    /// Only the one-shot release impulse remains. A held object is attached at the hand by
    /// <c>ObjectInteractionComponent</c>, so there is nothing to spring toward the buddy —
    /// that spring is what made objects float in rather than being caught.
    /// </summary>
    private void ApplyObjectBodyCommand(in DriveIntent intent)
    {
        ObjectDriveCommand command = intent.ObjectCommand;
        if (!command.Active || !GodotObject.IsInstanceValid(command.Body) || !command.Releases)
            return;

        command.Body!.ApplyCentralImpulse(command.ReleaseImpulse);
        LastObjectReleaseImpulse = command.ReleaseImpulse;
        if (command.Action == ObjectDriveAction.Toss)
            ObjectTossCount++;
        else
            ObjectDiscardCount++;
    }

    private void ApplyStationaryBrake(ConsciousnessDriveProfile mode)
    {
        float totalMass = TotalMass();
        float centerVelocityX = 0.0f;
        foreach (PuppetPartBody body in Rig.Parts)
            centerVelocityX += body.LinearVelocity.X * (body.Mass / totalMass);

        float forceX = Mathf.Clamp(
            -centerVelocityX * Profile.StationaryHorizontalDamping * mode.LocomotionScale,
            -Profile.MaximumStationaryForce,
            Profile.MaximumStationaryForce);
        LastStationaryForce = new Vector2(forceX, 0.0f);
        foreach (PuppetPartBody body in Rig.Parts)
            body.ApplyCentralForce(LastStationaryForce * (body.Mass / totalMass));
    }

    /// <summary>Re-arms the calm window after an accepted head impact or live head grab.</summary>
    public void NotifyHeadDisturbed()
    {
        if (!IsInitialized)
            return;
        HeadRightingDelayTicksRemaining = Profile.HeadRightingDelayTicks;
    }

    /// <summary>Development/test hook: hold ambient walk/jump gait off while keeping upright + balance.</summary>
    public bool SuppressLocomotion { get; set; }

    /// <summary>Development/test hook for isolated passive-structure probes that deliberately remove all support.</summary>
    public bool SuppressRecovery { get; set; }

    /// <summary>
    /// Cancels the guarded fraction of a real Boxing Glove solver impulse on a
    /// braced hand. The pipeline has already confirmed attribution and the 0.5x
    /// absorption coefficient; this physics component alone applies the matching
    /// counter-impulse on the authoritative clock.
    /// </summary>
    public void AbsorbGuardedImpact(
        BuddyPart part,
        Vector2 contactNormal,
        float rawImpulse,
        float acceptedFraction)
    {
        if (!IsInitialized || part is not (BuddyPart.LeftHand or BuddyPart.RightHand) ||
            !contactNormal.IsFinite() || !float.IsFinite(rawImpulse) || rawImpulse <= 0.0f ||
            !float.IsFinite(acceptedFraction))
        {
            return;
        }

        float cancelledFraction = 1.0f - Mathf.Clamp(acceptedFraction, 0.0f, 1.0f);
        Vector2 normal = contactNormal.IsZeroApprox() ? Vector2.Zero : contactNormal.Normalized();
        LastGuardCounterImpulse = -normal * rawImpulse * cancelledFraction;
        PuppetPartBody hand = part == BuddyPart.LeftHand ? Rig.LeftHand : Rig.RightHand;
        hand.ApplyCentralImpulse(LastGuardCounterImpulse);
        GuardAbsorptionCount++;
    }

    private void ResetGaitState()
    {
        _gaitTick = 0;
        _jumpPending = false;
        _jumpCrouchRemaining = 0;
        _pendingJumpDirection = 0.0f;
        _pendingJumpScale = 1.0f;
        _pendingJumpHorizontalRatio = 0.0f;
    }

    private void ApplyUprightTorque(float assistanceRamp, ConsciousnessDriveProfile mode)
    {
        PuppetPartBody torso = Rig.Torso;
        float error = Mathf.Wrap(-torso.GlobalRotation, -Mathf.Pi, Mathf.Pi);
        float gain = 1.0f + (Profile.AssistedTorqueMultiplier * assistanceRamp);
        float torque = (error * Profile.UprightStiffness * gain) -
                       (torso.AngularVelocity * Profile.UprightDamping);
        torque *= mode.UprightScale;
        LastUprightTorque = Mathf.Clamp(
            torque,
            -Profile.MaximumUprightTorque,
            Profile.MaximumUprightTorque);
        torso.ApplyTorque(LastUprightTorque);
    }

    private void ApplyHangAlignment(BuddyPartId grabbedPart, Vector2 grabWorldAnchor)
    {
        if (!grabWorldAnchor.IsFinite())
            return;

        Vector2 worldCenterOfMass = Vector2.Zero;
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            PuppetPartBody body = Rig.Parts[index];
            worldCenterOfMass += body.GlobalPosition * body.Mass;
        }
        worldCenterOfMass /= _totalMass;

        Vector2 restDirection = _hangRestDirections[(int)grabbedPart];
        Vector2 actualDirection = worldCenterOfMass - grabWorldAnchor;
        HangFrameResult hang = HangFrame.Evaluate(new HangFrameInput(
            new NumericsVector2(restDirection.X, restDirection.Y),
            new NumericsVector2(actualDirection.X, actualDirection.Y)));
        if (!hang.IsValid)
            return;

        PuppetPartBody torso = Rig.Torso;
        float error = HangFrame.WrapAngle(hang.Angle - torso.GlobalRotation);
        PendulumTorqueResult pendulum = PendulumTorque.Evaluate(new PendulumTorqueInput(
            error,
            _totalMass,
            actualDirection.Length(),
            Profile.HangGravityGain,
            torso.AngularVelocity,
            Profile.HangSwingDamping,
            Profile.MaximumHangAlignTorque));
        if (!pendulum.IsValid)
            return;

        LastHangAlignTorque = pendulum.Torque;
        torso.ApplyTorque(LastHangAlignTorque);
    }

    private void ApplyHeadUprightTorque(ConsciousnessDriveProfile mode)
    {
        if (HeadRightingDelayTicksRemaining > 0)
            return;

        PuppetPartBody head = Rig.Head;
        float error = Mathf.Wrap(-head.GlobalRotation, -Mathf.Pi, Mathf.Pi);
        float torque = (error * Profile.HeadUprightStiffness) -
                       (head.AngularVelocity * Profile.HeadUprightDamping);
        torque *= mode.UprightScale;
        LastHeadUprightTorque = Mathf.Clamp(
            torque,
            -Profile.MaximumHeadUprightTorque,
            Profile.MaximumHeadUprightTorque);
        head.ApplyTorque(LastHeadUprightTorque);
    }

    private void ApplyBalanceForce(ConsciousnessDriveProfile mode)
    {
        StandingSnapshot standing = Standing.Snapshot;
        if (standing.SupportContactCount == 0)
        {
            return;
        }

        PuppetPartBody torso = Rig.Torso;
        float error = standing.SupportCenter.X - torso.GlobalPosition.X;
        float forceX = (error * Profile.BalanceStiffness) -
                       (torso.LinearVelocity.X * Profile.BalanceDamping);
        forceX *= mode.BalanceScale;
        forceX = Mathf.Clamp(forceX, -Profile.MaximumBalanceForce, Profile.MaximumBalanceForce);
        LastBalanceForce = new Vector2(forceX, 0.0f);

        torso.ApplyCentralForce(LastBalanceForce);
        Vector2 reaction = LastBalanceForce * -0.5f;
        Rig.LeftFoot.ApplyCentralForce(reaction);
        Rig.RightFoot.ApplyCentralForce(reaction);
    }

    private void ApplySelfRightForce(float assistanceRamp, ConsciousnessDriveProfile mode)
    {
        float force = Profile.SelfRightForce * assistanceRamp * mode.RecoveryScale;
        Vector2 torsoLift = new(0.0f, -force);
        Vector2 headLift = new(0.0f, -force * 0.3f);
        Vector2 footReaction = new(0.0f, force * 0.65f);

        Rig.Torso.ApplyCentralForce(torsoLift);
        Rig.Head.ApplyCentralForce(headLift);
        Rig.LeftFoot.ApplyCentralForce(footReaction);
        Rig.RightFoot.ApplyCentralForce(footReaction);
    }

    private void ApplyLocomotion(float direction, float intentScale, ConsciousnessDriveProfile mode)
    {
        direction = Mathf.Clamp(direction, -1.0f, 1.0f);
        intentScale = Mathf.Clamp(intentScale, 0.0f, 2.0f);
        if (Mathf.IsZeroApprox(direction))
        {
            _gaitTick = 0;
            return;
        }

        // Phase-driven stepping: feet swing/plant toward gait targets (the visible
        // motion), with a reduced whole-body force as a propulsion assist so the
        // buddy reliably covers ground without "sliding" (RAGDOLL 3.3 limb-target).
        float phase = (_gaitTick % Profile.GaitCycleTicks) / (float)Profile.GaitCycleTicks;
        var tuning = new GaitTuning(Profile.StepLength, Profile.StepLift, Profile.TorsoBob, Profile.TorsoLean);
        GaitSample gait = GaitCycle.Sample(phase, direction, tuning);

        Vector2 leftFootDrive = DriveFootToTarget(
            Rig.LeftFoot, _leftFootRest, gait.LeftFootOffset, mode);
        Vector2 rightFootDrive = DriveFootToTarget(
            Rig.RightFoot, _rightFootRest, gait.RightFootOffset, mode);

        // Foot targeting is internal puppet actuation. Apply the equal reaction
        // to the torso so a displaced ragdoll foot cannot tow the whole buddy in
        // the wrong direction after knockout; deliberate translation remains
        // owned by the bounded propulsion assist and real floor contacts.
        Rig.Torso.ApplyCentralForce(-(leftFootDrive + rightFootDrive));

        // Whole-body propulsion assist, capped at the walk-speed ceiling.
        if ((Rig.Torso.LinearVelocity.X * direction) < Profile.MaximumWalkSpeed * intentScale)
        {
            float assist = Profile.WalkForce * Profile.WalkAssistScale * direction *
                           intentScale * mode.LocomotionScale;
            LastLocomotionForce = new Vector2(assist, 0.0f);
            float totalMass = TotalMass();
            foreach (PuppetPartBody body in Rig.Parts)
            {
                body.ApplyCentralForce(LastLocomotionForce * (body.Mass / totalMass));
            }
        }
        else
        {
            LastLocomotionForce = Vector2.Zero;
        }

        // Torso bob (upward lift on each footfall) and forward lean into travel.
        float bobForceY = gait.TorsoBobOffset * Profile.TorsoBobStiffness * mode.LocomotionScale;
        LastGaitForce = new Vector2(0.0f, bobForceY);
        Rig.Torso.ApplyCentralForce(LastGaitForce);
        Rig.Head.ApplyCentralForce(new Vector2(gait.TorsoLeanOffset * Profile.TorsoLeanForce * mode.LocomotionScale, 0.0f));
        _gaitTick++;
    }

    private Vector2 DriveFootToTarget(
        PuppetPartBody foot,
        Vector2 restOffset,
        NumericsVector2 gaitOffset,
        ConsciousnessDriveProfile mode)
    {
        Vector2 target = Rig.Torso.GlobalPosition + restOffset + new Vector2(gaitOffset.X, gaitOffset.Y);
        Vector2 force = ((target - foot.GlobalPosition) * Profile.StepDriveStiffness) -
                        (foot.LinearVelocity * Profile.StepDriveDamping);
        force *= mode.LocomotionScale;
        if (force.Length() > Profile.StepDriveMaxForce)
        {
            force = force.Normalized() * Profile.StepDriveMaxForce;
        }

        foot.ApplyCentralForce(force);
        return force;
    }

    private void UpdateJump(DriveIntent intent, ConsciousnessDriveProfile mode)
    {
        if (intent.JumpRequested && !_jumpPending)
        {
            _jumpPending = true;
            _jumpCrouchRemaining = Profile.JumpCrouchTicks;
            _pendingJumpDirection = Mathf.Clamp(intent.JumpDirection, -1.0f, 1.0f);
            _pendingJumpScale = Mathf.Clamp(intent.JumpScale, 0.0f, 2.0f);
            _pendingJumpHorizontalRatio = Mathf.Clamp(intent.JumpHorizontalRatio, 0.0f, 1.0f);
        }

        if (!_jumpPending)
        {
            return;
        }

        if (_jumpCrouchRemaining > 0)
        {
            // Anticipation: dip the torso/head to load the legs before launch.
            float crouch = Profile.JumpCrouchForce * mode.JumpScale;
            Rig.Torso.ApplyCentralForce(new Vector2(0.0f, crouch));
            Rig.Head.ApplyCentralForce(new Vector2(0.0f, crouch * 0.4f));
            _jumpCrouchRemaining--;
            return;
        }

        ApplyJump(mode, _pendingJumpDirection, _pendingJumpScale, _pendingJumpHorizontalRatio);
        _jumpPending = false;
    }

    private void ApplyResistance(DriveIntent intent, ConsciousnessDriveProfile mode)
    {
        float direction = Mathf.Clamp(intent.ResistanceDirection, -1.0f, 1.0f);
        float strength = Mathf.Clamp(intent.ResistanceStrength, 0.0f, 1.0f);
        float totalForce = Profile.GrabResistanceForce * direction * strength * mode.LocomotionScale;
        LastResistanceForce = new Vector2(totalForce, 0.0f);

        float totalMass = TotalMass();
        foreach (PuppetPartBody body in Rig.Parts)
        {
            body.ApplyCentralForce(LastResistanceForce * (body.Mass / totalMass));
        }
    }

    private void ApplyJump(
        ConsciousnessDriveProfile mode,
        float direction,
        float intentScale,
        float horizontalRatio)
    {
        float totalImpulse = Profile.JumpImpulse * mode.JumpScale * intentScale;
        LastJumpImpulse = totalImpulse;
        float totalMass = TotalMass();
        foreach (PuppetPartBody body in Rig.Parts)
        {
            float share = body.Mass / totalMass;
            body.ApplyCentralImpulse(new Vector2(
                totalImpulse * horizontalRatio * direction * share,
                -totalImpulse * share));
        }

        JumpImpulseCount++;
    }

    private void ApplyGuardHands(DriveIntent intent, ConsciousnessDriveProfile mode)
    {
        Vector2 targetVelocity = Rig.Torso.LinearVelocity;
        LastLeftGuardForce = DriveHandToTarget(
            Rig.LeftHand, intent.LeftGuardTarget, targetVelocity,
            intent.GuardStiffness, intent.GuardDamping, intent.GuardMaximumForce, mode);
        LastRightGuardForce = DriveHandToTarget(
            Rig.RightHand, intent.RightGuardTarget, targetVelocity,
            intent.GuardStiffness, intent.GuardDamping, intent.GuardMaximumForce, mode);

        // Guarding is internal actuation. Cancel its net external force on the
        // torso so reaching for the body-relative targets cannot tow the puppet
        // toward the pointer; locomotion remains the only deliberate translation.
        LastGuardReactionForce = -(LastLeftGuardForce + LastRightGuardForce);
        Rig.Torso.ApplyCentralForce(LastGuardReactionForce);
    }

    /// <summary>
    /// Drives the hands to the frantic thrash targets of a frightened, grabbed buddy. Same
    /// bounded spring machinery as the guard and Eat reaches, and the same torso-reaction
    /// cancellation: thrashing is internal actuation and may not tow the puppet anywhere.
    /// </summary>
    private void ApplyPanicHands(DriveIntent intent, ConsciousnessDriveProfile mode)
    {
        Vector2 targetVelocity = Rig.Torso.LinearVelocity;
        if (intent.PanicLeftHandActive)
        {
            LastLeftPanicHandForce = DriveHandToTarget(
                Rig.LeftHand,
                intent.LeftPanicHandTarget,
                targetVelocity,
                Profile.PanicHandStiffness,
                Profile.PanicHandDamping,
                Profile.PanicHandMaximumForce,
                mode);
        }

        if (intent.PanicRightHandActive)
        {
            LastRightPanicHandForce = DriveHandToTarget(
                Rig.RightHand,
                intent.RightPanicHandTarget,
                targetVelocity,
                Profile.PanicHandStiffness,
                Profile.PanicHandDamping,
                Profile.PanicHandMaximumForce,
                mode);
        }

        Rig.Torso.ApplyCentralForce(-(LastLeftPanicHandForce + LastRightPanicHandForce));
    }

    private void ApplyActivityHandReach(DriveIntent intent, ConsciousnessDriveProfile mode)
    {
        Vector2 targetVelocity = (Rig.Torso.LinearVelocity + Rig.Head.LinearVelocity) * 0.5f;
        LastLeftHandReachForce = DriveHandToTarget(
            Rig.LeftHand,
            intent.LeftActivityHandTarget,
            targetVelocity,
            Profile.ActivityHandStiffness,
            Profile.ActivityHandDamping,
            Profile.ActivityHandMaximumForce,
            mode);
        LastRightHandReachForce = DriveHandToTarget(
            Rig.RightHand,
            intent.RightActivityHandTarget,
            targetVelocity,
            Profile.ActivityHandStiffness,
            Profile.ActivityHandDamping,
            Profile.ActivityHandMaximumForce,
            mode);

        // The mouth-relative target is attached to the head. Equal reaction keeps
        // this reach internal instead of letting a hand target tow the whole puppet.
        LastRightHandReachReactionForce = -(LastLeftHandReachForce + LastRightHandReachForce);
        // While first gathering the food at the chest, only one quarter of the
        // internal reaction reaches the head (half the old initial displacement).
        // The share rises smoothly to one half as the hands reach the mouth.
        float headShare = Mathf.Lerp(0.25f, 0.5f, Mathf.Clamp(intent.ActivityReachLift, 0.0f, 1.0f));
        LastActivityHeadReactionForce = LastRightHandReachReactionForce * headShare;
        LastActivityTorsoReactionForce = LastRightHandReachReactionForce * (1.0f - headShare);
        Rig.Torso.ApplyCentralForce(LastActivityTorsoReactionForce);
        Rig.Head.ApplyCentralForce(LastActivityHeadReactionForce);
    }

    private static Vector2 DriveHandToTarget(
        PuppetPartBody hand,
        Vector2 target,
        Vector2 targetVelocity,
        float stiffness,
        float damping,
        float maximumForce,
        ConsciousnessDriveProfile mode)
    {
        Vector2 force = ((target - hand.GlobalPosition) * stiffness) -
                        ((hand.LinearVelocity - targetVelocity) * damping);
        force *= mode.LocomotionScale;
        float maximum = Mathf.Max(0.0f, maximumForce);
        if (force.Length() > maximum)
            force = force.Normalized() * maximum;
        hand.ApplyCentralForce(force);
        return force;
    }

    private float TotalMass() => _totalMass;
}
