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
    private const int TrajectoryDashPeriod = 3;
    private const float LandingMarkerRadius = 5.0f;

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

    /// <summary>The most recently launcher-spawned object is still live in the room.</summary>
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

    /// <summary>The content ID of the most recently spawned live launchable, or <c>null</c>.</summary>
    public string? CurrentLaunchableContentId =>
        HasLaunchable ? _spawned!.SemanticContentId : null;
    public LooseObjectBody? AimedBody => IsAiming ? _aimedBody : null;

    /// <summary>
    /// Current pullback strength, normalized 0..1. This is presentation telemetry only: launch
    /// velocity still comes from the authored pull distance and speed caps below.
    /// </summary>
    public float PullStrength => IsAiming
        ? Mathf.Clamp(
            (_bodyAnchor - _aimedBody!.GlobalPosition).Length() /
            Mathf.Max(1.0f, AimTuning.MaxPullDistance),
            0.0f,
            1.0f)
        : 0.0f;

    /// <summary>The end of the currently drawn prediction horizon in world coordinates.</summary>
    public Vector2 PredictedLandingWorldPosition => IsAiming
        ? PredictAimedWorldPosition(AimTuning.PredictionSeconds)
        : Vector2.Zero;

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
    /// Queues placement of one unheld instance of <paramref name="contentId"/> at the pointer.
    /// The profile owns whether placement first clears existing loose objects or is additive.
    /// </summary>
    public void RequestSpawn(string contentId, Vector2 worldPosition)
    {
        _pointer = worldPosition;
        _pendingSpawnContentId = contentId;
    }

    public void RequestBegin(Vector2 worldPosition)
    {
        _pointer = worldPosition;
        _pendingBegin = true;
    }

    public void RequestRelease() => _pendingRelease = true;
    public void RequestCancel() => _pendingCancel = true;

    public void CancelImmediately()
    {
        RequireInitialized();
        ClearPendingIntent();
        CancelAim();
    }

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
            if (Pipeline.Economy.IsUnlocked(requested) && FindProfile(requested) is { } profile)
                Spawn(profile);
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

        // The pull line is the physical relationship: object in the hand, original anchor at
        // the other end. Slightly thicken it with charge so a strong throw reads before release.
        float strength = PullStrength;
        DrawLine(start, anchor, tuning.PullLineColor, Mathf.Lerp(1.5f, 2.75f, strength), true);

        // Modernized Win98-style trajectory: still plain geometry, but segmented and fading so
        // it reads as a prediction rather than another piece of room art. Dashes also keep a
        // dense 24-segment arc legible over painted backgrounds.
        Vector2 previous = start;
        Vector2 current = start;
        for (int segment = 1; segment <= tuning.PredictionSegments; segment++)
        {
            float time = tuning.PredictionSeconds * segment / tuning.PredictionSegments;
            current = ToLocal(PredictAimedWorldPosition(time));
            float progress = segment / (float)tuning.PredictionSegments;
            if ((segment - 1) % TrajectoryDashPeriod != TrajectoryDashPeriod - 1)
            {
                Color segmentColor = tuning.TrajectoryColor;
                segmentColor.A *= Mathf.Lerp(0.95f, 0.28f, progress);
                float width = Mathf.Lerp(2.15f, 1.15f, progress);
                DrawLine(previous, current, segmentColor, width, true);
            }
            previous = current;
        }

        // The end of the prediction horizon gets a small target marker. It deliberately does
        // not promise collision-aware landing; it says "the object will be around here after
        // the authored preview time", matching PredictAimedWorldPosition exactly.
        Color marker = tuning.TrajectoryColor;
        marker.A *= 0.78f;
        float radius = Mathf.Lerp(LandingMarkerRadius * 0.72f, LandingMarkerRadius * 1.18f, strength);
        DrawArc(current, radius, 0.0f, Mathf.Tau, 16, marker, 1.5f, true);
        float cross = radius * 0.62f;
        DrawLine(current + Vector2.Left * cross, current + Vector2.Right * cross, marker, 1.0f, true);
        DrawLine(current + Vector2.Up * cross, current + Vector2.Down * cross, marker, 1.0f, true);
    }

    public override void _ExitTree()
    {
        _aimedBody = null;
        _spawned = null;
        _clearExistingLooseObjects = null;
    }

    private void Spawn(LooseObjectProfile profile)
    {
        _spawned = null;
        if (profile.SpawnPolicy == LooseObjectSpawnPolicy.ReplaceExisting)
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
