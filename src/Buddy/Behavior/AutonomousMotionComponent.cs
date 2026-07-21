using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Selects seeded ambient idle/walk/jump intent from physical support state.
/// It never applies forces and is suppressed by consciousness or recovery.
/// </summary>
[GlobalClass]
public partial class AutonomousMotionComponent : Node
{
    private AutonomousMotionPlanner? _planner;

    [Export] public StandingDetector Standing { get; set; } = null!;
    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public AutonomousMotionProfile Profile { get; set; } = null!;

    private Rect2 _walkableBounds;
    private bool _hasWalkableBounds;

    public AutonomousMotionIntent Intent { get; private set; }
    public ulong Seed { get; private set; }
    public int JumpRequestCount { get; private set; }
    public bool BlockedLeft { get; private set; }
    public bool BlockedRight { get; private set; }
    public bool IsWallStopping { get; private set; }
    public float LeftWallClearance { get; private set; } = float.PositiveInfinity;
    public float RightWallClearance { get; private set; } = float.PositiveInfinity;
    public bool IsInitialized { get; private set; }

    public void Initialize(ulong seed)
    {
        if (!GodotObject.IsInstanceValid(Standing) || !Standing.IsInitialized ||
            !GodotObject.IsInstanceValid(Rig) || !Rig.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException(
                "AutonomousMotionComponent requires an initialized standing detector and valid profile.");
        }

        Reseed(seed);
        IsInitialized = true;
    }

    /// <summary>Injected by the room owner whenever its authoritative inner bounds change.</summary>
    public void SetWalkableBounds(Rect2 bounds)
    {
        if (bounds.Size.X <= 0.0f || bounds.Size.Y <= 0.0f ||
            !float.IsFinite(bounds.Position.X) || !float.IsFinite(bounds.End.X))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), bounds, "Walkable bounds must be finite and non-empty.");
        }

        _walkableBounds = bounds;
        _hasWalkableBounds = true;
    }

    public void Reseed(ulong seed)
    {
        Seed = seed;
        _planner = new AutonomousMotionPlanner(new SeededRandomSource(seed), Profile.ToTuning());
        Intent = default;
        JumpRequestCount = 0;
    }

    public void PhysicsTick(
        Consciousness consciousness,
        RecoveryClockState recovery,
        bool behaviorEnabled = true)
    {
        if (_planner is null)
        {
            throw new InvalidOperationException("AutonomousMotionComponent was ticked before initialization.");
        }

        StandingSnapshot standing = Standing.Snapshot;
        bool enabled = behaviorEnabled &&
            consciousness == Consciousness.Conscious && !recovery.AssistanceActive;
        bool canWalk = standing.SupportContactCount > 0;
        bool canJump = standing.IsStable;
        UpdateWallSensing();
        Intent = _planner.Tick(enabled, canWalk, canJump, BlockedLeft, BlockedRight);
        if (Intent.JumpRequested)
        {
            JumpRequestCount++;
        }
    }

    private void UpdateWallSensing()
    {
        BlockedLeft = false;
        BlockedRight = false;
        IsWallStopping = false;
        LeftWallClearance = float.PositiveInfinity;
        RightWallClearance = float.PositiveInfinity;
        if (!_hasWalkableBounds)
            return;

        float leftEdge = float.PositiveInfinity;
        float rightEdge = float.NegativeInfinity;
        float totalMass = 0.0f;
        float velocityX = 0.0f;
        foreach (PuppetPartBody part in Rig.Parts)
        {
            leftEdge = Mathf.Min(leftEdge, part.GlobalPosition.X - part.Radius);
            rightEdge = Mathf.Max(rightEdge, part.GlobalPosition.X + part.Radius);
            totalMass += part.Mass;
            velocityX += part.LinearVelocity.X * part.Mass;
        }

        velocityX = totalMass > 0.0f ? velocityX / totalMass : 0.0f;
        LeftWallClearance = leftEdge - _walkableBounds.Position.X;
        RightWallClearance = _walkableBounds.End.X - rightEdge;
        float projectedLeft = LeftWallClearance + Mathf.Min(0.0f, velocityX) * Profile.WallLookAheadSeconds;
        float projectedRight = RightWallClearance - Mathf.Max(0.0f, velocityX) * Profile.WallLookAheadSeconds;
        BlockedLeft = projectedLeft <= Profile.WallAvoidMarginPixels;
        BlockedRight = projectedRight <= Profile.WallAvoidMarginPixels;
        IsWallStopping = (BlockedLeft && velocityX < -0.5f) || (BlockedRight && velocityX > 0.5f);
    }
}
