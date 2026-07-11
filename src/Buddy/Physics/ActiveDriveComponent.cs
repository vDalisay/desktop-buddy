using System;
using DesktopBuddy.Domain.Buddy;
using Godot;

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
            _gaitTick = 0;
            return;
        }

        float assistanceRamp = Recovery.State.AssistanceRamp;
        ApplyUprightTorque(assistanceRamp, mode);
        ApplyBalanceForce(mode);
        if (assistanceRamp > 0.0f)
        {
            ApplySelfRightForce(assistanceRamp, mode);
            _gaitTick = 0;
            return;
        }

        if (intent.ResistanceStrength > 0.0f)
        {
            ApplyResistance(intent, mode);
            _gaitTick = 0;
            return;
        }

        ApplyLocomotion(intent.WalkDirection, mode);
        if (intent.JumpRequested)
        {
            ApplyJump(mode);
        }
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
        if (Mathf.IsZeroApprox(direction) ||
            (Rig.Torso.LinearVelocity.X * direction) >= Profile.MaximumWalkSpeed)
        {
            _gaitTick = 0;
            return;
        }

        float totalForce = Profile.WalkForce * direction * mode.LocomotionScale;
        LastLocomotionForce = new Vector2(totalForce, 0.0f);
        float totalMass = TotalMass();
        foreach (PuppetPartBody body in Rig.Parts)
        {
            body.ApplyCentralForce(LastLocomotionForce * (body.Mass / totalMass));
        }

        int halfCycle = (_gaitTick / Profile.GaitHalfCycleTicks) & 1;
        float gait = Profile.GaitForce * mode.LocomotionScale * (halfCycle == 0 ? 1.0f : -1.0f);
        LastGaitForce = new Vector2(0.0f, gait);
        Rig.LeftFoot.ApplyCentralForce(-LastGaitForce);
        Rig.RightFoot.ApplyCentralForce(LastGaitForce);
        _gaitTick++;
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
