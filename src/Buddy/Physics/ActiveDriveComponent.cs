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
    public int JumpImpulseCount { get; private set; }
    public bool ActiveOutputsEnabled { get; private set; }
    public bool IsInitialized { get; private set; }

    private int _gaitTick;
    private bool _jumpPending;
    private int _jumpCrouchRemaining;
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

        ConsciousnessDriveProfile mode = consciousness == Consciousness.Conscious
            ? ConsciousProfile
            : UnconsciousProfile;
        ActiveOutputsEnabled = mode.ActiveDriveEnabled;
        if (!ActiveOutputsEnabled)
        {
            ResetGaitState();
            return;
        }

        float assistanceRamp = Recovery.State.AssistanceRamp;
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

        ApplyLocomotion(intent.WalkDirection, mode);
        UpdateJump(intent.JumpRequested, mode);
    }

    /// <summary>Development/test hook: hold ambient walk/jump gait off while keeping upright + balance.</summary>
    public bool SuppressLocomotion { get; set; }

    private void ResetGaitState()
    {
        _gaitTick = 0;
        _jumpPending = false;
        _jumpCrouchRemaining = 0;
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

    private void ApplyLocomotion(float direction, ConsciousnessDriveProfile mode)
    {
        direction = Mathf.Clamp(direction, -1.0f, 1.0f);
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

        DriveFootToTarget(Rig.LeftFoot, _leftFootRest, gait.LeftFootOffset, mode);
        DriveFootToTarget(Rig.RightFoot, _rightFootRest, gait.RightFootOffset, mode);

        // Whole-body propulsion assist, capped at the walk-speed ceiling.
        if ((Rig.Torso.LinearVelocity.X * direction) < Profile.MaximumWalkSpeed)
        {
            float assist = Profile.WalkForce * Profile.WalkAssistScale * direction * mode.LocomotionScale;
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

    private void DriveFootToTarget(PuppetPartBody foot, Vector2 restOffset, NumericsVector2 gaitOffset, ConsciousnessDriveProfile mode)
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
    }

    private void UpdateJump(bool jumpRequested, ConsciousnessDriveProfile mode)
    {
        if (jumpRequested && !_jumpPending)
        {
            _jumpPending = true;
            _jumpCrouchRemaining = Profile.JumpCrouchTicks;
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

        ApplyJump(mode);
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

    private void ApplyJump(ConsciousnessDriveProfile mode)
    {
        float totalImpulse = Profile.JumpImpulse * mode.JumpScale;
        LastJumpImpulse = totalImpulse;
        float totalMass = TotalMass();
        foreach (PuppetPartBody body in Rig.Parts)
        {
            body.ApplyCentralImpulse(new Vector2(0.0f, -totalImpulse * (body.Mass / totalMass)));
        }

        JumpImpulseCount++;
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
