using System;
using DesktopBuddy.App;
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
    [Export] public RayCast2D LeftObstacleCast { get; set; } = null!;
    [Export] public RayCast2D RightObstacleCast { get; set; } = null!;

    private Rect2 _walkableBounds;
    private bool _hasWalkableBounds;

    public AutonomousMotionIntent Intent { get; private set; }
    public ulong Seed { get; private set; }
    public int JumpRequestCount { get; private set; }
    public bool BlockedLeft { get; private set; }
    public bool BlockedRight { get; private set; }

    /// <summary>Pressed against the wall, with no clearance left for the comfort margin.</summary>
    public bool ContactLeft { get; private set; }
    public bool ContactRight { get; private set; }
    public bool IsWallStopping { get; private set; }
    public bool ObstacleLeft { get; private set; }
    public bool ObstacleRight { get; private set; }
    public float LeftWallClearance { get; private set; } = float.PositiveInfinity;
    public float RightWallClearance { get; private set; } = float.PositiveInfinity;
    public bool IsInitialized { get; private set; }

    public void Initialize(ulong seed)
    {
        if (!GodotObject.IsInstanceValid(Standing) || !Standing.IsInitialized ||
            !GodotObject.IsInstanceValid(Rig) || !Rig.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || !Profile.IsRuntimeValid ||
            !GodotObject.IsInstanceValid(LeftObstacleCast) ||
            !GodotObject.IsInstanceValid(RightObstacleCast))
        {
            throw new InvalidOperationException(
                "AutonomousMotionComponent requires an initialized standing detector and valid profile.");
        }

        Reseed(seed);
        ConfigureObstacleCast(LeftObstacleCast, -Profile.ObstacleProbeDistance);
        ConfigureObstacleCast(RightObstacleCast, Profile.ObstacleProbeDistance);
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
        UpdateObstacleSensing();
        Intent = _planner.Tick(enabled, canWalk, canJump, BlockedLeft, BlockedRight);
        if (Intent.JumpRequested)
        {
            JumpRequestCount++;
        }
    }

    public bool ObstacleInCommittedPath(float walkDirection) =>
        walkDirection < 0.0f ? ObstacleLeft :
        walkDirection > 0.0f && ObstacleRight;

    private static void ConfigureObstacleCast(RayCast2D cast, float targetX)
    {
        cast.Enabled = true;
        cast.CollisionMask = CollisionLayers.LooseObjects;
        cast.CollideWithAreas = false;
        cast.CollideWithBodies = true;
        // Once the buddy is touching a ball, the probe origin can sit *inside* the ball's
        // circle, and a ray that starts inside a shape reports nothing by default. That is
        // exactly the case the hop is for, so it must still register.
        cast.HitFromInside = true;
        cast.TargetPosition = new Vector2(targetX, 0.0f);
    }

    private void UpdateObstacleSensing()
    {
        // Probe just above the floor line, not at torso height: the objects the buddy
        // would hop over rest on the ground, so a chest-height ray sees none of them.
        Vector2 torso = Rig.Torso.GlobalPosition +
            new Vector2(0.0f, Profile.ObstacleProbeHeightOffset);
        LeftObstacleCast.GlobalPosition = torso;
        RightObstacleCast.GlobalPosition = torso;
        LeftObstacleCast.ForceRaycastUpdate();
        RightObstacleCast.ForceRaycastUpdate();
        ObstacleLeft = LeftObstacleCast.IsColliding();
        ObstacleRight = RightObstacleCast.IsColliding();
    }

    private void UpdateWallSensing()
    {
        BlockedLeft = false;
        BlockedRight = false;
        ContactLeft = false;
        ContactRight = false;
        IsWallStopping = false;
        LeftWallClearance = float.PositiveInfinity;
        RightWallClearance = float.PositiveInfinity;
        if (!_hasWalkableBounds)
            return;

        float leftEdge = float.PositiveInfinity;
        float rightEdge = float.NegativeInfinity;
        float totalMass = 0.0f;
        float velocityX = 0.0f;
        for (int index = 0; index < Rig.Parts.Count; index++)
        {
            PuppetPartBody part = Rig.Parts[index];
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
        // Contact is measured from the body, not from the widest part, and without the
        // comfort margin or the look-ahead. A swinging hand reaches the wall roughly 23 px
        // before the torso does, which is exactly the gap that made a ball resting in a
        // corner unreachable: the buddy stopped 51 px away and stood there. Arms may press
        // into the wall — containment keeps every part in the room regardless.
        float torsoX = Rig.Torso.GlobalPosition.X;
        float torsoRadius = Rig.Torso.Radius;
        ContactLeft = torsoX - torsoRadius <= _walkableBounds.Position.X;
        ContactRight = torsoX + torsoRadius >= _walkableBounds.End.X;
        IsWallStopping = (BlockedLeft && velocityX < -0.5f) || (BlockedRight && velocityX > 0.5f);
    }
}
