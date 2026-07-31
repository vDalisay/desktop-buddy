using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Authoritative physics-clock worker for grab-assisted pullback launches. A tool's spawn key
/// only places the object; the normal Grab tether owns pickup and carrying, and secondary
/// input temporarily hands the grabbed object to this component for trajectory preview and
/// launch (DECISIONS, "M5 Baseball Input — Revised").
///
/// <para>
/// Every launchable shares this one chord — confirmed for the Baseball on 2026-07-28 and for
/// the Meal on 2026-07-29 — so a new launchable is an authored profile in
/// <see cref="LaunchableProfiles"/>, not new input code.
/// </para>
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

    /// <summary>
    /// Every object this launcher can place, keyed by its own stable content ID. Authored
    /// data, so adding the Soccer Ball or the Grenade is a `.tres` reference.
    /// </summary>
    [Export] public Godot.Collections.Array<LooseObjectProfile> LaunchableProfiles { get; set; } = new();

    private Vector2 _pointer;
    private Vector2 _pointerAnchor;
    private Vector2 _bodyAnchor;
    private Action? _clearExistingLooseObjects;
    private string? _pendingSpawnContentId;
    private bool _pendingBegin;
    private bool _pendingRelease;
    private bool _pendingCancel;
    private LooseObjectBody? _spawned;
    private LooseObjectBody? _aimedBody;

    public bool IsInitialized { get; private set; }

    /// <summary>A launcher-spawned object is live in the room.</summary>
    public bool HasLaunchable =>
        GodotObject.IsInstanceValid(_spawned) && _spawned!.RuntimeId != 0;
    public bool IsAiming =>
        GodotObject.IsInstanceValid(_aimedBody) && _aimedBody!.RuntimeId != 0;
    public bool CanAimCurrentGrab =>
        Grab is { IsInitialized: true } &&
        Grab.CurrentGrab is { Active: true, Target: LooseObjectBody body } &&
        body.RuntimeId != 0 &&
        FindProfile(body.SemanticContentId) is not null;
    public LooseObjectBody? CurrentLaunchable => HasLaunchable ? _spawned : null;

    /// <summary>The content ID of the last object this launcher spawned, or <c>null</c>.</summary>
    public string? CurrentLaunchableContentId =>
        HasLaunchable ? _spawned!.SemanticContentId : null;
    public LooseObjectBody? AimedBody => IsAiming ? _aimedBody : null;

    /// <summary>
    /// The pullback tuning in force right now: the aimed launchable's own authored preset when
    /// it has one, otherwise this launcher's shared preset. Every launchable authored before
    /// the Soccer Ball leaves <see cref="LooseObjectProfile.Launch"/> null and is pulled with
    /// the shared numbers exactly as before.
    /// </summary>
    public PullbackLauncherProfile AimTuning
    {
        get
        {
            if (IsAiming && GodotObject.IsInstanceValid(_aimedBody!.Profile) &&
                _aimedBody.Profile!.Launch is { } authored &&
                GodotObject.IsInstanceValid(authored) && authored.IsRuntimeValid)
            {
                return authored;
            }

            return Profile;
        }
    }
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
            !LaunchablesValid())
        {
            throw new InvalidOperationException(
                "PullbackLauncherComponent requires pipeline, grab, registry, boundaries, object parent, valid tuning, and at least one valid launchable profile with a unique catalogue content ID.");
        }

        _clearExistingLooseObjects = clearExistingLooseObjects ??
            throw new ArgumentNullException(nameof(clearExistingLooseObjects));
        IsInitialized = true;
    }

    private bool LaunchablesValid()
    {
        if (LaunchableProfiles.Count == 0)
            return false;

        for (int index = 0; index < LaunchableProfiles.Count; index++)
        {
            LooseObjectProfile profile = LaunchableProfiles[index];
            if (!GodotObject.IsInstanceValid(profile) || !profile.IsRuntimeValid ||
                !ContentIds.IsCatalogueEntry(profile.ContentId))
            {
                return false;
            }

            // A duplicate ID would make the spawn key ambiguous, and the launcher would
            // silently pick whichever profile came first.
            for (int other = 0; other < index; other++)
            {
                if (LaunchableProfiles[other]?.ContentId == profile.ContentId)
                    return false;
            }
        }

        return true;
    }

    private LooseObjectProfile? FindProfile(string? contentId)
    {
        if (string.IsNullOrEmpty(contentId))
            return null;

        foreach (LooseObjectProfile profile in LaunchableProfiles)
        {
            if (GodotObject.IsInstanceValid(profile) && profile.ContentId == contentId)
                return profile;
        }

        return null;
    }

    public void MovePointer(Vector2 worldPosition)
    {
        _pointer = worldPosition;
        QueueRedraw();
    }

    /// <summary>
    /// Queues replacement of every loose object with one unheld instance of
    /// <paramref name="contentId"/> at the pointer. The root-injected callback owns the
    /// room-wide one-object spawn policy.
    /// </summary>
    public void RequestSpawn(string contentId, Vector2 worldPosition)
    {
        _pointer = worldPosition;
        _pendingSpawnContentId = contentId;
    }

    /// <summary>Queues secondary-button aiming for the launchable held by Grab.</summary>
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

        if (_pendingSpawnContentId is not null)
        {
            string requested = _pendingSpawnContentId;
            _pendingSpawnContentId = null;
            _pendingBegin = false;
            _pendingRelease = false;
            CancelAim();
            // Ownership is the shop's answer, not the launcher's: an unowned tool spawns
            // nothing at all (FR-013.3).
            if (Pipeline.Economy.IsUnlocked(requested) && FindProfile(requested) is { } profile)
                ReplaceWith(profile);
        }

        if (_pendingBegin)
        {
            _pendingBegin = false;
            TryBeginAiming();
        }

        if (IsAiming)
        {
            Vector2 displacement = (_pointer - _pointerAnchor)
                .LimitLength(AimTuning.MaxPullDistance);
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
        PullbackLauncherProfile tuning = AimTuning;
        DrawLine(start, anchor, tuning.PullLineColor, 2.0f, true);

        Vector2 previous = start;
        for (int segment = 1; segment <= tuning.PredictionSegments; segment++)
        {
            float time = tuning.PredictionSeconds * segment / tuning.PredictionSegments;
            Vector2 current = ToLocal(PredictAimedWorldPosition(time));
            DrawLine(previous, current, tuning.TrajectoryColor, 1.5f, true);
            previous = current;
        }
    }

    public override void _ExitTree()
    {
        _aimedBody = null;
        _spawned = null;
        _clearExistingLooseObjects = null;
    }

    private void ReplaceWith(LooseObjectProfile profile)
    {
        _spawned = null;
        _clearExistingLooseObjects!();

        Vector2 spawn = ClampInsideRoom(_pointer, profile.Radius);
        var body = new LooseObjectBody
        {
            Name = $"Launchable_{SpawnCount + 1}",
            GlobalPosition = spawn,
        };
        body.Configure(profile);
        ObjectParent.AddChild(body);
        if (!Registry.TryRegister(body, profile, out _))
        {
            AdmissionFailureCount++;
            body.QueueFree();
            return;
        }

        body.Sleeping = false;
        _spawned = body;
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
            AimTuning.MinimumLaunchPullDistance)
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

        // Grab owns the player-held lifetime. Release it first, then establish the throw token
        // and uncapped launcher velocity authoritatively. Attribution is the launched object's
        // own ID, so pain, memory, and statistics name the tool that was actually thrown.
        Grab.Release(countsAsThrow: false);
        Registry.MarkPlayerThrown(body, body.SemanticContentId);
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

        PullbackLauncherProfile tuning = AimTuning;
        Vector2 velocity = (_bodyAnchor - _aimedBody!.GlobalPosition) *
                           tuning.VelocityPerPullPixel;
        return velocity.LimitLength(tuning.MaxLaunchSpeed);
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
        _pendingSpawnContentId = null;
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
