using System;
using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// Measures support, orientation, vertical ordering, center of mass, and motion
/// without selecting behavior or applying forces.
/// </summary>
[GlobalClass]
public partial class StandingDetector : Node
{
    private int _stableTicks;

    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public ActiveDriveProfile Profile { get; set; } = null!;

    public StandingSnapshot Snapshot { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Rig) || !Rig.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException("StandingDetector requires an initialized rig and valid profile.");
        }

        IsInitialized = true;
    }

    public void PhysicsTick()
    {
        PuppetPartBody torso = Rig.Torso;
        PuppetPartBody head = Rig.Head;
        PuppetPartBody leftFoot = Rig.LeftFoot;
        PuppetPartBody rightFoot = Rig.RightFoot;

        int supports = (leftFoot.HasSupportContact ? 1 : 0) +
                       (rightFoot.HasSupportContact ? 1 : 0);
        Vector2 supportCenter = (leftFoot.GlobalPosition + rightFoot.GlobalPosition) * 0.5f;
        Vector2 centerOfMass = ComputeCenterOfMass(out float maximumSpeed, out float centerOfMassSpeed);
        float torsoTilt = Mathf.Abs(Mathf.Wrap(torso.GlobalRotation, -Mathf.Pi, Mathf.Pi));
        float headAbove = torso.GlobalPosition.Y - head.GlobalPosition.Y;
        float feetBelow = supportCenter.Y - torso.GlobalPosition.Y;
        float centerError = Mathf.Abs(centerOfMass.X - supportCenter.X);

        // Stability is a whole-body (center-of-mass) motion criterion, not per-limb
        // (RAGDOLL 5): a foot mid-swing during a normal step must not disqualify
        // standing, or the buddy is "unstable" the entire time it walks.
        bool meets = supports > 0 &&
                     torsoTilt <= Profile.MaximumStandingTorsoTilt &&
                     headAbove >= Profile.MinimumHeadAboveTorso &&
                     feetBelow >= Profile.MinimumFeetBelowTorso &&
                     centerError <= Profile.MaximumCenterOfMassError &&
                     centerOfMassSpeed <= Profile.MaximumStandingSpeed;
        _stableTicks = meets ? _stableTicks + 1 : 0;
        bool stable = _stableTicks >= Profile.StableStandingTicks;

        Snapshot = new StandingSnapshot(
            supports,
            torsoTilt,
            headAbove,
            feetBelow,
            centerError,
            maximumSpeed,
            _stableTicks,
            meets,
            stable,
            centerOfMass,
            supportCenter);
    }

    public void Reset()
    {
        _stableTicks = 0;
        Snapshot = default;
    }

    private Vector2 ComputeCenterOfMass(out float maximumSpeed, out float centerOfMassSpeed)
    {
        Vector2 weighted = Vector2.Zero;
        Vector2 weightedVelocity = Vector2.Zero;
        float totalMass = 0.0f;
        maximumSpeed = 0.0f;
        foreach (PuppetPartBody body in Rig.Parts)
        {
            weighted += body.GlobalPosition * body.Mass;
            weightedVelocity += body.LinearVelocity * body.Mass;
            totalMass += body.Mass;
            maximumSpeed = Mathf.Max(maximumSpeed, body.LinearVelocity.Length());
        }

        centerOfMassSpeed = totalMass > 0.0f ? (weightedVelocity / totalMass).Length() : 0.0f;
        return totalMass > 0.0f ? weighted / totalMass : Rig.Torso.GlobalPosition;
    }
}
