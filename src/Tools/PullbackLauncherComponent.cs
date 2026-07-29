using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Authoritative physics-clock worker for the Baseball's grab-assisted pullback
/// launch. Key 5 only spawns the object; the normal Grab tether owns pickup and
/// carrying. Secondary input temporarily hands that grabbed Baseball to this
/// component for trajectory preview and launch.
/// </summary>
[GlobalClass]
public partial class PullbackLauncherComponent : Node2D
{
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public LooseObjectRegistry Registry { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;
    [Export] public Node2D ObjectParent { get; set; } = null!;
    [Export] public PullbackLauncherProfile Profile { get; set; } = null!;
    [Export] public LooseObjectProfile BaseballProfile { get; set; } = null!;

    private Vector2 _pointer;
    private Vector2 _pointerAnchor;
    private Vector2 _bodyAnchor;
    private Action? _clearExistingLooseObjects;
    private bool _pendingSpawn;
    private bool _pendingBegin;
    private bool _pendingRelease;
    private bool _pendingCancel;
    private LooseObjectBody? _baseball;
    private LooseObjectBody? _aimedBody;

    public bool IsInitialized { get; private set; }
    public bool HasBall =>
        GodotObject.IsInstanceValid(_baseball) && _baseball!.RuntimeId != 0;
    public bool IsAiming =>
        GodotObject.IsInstanceValid(_aimedBody) && _aimedBody!.RuntimeId != 0;
    public bool CanAimCurrentGrab =>
        Grab is { IsInitialized: true } &&
        Grab.CurrentGrab is { Active: true, Target: LooseObjectBody body } &&
        body.RuntimeId != 0 &&
        body.SemanticContentId == ContentIds.ToolBaseball;
    public LooseObjectBody? CurrentBall => HasBall ? _baseball : null;
    public LooseObjectBody? AimedBody => IsAiming ? _aimedBody : null;
    public LooseObjectBody? LastLaunchedBody { get; private set; }
    public Vector2 LastLaunchVelocity { get; private set; }
    public int SpawnCount { get; private set; }
    public int LaunchCount { get; private set; }
    public int CancelCount { get; private set; }
    public int AdmissionFailureCount { get; private set; }

    public void Initialize(Action clearExistingLooseObjects)
    {
        if (!GodotObject.IsInstanceValid(Pipeline) ||
            !GodotObject.IsInstanceValid(Grab) || !Grab.IsInitialized ||
            !GodotObject.IsInstanceValid(Registry) ||
            !GodotObject.IsInstanceValid(Boundaries) ||
            !GodotObject.IsInstanceValid(ObjectParent) ||
            !GodotObject.IsInstanceValid(Profile) || !Profile.IsRuntimeValid ||
            !GodotObject.IsInstanceValid(BaseballProfile) || !BaseballProfile.IsRuntimeValid ||
            BaseballProfile.ContentId != ContentIds.ToolBaseball)
        {
            throw new InvalidOperationException(
                "PullbackLauncherComponent requires pipeline, grab, registry, boundaries, object parent, valid tuning, and the Baseball profile.");
        }

        _clearExistingLooseObjects = clearExistingLooseObjects ??
            throw new ArgumentNullException(nameof(clearExistingLooseObjects));
        IsInitialized = true;
    }

    public void MovePointer(Vector2 worldPosition)
    {
        _pointer = worldPosition;
        QueueRedraw();
    }

    /// <summary>
    /// Queues replacement of every loose object with one unheld Baseball at the pointer.
    /// The root-injected callback owns the room-wide one-ball spawn policy.
    /// </summary>
    public void RequestSpawn(Vector2 worldPosition)
    {
        _pointer = worldPosition;
        _pendingSpawn = true;
    }

    /// <summary>Queues secondary-button aiming for the Baseball held by Grab.</summary>
    public void RequestBegin(Vector2 worldPosition)
    {
        _pointer = worldPosition;
        _pendingBegin = true;
    }

    /// <summary>Queues release of secondary-button aiming.</summary>
    public void RequestRelease() => _pendingRelease = true;

    public void RequestCancel() => _pendingCancel = true;

    /// <summary>Immediate recovery cleanup on the authoritative physics clock.</summary>
    public void CancelImmediately()
    {
        RequireInitialized();
        ClearPendingIntent();
        CancelAim();
    }

    /// <summary>
    /// Predicts the aimed object's damped ballistic position using the same fixed
    /// cadence and initial velocity as launch. Drawing never mutates gameplay.
    /// </summary>
    public Vector2 PredictAimedWorldPosition(float seconds)
    {
        if (!IsAiming || !float.IsFinite(seconds) || seconds <= 0.0f)
            return IsAiming ? _aimedBody!.GlobalPosition : Vector2.Zero;

        float physicsHz = Mathf.Max(1.0f, Engine.PhysicsTicksPerSecond);
        float fixedStep = 1.0f / physicsHz;
        float remaining = seconds;
        Vector2 position = _aimedBody!.GlobalPosition;
        Vector2 velocity = CalculateLaunchVelocity();
        float gravity = ProjectSettings
            .GetSetting("physics/2d/default_gravity", 980.0f)
            .AsSingle() * _aimedBody.GravityScale;
        float damp = Mathf.Max(0.0f, _aimedBody.LinearDamp);
        while (remaining > 0.0f)
        {
            float step = Mathf.Min(fixedStep, remaining);
            velocity.Y += gravity * step;
            velocity *= 1.0f / (1.0f + damp * step);
            position += velocity * step;
            remaining -= step;
        }

        return position;
    }

    /// <summary>Consumes queued input intent on the root-owned physics clock.</summary>
    public void PhysicsTick()
    {
        RequireInitialized();

        if (_pendingCancel || (IsAiming && !GrabStillOwnsAimedBody()))
        {
            ClearPendingIntent();
            CancelAim();
            return;
        }

        if (_pendingSpawn)
        {
            _pendingSpawn = false;
            _pendingBegin = false;
            _pendingRelease = false;
            CancelAim();
            if (Pipeline.Economy.IsUnlocked(ContentIds.ToolBaseball))
                ReplaceWithBaseball();
        }

        if (_pendingBegin)
        {
            _pendingBegin = false;
            TryBeginAiming();
        }

        if (IsAiming)
        {
            Vector2 displacement = (_pointer - _pointerAnchor)
                .LimitLength(Profile.MaxPullDistance);
            _aimedBody!.GlobalPosition =
                ClampInsideRoom(_bodyAnchor + displacement, _aimedBody.Radius);
            _aimedBody.LinearVelocity = Vector2.Zero;
            _aimedBody.AngularVelocity = 0.0f;
        }

        if (_pendingRelease)
        {
            _pendingRelease = false;
            FinishAimRelease();
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!IsAiming)
            return;

        Vector2 start = ToLocal(_aimedBody!.GlobalPosition);
        Vector2 anchor = ToLocal(_bodyAnchor);
        DrawLine(start, anchor, Profile.PullLineColor, 2.0f, true);

        Vector2 previous = start;
        for (int segment = 1; segment <= Profile.PredictionSegments; segment++)
        {
            float time = Profile.PredictionSeconds * segment / Profile.PredictionSegments;
            Vector2 current = ToLocal(PredictAimedWorldPosition(time));
            DrawLine(previous, current, Profile.TrajectoryColor, 1.5f, true);
            previous = current;
        }
    }

    public override void _ExitTree()
    {
        _aimedBody = null;
        _baseball = null;
        _clearExistingLooseObjects = null;
    }

    private void ReplaceWithBaseball()
    {
        _baseball = null;
        _clearExistingLooseObjects!();

        Vector2 spawn = ClampInsideRoom(_pointer, BaseballProfile.Radius);
        var body = new LooseObjectBody
        {
            Name = $"Baseball_{SpawnCount + 1}",
            GlobalPosition = spawn,
        };
        body.Configure(BaseballProfile);
        ObjectParent.AddChild(body);
        if (!Registry.TryRegister(body, BaseballProfile, out _))
        {
            AdmissionFailureCount++;
            body.QueueFree();
            return;
        }

        body.Sleeping = false;
        _baseball = body;
        SpawnCount++;
        QueueRedraw();
    }

    private void TryBeginAiming()
    {
        if (IsAiming || !CanAimCurrentGrab ||
            Grab.CurrentGrab.Target is not LooseObjectBody body)
        {
            return;
        }

        _aimedBody = body;
        _pointerAnchor = _pointer;
        _bodyAnchor = body.GlobalPosition;
        body.FreezeMode = RigidBody2D.FreezeModeEnum.Kinematic;
        body.Freeze = true;
        body.Sleeping = false;
        body.LinearVelocity = Vector2.Zero;
        body.AngularVelocity = 0.0f;
        Registry.SetProtected(body, true);
        QueueRedraw();
    }

    private void FinishAimRelease()
    {
        if (!IsAiming)
            return;

        if ((_bodyAnchor - _aimedBody!.GlobalPosition).Length() <
            Profile.MinimumLaunchPullDistance)
        {
            CancelAim();
            return;
        }

        LaunchAimedBody();
    }

    private void LaunchAimedBody()
    {
        LooseObjectBody body = _aimedBody!;
        Vector2 velocity = CalculateLaunchVelocity();
        Registry.SetProtected(body, false);
        body.Freeze = false;
        body.Sleeping = false;

        // Grab owns the player-held lifetime. Release it first, then establish the
        // Baseball throw token and uncapped launcher velocity authoritatively.
        Grab.Release(countsAsThrow: false);
        Registry.MarkPlayerThrown(body, ContentIds.ToolBaseball);
        body.LinearVelocity = velocity;
        body.AngularVelocity = 0.0f;

        LastLaunchedBody = body;
        LastLaunchVelocity = velocity;
        LaunchCount++;
        _aimedBody = null;
        QueueRedraw();
    }

    private void CancelAim()
    {
        if (!IsAiming)
            return;

        LooseObjectBody body = _aimedBody!;
        Registry.SetProtected(body, false);
        body.Freeze = false;
        body.Sleeping = false;
        body.LinearVelocity = Vector2.Zero;
        body.AngularVelocity = 0.0f;
        _aimedBody = null;
        CancelCount++;
        QueueRedraw();
    }

    private bool GrabStillOwnsAimedBody() =>
        IsAiming &&
        Grab.CurrentGrab is { Active: true, Target: LooseObjectBody body } &&
        body == _aimedBody;

    private Vector2 CalculateLaunchVelocity()
    {
        if (!IsAiming)
            return Vector2.Zero;

        Vector2 velocity = (_bodyAnchor - _aimedBody!.GlobalPosition) *
                           Profile.VelocityPerPullPixel;
        return velocity.LimitLength(Profile.MaxLaunchSpeed);
    }

    private Vector2 ClampInsideRoom(Vector2 position, float radius)
    {
        Rect2 bounds = Boundaries.InnerBounds;
        return new Vector2(
            Mathf.Clamp(position.X, bounds.Position.X + radius, bounds.End.X - radius),
            Mathf.Clamp(position.Y, bounds.Position.Y + radius, bounds.End.Y - radius));
    }

    private void ClearPendingIntent()
    {
        _pendingSpawn = false;
        _pendingBegin = false;
        _pendingRelease = false;
        _pendingCancel = false;
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("PullbackLauncherComponent used before initialization.");
    }
}
