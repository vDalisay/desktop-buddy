using System;
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
/// Authoritative physics-clock worker for the Grenade's pin, fuse, and blast (M5 Task 6).
///
/// <para>The grenade is an ordinary launchable: the spawn key places it, the Grab tether
/// carries it, and <see cref="PullbackLauncherComponent"/> aims and throws it on the same
/// chord as the Baseball. This component adds only the three things a ball has no concept
/// of — the pin comes out on the first secondary press, the fuse runs in routed ticks once
/// the player lets go, and the detonation puts an impulse through the shared pain
/// pipeline.</para>
///
/// <para><b>Nothing here scales damage.</b> The blast hands
/// <see cref="InteractionDamageComponent.ApplyBlastImpulse"/> an equivalent impulse shaped
/// only by distance falloff, and the shared curve turns that into pain exactly as it does
/// for a bat or a bullet (`DECISIONS.md`, the no-per-tool-multiplier rule).</para>
///
/// <para>One grenade at a time, because the launcher's spawn policy already replaces every
/// loose object when it places a new one. That is a spawn policy rather than a cap, and it
/// is the Baseball's behaviour unchanged.</para>
/// </summary>
[GlobalClass]
public partial class GrenadeComponent : Node2D
{
    /// <summary>Slack on the floor test, in px — the same tolerance the registry uses.</summary>
    private const float GroundContactTolerance = 2.0f;

    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public GrabTetherController Grab { get; set; } = null!;
    [Export] public LooseObjectRegistry Registry { get; set; } = null!;
    [Export] public PullbackLauncherComponent Launcher { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;
    [Export] public GrenadeProfile Profile { get; set; } = null!;

    private readonly PinBody[] _pins = new PinBody[GrenadeProfile.PinPoolCapacity];

    private Action<LooseObjectBody>? _despawn;
    private LooseObjectBody? _tracked;
    private GrenadeFusePhase _phase = GrenadeFusePhase.Fresh;
    private bool _pendingPinPull;
    private float _previousSpeed;
    private bool _wasOnFloor;
    private int _ticksSinceThud;

    public bool IsInitialized { get; private set; }

    /// <summary>
    /// The pooled cosmetic pins, for a presenter that draws them. Exposed read-only: a
    /// presenter may look at where a pin is and whether it is live, and may not drop,
    /// park, or move one — those are this component's, on the routed tick.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<PinBody> Pins => _pins;

    /// <summary>The grenade this component is following, or <c>null</c>.</summary>
    public LooseObjectBody? Tracked =>
        GodotObject.IsInstanceValid(_tracked) && _tracked!.RuntimeId != 0 ? _tracked : null;

    public GrenadeFuseStage Stage => _phase.Stage;
    public int FuseTicksRemaining => _phase.TicksRemaining;
    public bool PinIsOut => _phase.PinIsOut;
    public bool IsCountingDown => _phase.IsCountingDown;

    // Telemetry consumed by scenarios and the laboratory panel.
    public int PinDropCount { get; private set; }
    public int DetonationCount { get; private set; }
    public int ActivePinCount { get; private set; }
    public int ThudCount { get; private set; }
    public float LastThudSpeed { get; private set; }
    public Vector2 LastBlastCenter { get; private set; }

    /// <summary>Buddy parts the last blast scored positive pain on.</summary>
    public int LastBlastScoredParts { get; private set; }

    /// <summary>Total pain the last blast put through the shared curve.</summary>
    public float LastBlastPain { get; private set; }

    /// <summary>Dynamic bodies the last blast shoved, the grenade itself excluded.</summary>
    public int LastBlastShovedBodies { get; private set; }

    /// <summary>Fired on the tick the pin leaves the grenade, at the grenade's position.</summary>
    public event Action<Vector2>? PinPulled;

    /// <summary>Fired on the tick the grenade goes off, at the blast centre.</summary>
    public event Action<Vector2>? Detonated;

    /// <summary>Fired when a falling grenade lands hard enough to be heard.</summary>
    public event Action<float>? GroundContact;

    /// <param name="despawn">
    /// The root's loose-object removal, injected because taking a detonated grenade out of
    /// the world also has to release the player's grab and cancel a buddy interaction, and
    /// those are the composition root's to own — not this component's.
    /// </param>
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

    /// <summary>
    /// Queues the secondary-press pin pull. Routed through the same queued-input path as
    /// every other tool intent — the component never reads a key or a button itself.
    /// </summary>
    public void RequestPinPull() => _pendingPinPull = true;

    /// <summary>
    /// Offers a freshly created loose object to the component. A grenade is a grenade
    /// however it got into the room — the launcher's spawn key is only the usual way — so
    /// the root's loose-object factory hands every new body over and this decides.
    /// </summary>
    public void NotifySpawned(LooseObjectBody body)
    {
        RequireInitialized();
        if (Tracked is not null ||
            !GodotObject.IsInstanceValid(body) ||
            body.SemanticContentId != ContentIds.ToolGrenade)
        {
            return;
        }

        Adopt(body);
    }

    /// <summary>Consumes queued intent and advances the fuse on the root's physics clock.</summary>
    public void PhysicsTick()
    {
        RequireInitialized();
        AdvancePins();
        AdoptNewGrenade();

        LooseObjectBody? body = Tracked;
        if (body is null)
        {
            _tracked = null;
            _phase = GrenadeFusePhase.Fresh;
            _pendingPinPull = false;
            return;
        }

        bool pinPullRequested = _pendingPinPull;
        _pendingPinPull = false;

        GrenadeFuseResult result = GrenadeFuseMachine.Tick(
            new GrenadeFuseInput(
                _phase,
                pinPullRequested,
                PlayerControls(body),
                Profile.ToFuseConstants()));
        _phase = result.Phase;

        if (result.PinPulled)
        {
            DropPin(body);
            PinDropCount++;
            PinPulled?.Invoke(body.GlobalPosition);
        }

        if (_phase.IsCountingDown)
        {
            // A live fuse must never be evicted to make room for something else: the
            // player is owed the explosion they started. Re-asserted every tick rather
            // than once, because the launcher clears its own aim protection on the very
            // tick the throw releases.
            Registry.SetProtected(body, true);
        }

        TrackGroundContact(body);

        if (result.Detonated)
        {
            Detonate(body);
        }
    }

    /// <summary>Immediate recovery cleanup on the authoritative physics clock.</summary>
    public void CancelImmediately()
    {
        RequireInitialized();
        _tracked = null;
        _phase = GrenadeFusePhase.Fresh;
        _pendingPinPull = false;
        _wasOnFloor = false;
        _previousSpeed = 0.0f;
    }

    /// <summary>
    /// Picks up a freshly spawned grenade from the launcher. Adoption is a poll rather
    /// than a spawn callback because the launcher is the one place a launchable can be
    /// born, and it already publishes what it last placed.
    /// </summary>
    private void AdoptNewGrenade()
    {
        if (Tracked is not null)
            return;

        if (Launcher.CurrentLaunchableContentId != ContentIds.ToolGrenade ||
            Launcher.CurrentLaunchable is not { } candidate)
        {
            return;
        }

        Adopt(candidate);
    }

    private void Adopt(LooseObjectBody body)
    {
        _tracked = body;
        _phase = GrenadeFusePhase.Fresh;
        _pendingPinPull = false;
        _previousSpeed = body.LinearVelocity.Length();
        // Starts "already on the floor" so the spawn itself is never heard as a landing.
        _wasOnFloor = true;
        _ticksSinceThud = Profile.ThudMinIntervalTicks;
    }

    /// <summary>
    /// Whether the player still has hold of this grenade — by the tether or by the
    /// launcher's aim, which are the same fact to the fuse: control has not been let go.
    /// A buddy holding it is deliberately <b>not</b> control; a caught live grenade goes
    /// off in the buddy's hands.
    /// </summary>
    private bool PlayerControls(LooseObjectBody body)
    {
        if (Launcher.AimedBody == body)
            return true;

        GrabState grab = Grab.CurrentGrab;
        return grab.Active && grab.Target == body;
    }

    private void TrackGroundContact(LooseObjectBody body)
    {
        if (_ticksSinceThud < int.MaxValue)
            _ticksSinceThud++;

        float floorY = Boundaries.InnerBounds.End.Y;
        bool onFloor = float.IsFinite(floorY) &&
                       body.GlobalPosition.Y + body.Radius >= floorY - GroundContactTolerance;
        // The impact speed is last tick's: by the time the body is in the floor band the
        // solver has already taken the fall out of it.
        if (onFloor && !_wasOnFloor &&
            _previousSpeed >= Profile.ThudMinImpactSpeed &&
            _ticksSinceThud >= Profile.ThudMinIntervalTicks)
        {
            ThudCount++;
            LastThudSpeed = _previousSpeed;
            _ticksSinceThud = 0;
            GroundContact?.Invoke(_previousSpeed);
        }

        _wasOnFloor = onFloor;
        _previousSpeed = body.LinearVelocity.Length();
    }

    /// <summary>
    /// Puts the blast through the two systems it belongs to and no others: the shared pain
    /// pipeline for the buddy, and the physics world for everything the shock wave can
    /// move. Cosmetic bodies — pins, magazines, projectile trails — are excluded by
    /// construction, because they are on no collision layer the query asks for.
    /// </summary>
    private void Detonate(LooseObjectBody body)
    {
        Vector2 center = body.GlobalPosition;
        int sourceId = body.InteractionId;
        LastBlastCenter = center;
        LastBlastScoredParts = 0;
        LastBlastPain = 0.0f;

        // Pain: one sample per buddy part, at its surface, through the unmodified curve.
        System.Collections.Generic.IReadOnlyList<PuppetPartBody> parts = Pipeline.Buddy.Rig.Parts;
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
        DetonationCount++;
        Detonated?.Invoke(center);

        // "Nothing left to hold": the slot is freed and the body leaves the world. The
        // root's removal also releases the player's grab and cancels a buddy interaction.
        _tracked = null;
        _phase = new GrenadeFusePhase(GrenadeFuseStage.Detonated, 0);
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

            // Straight up for anything sitting exactly on the centre, so a grenade that
            // goes off underneath something still throws it somewhere.
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

        // Away from the grenade and a little up, so the pin reads as thrown rather than
        // dropped. Deterministic: the side follows the grenade's own motion.
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
}
