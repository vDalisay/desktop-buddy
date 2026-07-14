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
    public Vector2 LastBalanceForce { get; private set; }
    public Vector2 LastLocomotionForce { get; private set; }
    public Vector2 LastGaitForce { get; private set; }
    public float LastJumpImpulse { get; private set; }
    public Vector2 LastResistanceForce { get; private set; }
    public Vector2 LastLeftGuardForce { get; private set; }
    public Vector2 LastRightGuardForce { get; private set; }
    public Vector2 LastGuardReactionForce { get; private set; }
    public Vector2 LastGuardCounterImpulse { get; private set; }
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

        IsInitialized = true;
    }

    public void PhysicsTick(Consciousness consciousness, DriveIntent intent)
    {
        LastUprightTorque = 0.0f;
        LastBalanceForce = Vector2.Zero;
        LastLocomotionForce = Vector2.Zero;
        LastGaitForce = Vector2.Zero;
        LastJumpImpulse = 0.0f;
        LastResistanceForce = Vector2.Zero;
        LastLeftGuardForce = Vector2.Zero;
        LastRightGuardForce = Vector2.Zero;
        LastGuardReactionForce = Vector2.Zero;

        ConsciousnessDriveProfile mode = consciousness == Consciousness.Conscious
            ? ConsciousProfile
            : UnconsciousProfile;
        ActiveOutputsEnabled = mode.ActiveDriveEnabled;
        if (!ActiveOutputsEnabled)
        {
            ResetGaitState();
            return;
        }

        float assistanceRamp = SuppressRecovery ? 0.0f : Recovery.State.AssistanceRamp;
        ApplyUprightTorque(assistanceRamp, mode);
        ApplyBalanceForce(mode);
        if (assistanceRamp > 0.0f)
        {
            ApplySelfRightForce(assistanceRamp, mode);
            ResetGaitState();
            return;
        }

        if (intent.ResistanceStrength > 0.0f)
        {
            ApplyResistance(intent, mode);
            ResetGaitState();
            return;
        }

        // Lab affordance: isolate the passive rig + upright/balance response from
        // autonomous walking/jumping (used by passive-structure regressions).
        if (SuppressLocomotion)
        {
            ResetGaitState();
            return;
        }

        ApplyLocomotion(intent.WalkDirection, intent.LocomotionScale, mode);
        if (intent.GuardActive)
            ApplyGuardHands(intent, mode);
        UpdateJump(intent, mode);
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
        LastLeftGuardForce = DriveHandToGuardTarget(
            Rig.LeftHand, intent.LeftGuardTarget, targetVelocity, intent, mode);
        LastRightGuardForce = DriveHandToGuardTarget(
            Rig.RightHand, intent.RightGuardTarget, targetVelocity, intent, mode);

        // Guarding is internal actuation. Cancel its net external force on the
        // torso so reaching for the body-relative targets cannot tow the puppet
        // toward the pointer; locomotion remains the only deliberate translation.
        LastGuardReactionForce = -(LastLeftGuardForce + LastRightGuardForce);
        Rig.Torso.ApplyCentralForce(LastGuardReactionForce);
    }

    private static Vector2 DriveHandToGuardTarget(
        PuppetPartBody hand,
        Vector2 target,
        Vector2 targetVelocity,
        DriveIntent intent,
        ConsciousnessDriveProfile mode)
    {
        Vector2 force = ((target - hand.GlobalPosition) * intent.GuardStiffness) -
                        ((hand.LinearVelocity - targetVelocity) * intent.GuardDamping);
        force *= mode.LocomotionScale;
        float maximum = Mathf.Max(0.0f, intent.GuardMaximumForce);
        if (force.Length() > maximum)
            force = force.Normalized() * maximum;
        hand.ApplyCentralForce(force);
        return force;
    }

    private float TotalMass()
    {
        float total = 0.0f;
        foreach (PuppetPartBody body in Rig.Parts)
        {
            total += body.Mass;
        }

        return total;
    }
}
