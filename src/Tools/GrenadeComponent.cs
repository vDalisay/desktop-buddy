using System;
using System.Collections.Generic;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Authoritative physics-clock worker for grenade pins, independent fuses and blasts.
/// Grenades remain ordinary launchable loose objects; this component observes every live grenade
/// in the registry and owns only grenade-specific state. Multiple grenades therefore share the
/// existing loose-object capacity/eviction policy without sharing one fuse.
/// </summary>
[GlobalClass]
public partial class GrenadeComponent : Node2D
{
    private const float GroundContactTolerance = 2.0f;

    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public LooseObjectRegistry Registry { get; set; } = null!;
    [Export] public PullbackLauncherComponent Launcher { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;
    [Export] public GrenadeProfile Profile { get; set; } = null!;

    private readonly PinBody[] _pins = new PinBody[GrenadeProfile.PinPoolCapacity];
    private readonly Dictionary<int, TrackedGrenadeState> _tracked = new();
    private readonly int[] _detonateBuffer = new int[LooseObjectRegistry.Capacity];

    private Action<LooseObjectBody>? _despawn;
    private bool _pendingPinPull;
    private int _primaryRuntimeId;

    public bool IsInitialized { get; private set; }
    public IReadOnlyList<PinBody> Pins => _pins;

    /// <summary>
    /// Backward-compatible primary grenade view used by the existing presenter/lab telemetry.
    /// With multiple grenades this is the most recently adopted live grenade; fuse authority is
    /// still maintained independently for every tracked runtime ID.
    /// </summary>
    public LooseObjectBody? Tracked => PrimaryState()?.Body;
    public int TrackedCount => _tracked.Count;
    public GrenadeFuseStage Stage => PrimaryState()?.Phase.Stage ?? GrenadeFuseStage.Pinned;
    public int FuseTicksRemaining => PrimaryState()?.Phase.TicksRemaining ?? 0;
    public bool PinIsOut => PrimaryState()?.Phase.PinIsOut ?? false;
    public bool IsCountingDown => PrimaryState()?.Phase.IsCountingDown ?? false;

    public int PinDropCount { get; private set; }
    public int DetonationCount { get; private set; }
    public int ActivePinCount { get; private set; }
    public int ThudCount { get; private set; }
    public float LastThudSpeed { get; private set; }
    public Vector2 LastBlastCenter { get; private set; }
    public int LastBlastScoredParts { get; private set; }
    public float LastBlastPain { get; private set; }
    public int LastBlastShovedBodies { get; private set; }

    public event Action<Vector2>? PinPulled;
    public event Action<Vector2>? Detonated;
    public event Action<float>? GroundContact;

    public void Initialize(Action<LooseObjectBody> despawn)
    {
        if (!GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(Grab) || !Grab.IsInitialized ||
            !GodotObject.IsInstanceValid(Registry) ||
            !GodotObject.IsInstanceValid(Launcher) ||
            !GodotObject.IsInstanceValid(Boundaries) ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException(
                "GrenadeComponent requires an initialized pipeline, grab, registry, launcher, boundaries, and a valid grenade profile.");
        }

        _despawn = despawn ?? throw new ArgumentNullException(nameof(despawn));
        for (int index = 0; index < _pins.Length; index++)
        {
            var pin = new PinBody { Name = $"GrenadePin_{index + 1}" };
            AddChild(pin);
            pin.Configure(Profile);
            _pins[index] = pin;
        }

        IsInitialized = true;
    }

    public void RequestPinPull() => _pendingPinPull = true;

    public void NotifySpawned(LooseObjectBody body)
    {
        RequireInitialized();
        if (!GodotObject.IsInstanceValid(body) ||
            body.RuntimeId == 0 ||
            body.SemanticContentId != ContentIds.ToolGrenade)
        {
            return;
        }

        Adopt(body);
    }

    public void PhysicsTick()
    {
        RequireInitialized();
        AdvancePins();
        ReconcileRegistryGrenades();

        LooseObjectBody? pinTarget = _pendingPinPull ? PlayerControlledGrenade() : null;
        _pendingPinPull = false;

        int detonateCount = 0;
        for (int slot = 0; slot < LooseObjectRegistry.Capacity; slot++)
        {
            LooseObjectBody? body = Registry.BodyAt(slot);
            if (!IsLiveGrenade(body) || !_tracked.TryGetValue(body!.RuntimeId, out TrackedGrenadeState? state))
                continue;

            bool pinPullRequested = body == pinTarget;
            TickFlameCook(state);
            (bool struckPinPull, bool forcedDetonation) = ConsumeStrikeFlags(state);
            GrenadeFuseResult result = GrenadeFuseMachine.Tick(
                new GrenadeFuseInput(
                    state.Phase,
                    pinPullRequested,
                    PlayerControls(body),
                    Profile.ToFuseConstants(),
                    struckPinPull,
                    forcedDetonation));
            state.Phase = result.Phase;

            if (result.PinPulled)
            {
                DropPin(body);
                PinDropCount++;
                PinPulled?.Invoke(body.GlobalPosition);
            }

            if (state.Phase.IsCountingDown)
                Registry.SetProtected(body, true);

            TrackGroundContact(state);

            if (result.Detonated && detonateCount < _detonateBuffer.Length)
                _detonateBuffer[detonateCount++] = body.RuntimeId;
        }

        // Despawn mutates registry slots, so detonate only after registry enumeration completes.
        for (int index = 0; index < detonateCount; index++)
        {
            int runtimeId = _detonateBuffer[index];
            if (_tracked.TryGetValue(runtimeId, out TrackedGrenadeState? state) &&
                IsLiveGrenade(state.Body))
            {
                Detonate(state.Body);
            }
        }
    }

    public void CancelImmediately()
    {
        RequireInitialized();
        foreach (TrackedGrenadeState state in _tracked.Values)
        {
            if (IsLiveGrenade(state.Body))
                Registry.SetProtected(state.Body, false);
        }
        _tracked.Clear();
        _primaryRuntimeId = 0;
        _pendingPinPull = false;
    }

    private void ReconcileRegistryGrenades()
    {
        for (int slot = 0; slot < LooseObjectRegistry.Capacity; slot++)
        {
            LooseObjectBody? body = Registry.BodyAt(slot);
            if (IsLiveGrenade(body))
                Adopt(body!);
        }

        if (_tracked.Count == 0)
        {
            _primaryRuntimeId = 0;
            return;
        }

        Span<int> stale = stackalloc int[LooseObjectRegistry.Capacity];
        int staleCount = 0;
        foreach ((int runtimeId, TrackedGrenadeState state) in _tracked)
        {
            if (!IsLiveGrenade(state.Body) || Registry.FindBody(runtimeId) != state.Body)
                stale[staleCount++] = runtimeId;
        }
        for (int index = 0; index < staleCount; index++)
            _tracked.Remove(stale[index]);

        if (_primaryRuntimeId != 0 && !_tracked.ContainsKey(_primaryRuntimeId))
            _primaryRuntimeId = 0;
    }

    private void Adopt(LooseObjectBody body)
    {
        if (_tracked.ContainsKey(body.RuntimeId))
            return;

        _tracked.Add(body.RuntimeId, new TrackedGrenadeState
        {
            Body = body,
            Phase = GrenadeFusePhase.Fresh,
            PreviousSpeed = body.LinearVelocity.Length(),
            WasOnFloor = true,
            TicksSinceThud = Profile.ThudMinIntervalTicks,
        });
        _primaryRuntimeId = body.RuntimeId;
    }

    private TrackedGrenadeState? PrimaryState()
    {
        if (_primaryRuntimeId != 0 && _tracked.TryGetValue(_primaryRuntimeId, out TrackedGrenadeState? primary) &&
            IsLiveGrenade(primary.Body))
        {
            return primary;
        }

        foreach (TrackedGrenadeState state in _tracked.Values)
        {
            if (IsLiveGrenade(state.Body))
            {
                _primaryRuntimeId = state.Body.RuntimeId;
                return state;
            }
        }

        _primaryRuntimeId = 0;
        return null;
    }

    private LooseObjectBody? PlayerControlledGrenade()
    {
        if (Launcher.AimedBody is LooseObjectBody aimed && IsLiveGrenade(aimed))
            return aimed;

        GrabState grab = Grab.CurrentGrab;
        return grab.Active && grab.Target is LooseObjectBody body && IsLiveGrenade(body)
            ? body
            : null;
    }

    private bool PlayerControls(LooseObjectBody body)
    {
        if (Launcher.AimedBody == body)
            return true;

        GrabState grab = Grab.CurrentGrab;
        return grab.Active && grab.Target == body;
    }

    private static bool IsLiveGrenade(LooseObjectBody? body) =>
        GodotObject.IsInstanceValid(body) &&
        body!.RuntimeId != 0 &&
        body.SemanticContentId == ContentIds.ToolGrenade;

    private void TrackGroundContact(TrackedGrenadeState state)
    {
        LooseObjectBody body = state.Body;
        if (state.TicksSinceThud < int.MaxValue)
            state.TicksSinceThud++;

        float floorY = Boundaries.InnerBounds.End.Y;
        bool onFloor = float.IsFinite(floorY) &&
                       body.GlobalPosition.Y + body.Radius >= floorY - GroundContactTolerance;
        if (onFloor && !state.WasOnFloor &&
            state.PreviousSpeed >= Profile.ThudMinImpactSpeed &&
            state.TicksSinceThud >= Profile.ThudMinIntervalTicks)
        {
            ThudCount++;
            LastThudSpeed = state.PreviousSpeed;
            state.TicksSinceThud = 0;
            GroundContact?.Invoke(state.PreviousSpeed);
        }

        state.WasOnFloor = onFloor;
        state.PreviousSpeed = body.LinearVelocity.Length();
    }

    private void Detonate(LooseObjectBody body)
    {
        Vector2 center = body.GlobalPosition;
        int sourceId = body.InteractionId;
        LastBlastCenter = center;
        LastBlastScoredParts = 0;
        LastBlastPain = 0.0f;

        IReadOnlyList<PuppetPartBody> parts = Pipeline.Buddy.Rig.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            PuppetPartBody part = parts[index];
            float distance = Mathf.Max(
                0.0f, center.DistanceTo(part.GlobalPosition) - part.Radius);
            float impulse = Profile.EquivalentImpulseAtCenter * Profile.FalloffAt(distance);
            if (impulse <= 0.0f)
                continue;

            float pain = Pipeline.ApplyBlastImpulse(
                sourceId,
                ContentIds.ToolGrenade,
                (BuddyPart)(int)part.PartId,
                impulse,
                part.GlobalPosition);
            if (pain > 0.0f)
            {
                LastBlastScoredParts++;
                LastBlastPain += pain;
            }
        }

        LastBlastShovedBodies = ApplyRadialShove(center, body);
        // Before the tracking entry goes away, so the source cannot re-arm itself.
        ChainNeighbours(body, center);
        DetonationCount++;
        Detonated?.Invoke(center);

        int runtimeId = body.RuntimeId;
        _tracked.Remove(runtimeId);
        if (_primaryRuntimeId == runtimeId)
            _primaryRuntimeId = 0;
        _despawn!(body);
    }

    private int ApplyRadialShove(Vector2 center, LooseObjectBody exclude)
    {
        if (Profile.ShoveImpulseAtCenter <= 0.0f)
            return 0;

        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        if (space is null)
            return 0;

        var shape = new CircleShape2D { Radius = Profile.BlastZeroRadiusPx };
        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = shape,
            Transform = new Transform2D(0.0f, center),
            CollisionMask = CollisionLayers.BuddyParts | CollisionLayers.LooseObjects,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };

        Godot.Collections.Array<Godot.Collections.Dictionary> hits =
            space.IntersectShape(query, LooseObjectRegistry.Capacity + 16);
        shape.Dispose();

        int shoved = 0;
        foreach (Godot.Collections.Dictionary hit in hits)
        {
            if (!hit.TryGetValue("collider", out Variant value) ||
                value.AsGodotObject() is not RigidBody2D target ||
                target == exclude ||
                target.Freeze)
            {
                continue;
            }

            Vector2 away = target.GlobalPosition - center;
            float distance = away.Length();
            float falloff = Profile.FalloffAt(distance);
            if (falloff <= 0.0f)
                continue;

            Vector2 direction = distance > 0.001f ? away / distance : Vector2.Up;
            target.ApplyCentralImpulse(direction * (Profile.ShoveImpulseAtCenter * falloff));
            shoved++;
        }

        return shoved;
    }

    private void DropPin(LooseObjectBody body)
    {
        PinBody? pin = null;
        foreach (PinBody candidate in _pins)
        {
            if (!candidate.IsLive)
            {
                pin = candidate;
                break;
            }
        }

        if (pin is null)
            return;

        float side = body.LinearVelocity.X >= 0.0f ? -1.0f : 1.0f;
        pin.Drop(
            body.GlobalPosition + new Vector2(side * (body.Radius + 3.0f), -2.0f),
            new Vector2(side * Profile.PinEjectSpeed, -Profile.PinEjectSpeed * 0.45f),
            side * 9.0f,
            Profile.PinLingerTicks);
        ActivePinCount++;
    }

    private void AdvancePins()
    {
        foreach (PinBody pin in _pins)
        {
            if (pin.Advance())
            {
                pin.Park();
                ActivePinCount = Mathf.Max(0, ActivePinCount - 1);
            }
        }
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("GrenadeComponent used before initialization.");
    }

    private sealed class TrackedGrenadeState
    {
        public required LooseObjectBody Body { get; init; }
        public GrenadeFusePhase Phase { get; set; } = GrenadeFusePhase.Fresh;
        public float PreviousSpeed { get; set; }
        public bool WasOnFloor { get; set; }
        public int TicksSinceThud { get; set; }

        /// <summary>Outside-the-fuse state; see <see cref="GrenadeComponent.NotifyStruck"/>.</summary>
        public bool StruckPinPull { get; set; }
        public bool ForcedDetonation { get; set; }
        public bool ChainedByBlast { get; set; }
        public int PistolHits { get; set; }
        public float HeatTicks { get; set; }
    }
}
