using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>Where one pooled projectile is in its short life.</summary>
public enum ProjectileState
{
    /// <summary>Parked in the pool: inert, invisible, and outside every collision layer.</summary>
    Pooled,

    /// <summary>In flight.</summary>
    Live,

    /// <summary>
    /// Hit something and stopped, but deliberately still a valid instance. The
    /// interaction pipeline resolves a contact's attribution on the routed tick after
    /// the solver produced it, so a projectile freed on impact would lose its own pain.
    /// </summary>
    Spent,
}

/// <summary>
/// One pooled projectile fired by a cursor gun (RAGDOLL §9.2). It lives on the
/// Projectiles layer, so it strikes buddy parts, loose objects, and room bounds but
/// never another projectile or a physical tool, and it is <b>never</b> registered with
/// the <see cref="Objects.LooseObjectRegistry"/>: bullets are bounded by their own pool
/// and never consume one of the 24 loose-object slots (RAGDOLL §10, ARCHITECTURE §15).
///
/// <para>It cannot pass through what it is fired at, but not because of the engine's
/// continuous-collision setting — see <see cref="GunProfile.MaximumTravelPerTickPx"/>
/// for what really guarantees that, and for the measurements behind it.</para>
///
/// <para>Pain comes from the impulse the solver measures when a shot really connects,
/// through the shared curve, with the firing tool's content ID as attribution. The
/// body owns no gameplay decisions: lifetime, travel, and recycling are driven from
/// the owning component's routed tick.</para>
///
/// <para><see cref="InteractionId"/> is re-minted on every launch. A pooled instance
/// that reused one identity would let its second shot be swallowed as a continuation
/// of its first shot's contact episode, on the same part, inside the router's re-arm
/// window.</para>
///
/// <para>A multi-projectile shot is the one exception, and it is deliberate: every pellet
/// of one trigger pull is launched with the <b>same</b> shared identity, so the router's
/// <c>(SourceInteractionId, TargetPartId)</c> episode key makes six pellets into one part
/// a single scored impact rather than six. A shotgun's damage therefore comes from
/// coverage — pellets across N parts open N episodes — which is the owner-accepted dedup
/// interpretation recorded in DECISIONS for M5 Task 9.</para>
/// </summary>
[GlobalClass]
public partial class ProjectileBody : RigidBody2D, IImpactSource
{
    private const int ContactBufferSize = 4;
    private const float MinimumStreakSpeed = 1.0f;

    private Color _fillColor = new("ffe08a");
    private Color _trailColor = new("ffb347");
    private bool _emitsImpactSmoke = true;
    private Color _impactSmokeColor = new(0.50f, 0.52f, 0.55f, 0.42f);
    private string _contentId = ContentIds.ToolPistol;
    private Vector2 _lastSample;
    private Vector2 _launchVelocity;
    private Vector2 _approachVelocity;
    private Vector2 _headingBeforeLastStep;
    private Vector2 _positionBeforeLastStep;
    private Vector2 _lastIntegratedPosition;
    private bool _contactObserved;
    private Vector2 _impactPosition;
    private Vector2 _impactHeading;
    private int _contactTicks;
    private float _deliveredImpulse;
    private ulong _hitBodyId;
    private bool _shoveDelivered;
    private bool _hitReported;

    public float Radius { get; private set; } = 2.0f;

    public int InteractionId { get; private set; } = InteractionIds.Next();

    public string ContentId => _contentId;

    public ProjectileState State { get; private set; } = ProjectileState.Pooled;

    /// <summary>Routed ticks this projectile has been live, or spent.</summary>
    public int TicksInState { get; private set; }

    /// <summary>
    /// Pixels of path actually flown, accumulated per routed tick. Path length rather
    /// than distance from the muzzle, so the authored bound stays meaningful for a shot
    /// that deflected instead of flying straight.
    /// </summary>
    public float TravelledPx { get; private set; }

    /// <summary>True once the solver has reported a contact for this flight.</summary>
    public bool HasHit => _contactObserved;

    /// <summary>The velocity this projectile was launched with, for test readouts.</summary>
    public Vector2 LaunchVelocity => _launchVelocity;

    /// <summary>
    /// Where this flight began. Kept because it is the only honest answer to "did the
    /// round come out of the barrel the player can see": recovering it later from the
    /// current position means guessing how many steps have been integrated since.
    /// </summary>
    public Vector2 LaunchPosition { get; private set; }

    /// <summary>
    /// The orientation this flight actually began at, snapshotted before any physics step
    /// could add to it. A recycled pool slot must not inherit the orientation of the shot
    /// before it, and one tick later this is unreadable: an impact spins the body.
    /// </summary>
    public float LaunchRotation { get; private set; }

    /// <summary>
    /// World-space direction the drawn streak points along. Exposed so a scenario can
    /// prove the visual is really glued to the flight path: the streak is drawn in this
    /// body's local space, so any body rotation swings the drawn shot away from the
    /// direction it is actually travelling.
    /// </summary>
    public Vector2 VisualForward => WorldStreakForward();

    /// <summary>
    /// Where along its own path the shot got to. The body's position at the step a contact
    /// becomes visible has already been shoved off the flight line by the collision - nine
    /// to fourteen pixels sideways on an ordinary hit - so drawing the round there flicks it
    /// off its own line on the one frame the player reads the hit (owner report 2026-08-26).
    /// The travel since the last clean step is projected back onto the heading, which keeps
    /// how far it got into what it hit and drops the sideways kick.
    /// </summary>
    private Vector2 ImpactPointOnTheFlightLine(Vector2 resolvedPosition)
    {
        if (_positionBeforeLastStep == Vector2.Zero || _impactHeading == Vector2.Zero)
            return resolvedPosition;

        Vector2 heading = _impactHeading.Normalized();
        float along = (resolvedPosition - _positionBeforeLastStep).Dot(heading);
        return _positionBeforeLastStep + (heading * Mathf.Max(0.0f, along));
    }

    /// <summary>
    /// The heading the shot was really carrying when it struck - the last read taken before
    /// the collision bent it. Everything that means "the way the shot was going" wants this
    /// one, not the live velocity, which by the time a contact is visible has already been
    /// turned by the impact.
    /// </summary>
    internal Vector2 ImpactHeading =>
        _impactHeading != Vector2.Zero ? _impactHeading : _approachVelocity;

    /// <summary>
    /// Where in the world the shot is actually drawn. Once it has connected this is the
    /// point it struck, not the body's current position: the body keeps being simulated
    /// through the settling window and the drawing no longer follows it.
    /// </summary>
    public Vector2 VisualOrigin => ToGlobal(LocalDrawOrigin());

    /// <summary>
    /// The largest contact impulse the solver has actually applied to this projectile.
    /// A continuous-collision hit is detected a step before it is resolved, so this —
    /// not the bare contact — is what says the shot has landed.
    /// </summary>
    public float DeliveredImpulse => _deliveredImpulse;

    /// <summary>Shapes and configures the body once, when the pool is built.</summary>
    public void Configure(GunProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Radius = profile.ProjectileRadius;
        _fillColor = profile.ProjectileColor;
        _trailColor = profile.TrailColor;
        _emitsImpactSmoke = profile.EmitsImpactSmoke;
        _impactSmokeColor = profile.ImpactSmokeColor;
        _contentId = profile.ContentId;
        Mass = profile.ProjectileMass;
        GravityScale = profile.ProjectileGravityScale;
        LinearDamp = 0.0f;
        LinearDampMode = DampMode.Replace;
        AngularDamp = 0.0f;
        AngularDampMode = DampMode.Replace;
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = profile.ProjectileRadius } });
        // Godot's own continuous collision is deliberately OFF: it prevents tunneling by
        // *replacing the body's velocity* with the reduced velocity that lands it on the
        // surface it was about to cross, so the shot stops in the right place carrying
        // almost no momentum, the solver reports a correspondingly tiny impulse, and a
        // visibly perfect hit does no harm at all. What guarantees this body cannot skip
        // its target instead is GunProfile.MaximumTravelPerTickPx — read that first if you
        // are about to turn this back on.
        ContinuousCd = CcdMode.Disabled;
        // Rotation is deliberately LEFT FREE, and this is not an oversight. An off-centre
        // hit does spin a bullet — 121 degrees while still visible, measured 2026-07-31 —
        // but that is invisible on a round body drawn along its own velocity, and it is
        // not free to take away: `LockRotation = true` halved the contact impulse the
        // shared pain pipeline scores, from 1187 to 598 on the same seeded point-blank
        // head shot, quietly cutting every gun's damage in half. A projectile's spin-up is
        // part of the impulse this project measures pain from, so the alignment fix belongs
        // in the drawing (see LocalStreakForward), never in the body.
        LockRotation = false;
        ContactMonitor = true;
        MaxContactsReported = ContactBufferSize;
        CanSleep = false;
        Park();
    }

    /// <summary>
    /// Puts a pooled projectile into flight. Called only from a routed tick.
    ///
    /// <para><paramref name="sharedInteractionId"/> is the identity every pellet of one
    /// multi-projectile shot is stamped with. Left <c>null</c> — which is what the
    /// single-projectile path passes — the flight mints its own identity exactly as it
    /// always did.</para>
    /// </summary>
    public void Launch(Vector2 position, Vector2 velocity, int? sharedInteractionId = null)
    {
        InteractionId = sharedInteractionId ?? InteractionIds.Next();
        LaunchPosition = position;
        _lastSample = position;
        _launchVelocity = velocity;
        _approachVelocity = velocity;
        _contactObserved = false;
        _impactPosition = Vector2.Zero;
        _impactHeading = Vector2.Zero;
        _headingBeforeLastStep = Vector2.Zero;
        _positionBeforeLastStep = Vector2.Zero;
        _lastIntegratedPosition = Vector2.Zero;
        _contactTicks = 0;
        _deliveredImpulse = 0.0f;
        _hitBodyId = 0;
        _shoveDelivered = false;
        _hitReported = false;
        LastShoveImpulse = 0.0f;
        TravelledPx = 0.0f;
        TicksInState = 0;
        State = ProjectileState.Live;

        Freeze = false;
        Sleeping = false;
        // Rotation is cleared with the rest of the transform rather than carried over: a
        // reused pool slot starts every shot square, so it reads as this flight's own spin.
        PooledBodyPlacement.Launch(this, position, 0.0f, velocity, 0.0f);
        LaunchRotation = Rotation;
        CollisionLayer = CollisionLayers.Projectiles;
        CollisionMask = CollisionLayers.MaskProjectiles;
        Visible = true;
        QueueRedraw();
    }

    /// <summary>
    /// Advances this projectile's own bookkeeping on the owning component's routed
    /// tick and reports whether it is ready to return to the pool.
    /// </summary>
    public bool Advance(
        int lifetimeTicks,
        float maxTravelPx,
        int contactSettleTicks,
        int spentLingerTicks)
    {
        if (State == ProjectileState.Pooled)
            return false;

        TicksInState++;
        if (State == ProjectileState.Spent)
            return TicksInState >= Math.Max(1, spentLingerTicks);

        TravelledPx += GlobalPosition.DistanceTo(_lastSample);
        _lastSample = GlobalPosition;
        // The streak is drawn along the direction of travel, and a deflected shot changes
        // that direction, so a live projectile is redrawn every tick it flies.
        QueueRedraw();

        // A projectile that connected keeps its physics for a short settling window
        // before it is taken out of the world, and that window is load-bearing: the
        // solver spreads one impact over several steps, and the first step it reports can
        // be a touch of almost no impulse. Withdrawing on that first report stopped the
        // bullet dead before the real impact resolved, and the shot visibly connected and
        // did nothing. The shared impact router's own threshold discards the weak touches,
        // so waiting costs nothing and lets the real impulse through.
        if (_contactObserved)
        {
            _contactTicks++;
            if (_contactTicks >= Math.Max(1, contactSettleTicks))
            {
                Spend();
                return false;
            }
        }

        if (TicksInState >= Math.Max(2, lifetimeTicks) || TravelledPx >= maxTravelPx)
        {
            // An expiring projectile has hit nothing, so nothing is waiting to resolve
            // its attribution; it can be parked immediately.
            return true;
        }

        return false;
    }

    /// <summary>
    /// Stops a projectile that connected and takes it out of every collision layer,
    /// while leaving the instance valid so the pipeline can still attribute its hit.
    /// </summary>
    private void Spend()
    {
        State = ProjectileState.Spent;
        TicksInState = 0;
        CollisionLayer = 0;
        CollisionMask = 0;
        LinearVelocity = Vector2.Zero;
        AngularVelocity = 0.0f;
        Freeze = true;
        FreezeMode = FreezeModeEnum.Kinematic;
        Visible = false;
        QueueRedraw();
    }

    /// <summary>Returns the projectile to the pool, inert and out of the way.</summary>
    public void Park()
    {
        State = ProjectileState.Pooled;
        TicksInState = 0;
        TravelledPx = 0.0f;
        _contactObserved = false;
        _impactPosition = Vector2.Zero;
        _impactHeading = Vector2.Zero;
        _headingBeforeLastStep = Vector2.Zero;
        _positionBeforeLastStep = Vector2.Zero;
        _lastIntegratedPosition = Vector2.Zero;
        _contactTicks = 0;
        _deliveredImpulse = 0.0f;
        _hitBodyId = 0;
        _shoveDelivered = false;
        _hitReported = false;
        _launchVelocity = Vector2.Zero;
        _approachVelocity = Vector2.Zero;
        CollisionLayer = 0;
        CollisionMask = 0;
        LinearVelocity = Vector2.Zero;
        AngularVelocity = 0.0f;
        FreezeMode = FreezeModeEnum.Kinematic;
        Freeze = true;
        Visible = false;
        QueueRedraw();
    }

    public override void _IntegrateForces(PhysicsDirectBodyState2D state)
    {
        if (State != ProjectileState.Live)
            return;

        // The velocity the shot is carrying *into* whatever it is about to touch. Read
        // every step before the contacts are examined, because a resolved contact has
        // already reversed and shrunk it, and the shove has to push the way the shot was
        // going rather than the way it bounced.
        // ... and one step of history behind it. The contacts this step reports were
        // resolved during the last one, so by the time a contact is visible here the
        // velocity has already been bent by it - 23 degrees on a clean point-blank hit,
        // and most of a half-turn on a hard one. The step before is the last clean read.
        if (state.LinearVelocity.LengthSquared() > MinimumStreakSpeed * MinimumStreakSpeed)
        {
            _headingBeforeLastStep = _approachVelocity;
            _positionBeforeLastStep = _lastIntegratedPosition;
            _approachVelocity = state.LinearVelocity;
        }

        _lastIntegratedPosition = state.Transform.Origin;

        // Observation only: what happens to a projectile that connected is decided on
        // the owning component's routed tick, never inside a solver callback.
        int contactCount = state.GetContactCount();
        // The pose the shot struck at, latched on the first contact of this flight. The
        // body goes on being simulated for the settling window the impulse needs, and in
        // those few steps it bounces, slides and spins - 121 degrees while still visible.
        // The drawing stops following it here, so a landed shot stays put and stays
        // pointing the way it came in (owner report 2026-08-26).
        if (contactCount > 0 && !_contactObserved)
        {
            _impactHeading = _headingBeforeLastStep != Vector2.Zero
                ? _headingBeforeLastStep
                : _launchVelocity;
            _impactPosition = ImpactPointOnTheFlightLine(state.Transform.Origin);
        }

        for (int index = 0; index < contactCount; index++)
        {
            _contactObserved = true;
            float impulse = state.GetContactImpulse(index).Length();
            if (impulse > _deliveredImpulse)
                _deliveredImpulse = impulse;

            // Remember the hardest thing hit so the routed tick can shove it. An id
            // rather than the reference: a pooled projectile outlives its own contacts,
            // and a stale strong reference to a freed body is how that becomes a crash.
            if (impulse >= _deliveredImpulse &&
                state.GetContactColliderObject(index) is RigidBody2D target)
            {
                _hitBodyId = target.GetInstanceId();
            }
        }
    }

    /// <summary>
    /// Puts this projectile's authored knockback through the body it hit, on the owning
    /// component's routed tick and at most once per flight.
    ///
    /// <para>This is <b>knockback only</b>. Pain is scored from the impulse the solver
    /// itself reported, which has already happened by the time this runs; a central impulse
    /// applied here moves the target and is invisible to the pain pipeline, which is the
    /// same separation the grenade's blast shove relies on. The magnitude falls off with
    /// how far the shot flew (<see cref="GunProfile.ContactShoveAfter"/>): point blank it is
    /// authored to a grenade's worth across a whole burst, and past the far radius it is
    /// nothing at all, leaving the bare physical hit that was always there.</para>
    /// </summary>
    /// <returns>The impulse really delivered, or <c>0</c> when there was nothing to shove.</returns>
    public float TryApplyContactShove(GunProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (_shoveDelivered || !_contactObserved || _hitBodyId == 0)
            return 0.0f;

        float magnitude = profile.ContactShoveAfter(TravelledPx);
        if (magnitude <= 0.0f)
        {
            // Nothing to deliver is still a delivered shove: a projectile past the far
            // radius must not keep re-testing every tick of its settling window.
            _shoveDelivered = true;
            return 0.0f;
        }

        _shoveDelivered = true;
        if (GodotObject.InstanceFromId(_hitBodyId) is not RigidBody2D target ||
            !GodotObject.IsInstanceValid(target) ||
            target.Freeze)
        {
            return 0.0f;
        }

        if (profile.ShovesLooseObjectsOnly && target is not Objects.LooseObjectBody)
            return 0.0f;

        // The heading from before the collision bent it. Read live, this is the velocity the
        // contact has already turned - a fifth of a turn on a clean hit, most of a half-turn
        // on a hard one - so the knockback was pushed the way the shot bounced rather than
        // the way it was going, which is the one thing this is documented not to do
        // (owner instruction 2026-08-26).
        Vector2 heading = ImpactHeading != Vector2.Zero ? ImpactHeading : _launchVelocity;
        if (heading == Vector2.Zero)
            return 0.0f;

        LastShoveHeading = heading.Normalized();
        target.ApplyCentralImpulse(LastShoveHeading * magnitude);
        LastShoveImpulse = magnitude;
        return magnitude;
    }

    /// <summary>The extra knockback this flight delivered, for test readouts and telemetry.</summary>
    public float LastShoveImpulse { get; private set; }

    /// <summary>Which way that knockback pushed, for the same readouts.</summary>
    public Vector2 LastShoveHeading { get; private set; }

    /// <summary>
    /// The body this flight connected with, handed out once. The owning component drains it on
    /// its routed tick so a shot can be routed at whatever it hit — a grenade, in particular,
    /// answers to being shot (owner instruction 2026-08-21). Resolved from the instance ID for
    /// the same reason the shove is: a pooled projectile outlives its own contacts.
    /// </summary>
    public RigidBody2D? TryConsumeHitBody()
    {
        if (_hitReported || !_contactObserved || _hitBodyId == 0)
            return null;

        _hitReported = true;
        return GodotObject.InstanceFromId(_hitBodyId) is RigidBody2D target &&
            GodotObject.IsInstanceValid(target)
            ? target
            : null;
    }

    public override void _Draw()
    {
        if (State != ProjectileState.Live)
            return;

        // A short trail back along the flight direction: at these speeds a two-pixel dot
        // renders as an invisible flicker, and the streak is what reads as a shot.
        Vector2 origin = LocalDrawOrigin();
        Vector2 forward = LocalStreakForward();
        if (forward != Vector2.Zero)
            DrawLine(origin, origin - (forward * (Radius * 6.0f)), _trailColor, Radius * 1.2f, true);

        DrawCircle(origin, Radius, _fillColor, true, -1.0f, true);
    }

    /// <summary>
    /// The direction, in this body's local space, that the drawn streak runs along. One
    /// source of truth for <see cref="_Draw"/> and <see cref="VisualForward"/>.
    ///
    /// <para>Two things are corrected here, and both were reported as "the ammo doesn't
    /// line up with the gun, and it rotates while flying". It follows the velocity the
    /// body has <b>right now</b> rather than the one it was launched with, so a deflected
    /// shot is drawn along the path it is really on; and it undoes the body's own rotation,
    /// because a canvas item draws in local space and the body is free to spin (see
    /// <see cref="Configure"/> for why it must stay free). Any future projectile visual —
    /// a dart, a tracer mesh — has to be oriented from velocity the same way.</para>
    /// </summary>
    private Vector2 LocalStreakForward() => WorldStreakForward().Rotated(-Rotation);

    /// <summary>
    /// The heading the shot is drawn along, in world space. Nothing about the body's own
    /// spin enters it: the body is free to tumble (that freedom is what the pain pipeline
    /// measures its impulse from) and the drawing must not inherit a degree of it.
    /// </summary>
    private Vector2 WorldStreakForward()
    {
        Vector2 world;
        if (_contactObserved && _impactHeading != Vector2.Zero)
        {
            // Landed. It points the way it came in, and stays there: the velocity of a
            // body being resolved out of what it hit whips through every direction there
            // is, and drawing along it made a hit look like the round tumbling in place.
            world = _impactHeading;
        }
        else
        {
            Vector2 velocity = LinearVelocity;
            // Stopped, or barely moving: the launch direction is the last thing it did
            // that the player could read as a direction.
            world = velocity.Length() > MinimumStreakSpeed ? velocity : _launchVelocity;
        }

        return world == Vector2.Zero ? Vector2.Zero : world.Normalized();
    }

    /// <summary>
    /// Where the shot is drawn, in this body's own space: its centre while it flies, and
    /// the point it struck once it has landed. A canvas item draws in local space, so
    /// pinning the drawing to a world point is a matter of undoing the body's own drift.
    /// </summary>
    private Vector2 LocalDrawOrigin() =>
        _contactObserved ? ToLocal(_impactPosition) : Vector2.Zero;
}
