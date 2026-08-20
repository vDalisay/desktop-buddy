using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Selects seeded ambient idle/walk/jump intent from physical support state.
/// It never applies forces and is suppressed by consciousness or recovery.
/// </summary>
[GlobalClass]
public partial class AutonomousMotionComponent : Node
{
    private const float RoomInterestArrivalPixels = 28.0f;

    private AutonomousMotionPlanner? _planner;
    private Vector2 _roomInterestTarget;
    private int _roomInterestTicksRemaining;
    private int _roomGazeTicksRemaining;
    private int _roomInterestGazeTicks;

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

    /// <summary>Consecutive ticks the committed walk has been blocked by a loose object.</summary>
    public int ObstructedTicks { get; private set; }

    /// <summary>How many times an unpassable obstacle has turned the buddy around.</summary>
    public int ObstacleTurnAroundCount { get; private set; }

    private AutonomousMotionGoal _lastPlannedGoal;
    private float _notedObstacleDirection;
    private bool _notedObstacle;
    private int _obstacleClearTicks;

    /// <summary>
    /// The arbiter's combined obstacle verdict, fed back for the next tick. The layer-3 ray on
    /// its own is intermittent — which is exactly why the arbiter ORs it with the registry's
    /// view of resting objects — and an accumulator driven by the flickering half never
    /// reached the give-up threshold. One tick of lag at 120 Hz is invisible.
    /// </summary>
    public void NoteObstacleEvidence(float walkDirection, bool obstructed)
    {
        _notedObstacleDirection = walkDirection;
        _notedObstacle = obstructed;
    }
    public float LeftWallClearance { get; private set; } = float.PositiveInfinity;
    public float RightWallClearance { get; private set; } = float.PositiveInfinity;
    public Rect2 WalkableBounds => _walkableBounds;
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// True while a presentation/personality layer has an ambient room target queued. This is
    /// still ordinary AmbientAutonomy: BehaviorArbiter disables this worker whenever any player,
    /// need, fun, panic, or committed-reaction behavior has priority.
    /// </summary>
    public bool HasRoomInterest => _roomInterestTicksRemaining > 0;

    /// <summary>
    /// How many times a suggested point of interest has been walked all the way to. The
    /// room-interest owner watches this rather than polling positions, so "arrived" means
    /// exactly what the walk itself decided.
    /// </summary>
    public int RoomInterestArrivals { get; private set; }

    /// <summary>Whether the arrival gaze is still holding, and where it points.</summary>
    public bool HasRoomGaze => _roomGazeTicksRemaining > 0;
    public Vector2 RoomGazePoint { get; private set; }

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

    /// <summary>
    /// Suggests a temporary low-priority point of interest in room/world coordinates. The
    /// normal seeded planner keeps authority over jumps and any walk it has already committed to;
    /// the suggestion only turns an otherwise-idle ambient tick into a short walk toward target.
    /// Only X steers the walk; the full point is kept so the arrival gaze has something to
    /// look at that is not necessarily at foot height.
    /// </summary>
    public void SuggestRoomInterest(Vector2 targetWorld, int durationTicks, int gazeTicks = 0)
    {
        if (!targetWorld.IsFinite())
            throw new ArgumentOutOfRangeException(nameof(targetWorld));
        if (durationTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationTicks));
        if (gazeTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(gazeTicks));

        _roomInterestTarget = targetWorld;
        _roomInterestTicksRemaining = durationTicks;
        _roomInterestGazeTicks = gazeTicks;
    }

    public void ClearRoomInterest()
    {
        _roomInterestTicksRemaining = 0;
        _roomGazeTicksRemaining = 0;
    }

    public void Reseed(ulong seed)
    {
        Seed = seed;
        _planner = new AutonomousMotionPlanner(new SeededRandomSource(seed), Profile.ToTuning());
        Intent = default;
        JumpRequestCount = 0;
        ClearRoomInterest();
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

        // Hopping an obstacle is trait-gated on purpose (DECISIONS 2026-07-20, "too random"),
        // and the threshold is 35 out of a uniform 0-100 — so roughly a third of buddies can
        // never hop anything. Those buddies had no other move: obstacles fed the hop gate but
        // never the walk planner, so a bat left lying in the path pinned them against it
        // forever (owner report 2026-08-20). After a fair while spent getting nowhere, the
        // obstacle counts as a wall and the planner turns them around. A buddy who *can* hop
        // is unaffected: he clears it long before this expires.
        float goalDirection = _planner.Goal switch
        {
            AutonomousMotionGoal.WalkLeft => -1.0f,
            AutonomousMotionGoal.WalkRight => 1.0f,
            _ => 0.0f,
        };
        bool notedForThisGoal = _notedObstacle &&
                                Mathf.Sign(_notedObstacleDirection) == Mathf.Sign(goalDirection);
        bool obstructed = enabled && canWalk && goalDirection != 0.0f &&
                          (ObstacleInCommittedPath(goalDirection) || notedForThisGoal);
        // Debounced, not sampled. Both obstacle sources flicker — the ray intermittently, and
        // the whole test drops out whenever a shove costs the buddy his footing for a few ticks
        // (canWalk goes false). A plain reset-on-clear never got past ~48 ticks of a 360-tick
        // budget, so the stuck buddy stayed stuck. The clock only rewinds once the path has
        // been genuinely clear for a while.
        if (obstructed)
        {
            ObstructedTicks++;
            _obstacleClearTicks = 0;
        }
        else if (ObstructedTicks > 0)
        {
            _obstacleClearTicks++;
            if (_obstacleClearTicks >= Profile.ObstacleClearTicks)
            {
                ObstructedTicks = 0;
                _obstacleClearTicks = 0;
            }
            else
            {
                ObstructedTicks++;
            }
        }

        bool giveUp = ObstructedTicks >= Profile.ObstacleGiveUpTicks;

        Intent = _planner.Tick(
            enabled,
            canWalk,
            canJump,
            BlockedLeft || (giveUp && goalDirection < 0.0f),
            BlockedRight || (giveUp && goalDirection > 0.0f));

        if (giveUp)
            ObstacleTurnAroundCount++;

        // A fresh goal gets a fresh budget, however it was chosen.
        if (_planner.Goal != _lastPlannedGoal)
        {
            _lastPlannedGoal = _planner.Goal;
            ObstructedTicks = 0;
            _obstacleClearTicks = 0;
        }

        ApplyRoomInterest(enabled, canWalk);
        if (Intent.JumpRequested)
        {
            JumpRequestCount++;
        }
    }

    public bool ObstacleInCommittedPath(float walkDirection)
    {
        RayCast2D? cast = walkDirection < 0.0f ? LeftObstacleCast :
            walkDirection > 0.0f ? RightObstacleCast : null;
        return cast is not null && cast.IsColliding() &&
            cast.GetCollider() is not LooseObjectBody { Profile.SoccerPlay: not null };
    }

    private void ApplyRoomInterest(bool enabled, bool canWalk)
    {
        if (_roomGazeTicksRemaining > 0)
            _roomGazeTicksRemaining--;
        if (_roomInterestTicksRemaining <= 0)
            return;

        _roomInterestTicksRemaining--;
        if (!enabled || !canWalk || Intent.Goal != AutonomousMotionGoal.Idle)
            return;

        float delta = _roomInterestTarget.X - Rig.Torso.GlobalPosition.X;
        if (Mathf.Abs(delta) <= RoomInterestArrivalPixels)
        {
            RoomInterestArrivals++;
            RoomGazePoint = _roomInterestTarget;
            _roomGazeTicksRemaining = _roomInterestGazeTicks;
            ClearRoomInterest();
            return;
        }

        float direction = Math.Sign(delta);
        if ((direction < 0.0 && BlockedLeft) || (direction > 0.0 && BlockedRight) ||
            ObstacleInCommittedPath(direction))
        {
            ClearRoomInterest();
            return;
        }

        Intent = new AutonomousMotionIntent(
            direction < 0.0f ? AutonomousMotionGoal.WalkLeft : AutonomousMotionGoal.WalkRight,
            direction,
            false,
            false);
    }

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
