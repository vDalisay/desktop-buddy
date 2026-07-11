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
        Vector2 centerOfMass = ComputeCenterOfMass(out float maximumSpeed);
        float torsoTilt = Mathf.Abs(Mathf.Wrap(torso.GlobalRotation, -Mathf.Pi, Mathf.Pi));
        float headAbove = torso.GlobalPosition.Y - head.GlobalPosition.Y;
        float feetBelow = supportCenter.Y - torso.GlobalPosition.Y;
        float centerError = Mathf.Abs(centerOfMass.X - supportCenter.X);

        bool meets = supports > 0 &&
                     torsoTilt <= Profile.MaximumStandingTorsoTilt &&
                     headAbove >= Profile.MinimumHeadAboveTorso &&
                     feetBelow >= Profile.MinimumFeetBelowTorso &&
                     centerError <= Profile.MaximumCenterOfMassError &&
                     maximumSpeed <= Profile.MaximumStandingSpeed;
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

    private Vector2 ComputeCenterOfMass(out float maximumSpeed)
    {
        Vector2 weighted = Vector2.Zero;
        float totalMass = 0.0f;
        maximumSpeed = 0.0f;
        foreach (PuppetPartBody body in Rig.Parts)
        {
            weighted += body.GlobalPosition * body.Mass;
            totalMass += body.Mass;
            maximumSpeed = Mathf.Max(maximumSpeed, body.LinearVelocity.Length());
        }

        return totalMass > 0.0f ? weighted / totalMass : Rig.Torso.GlobalPosition;
    }
}
