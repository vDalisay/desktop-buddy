using System;
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

    public float LastUprightTorque { get; private set; }
    public Vector2 LastBalanceForce { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Rig) || !Rig.IsInitialized ||
            !GodotObject.IsInstanceValid(Standing) || !Standing.IsInitialized ||
            !GodotObject.IsInstanceValid(Recovery) || !Recovery.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException("ActiveDriveComponent dependencies are incomplete or invalid.");
        }

        IsInitialized = true;
    }

    public void PhysicsTick(bool conscious)
    {
        LastUprightTorque = 0.0f;
        LastBalanceForce = Vector2.Zero;
        if (!conscious)
        {
            return;
        }

        float assistanceRamp = Recovery.State.AssistanceRamp;
        ApplyUprightTorque(assistanceRamp);
        ApplyBalanceForce();
        if (assistanceRamp > 0.0f)
        {
            ApplySelfRightForce(assistanceRamp);
        }
    }

    private void ApplyUprightTorque(float assistanceRamp)
    {
        PuppetPartBody torso = Rig.Torso;
        float error = Mathf.Wrap(-torso.GlobalRotation, -Mathf.Pi, Mathf.Pi);
        float gain = 1.0f + (Profile.AssistedTorqueMultiplier * assistanceRamp);
        float torque = (error * Profile.UprightStiffness * gain) -
                       (torso.AngularVelocity * Profile.UprightDamping);
        LastUprightTorque = Mathf.Clamp(
            torque,
            -Profile.MaximumUprightTorque,
            Profile.MaximumUprightTorque);
        torso.ApplyTorque(LastUprightTorque);
    }

    private void ApplyBalanceForce()
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
        forceX = Mathf.Clamp(forceX, -Profile.MaximumBalanceForce, Profile.MaximumBalanceForce);
        LastBalanceForce = new Vector2(forceX, 0.0f);

        torso.ApplyCentralForce(LastBalanceForce);
        Vector2 reaction = LastBalanceForce * -0.5f;
        Rig.LeftFoot.ApplyCentralForce(reaction);
        Rig.RightFoot.ApplyCentralForce(reaction);
    }

    private void ApplySelfRightForce(float assistanceRamp)
    {
        float force = Profile.SelfRightForce * assistanceRamp;
        Vector2 torsoLift = new(0.0f, -force);
        Vector2 headLift = new(0.0f, -force * 0.3f);
        Vector2 footReaction = new(0.0f, force * 0.65f);

        Rig.Torso.ApplyCentralForce(torsoLift);
        Rig.Head.ApplyCentralForce(headLift);
        Rig.LeftFoot.ApplyCentralForce(footReaction);
        Rig.RightFoot.ApplyCentralForce(footReaction);
    }
}
