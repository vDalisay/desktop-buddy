using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>Where one pooled spray droplet is in its very short life.</summary>
public enum SprayDropletState
{
    /// <summary>Parked in the pool: inert, invisible, and outside every collision layer.</summary>
    Pooled,

    /// <summary>In flight.</summary>
    Live,
}

/// <summary>
/// One pooled droplet of the Fire Sprayer's stream (M5 Task 7 plan §2.2). It copies
/// <see cref="ProjectileBody"/>'s pooling and layer discipline exactly — own pool, never a
/// loose object, never evictable, still flying after the tool is put away — and
/// deliberately <b>not</b> its damage path.
///
/// <para>A droplet's buddy contact does two things and no more: it names the part it
/// touched, and it retires. It never scores an impact, never reaches the interaction
/// pipeline as a contact source, and carries a cosmetically tiny mass so the stream pushes
/// nothing (owner default 4). Burning is the sprayer's only harm lane, and routing droplets
/// around the contact pipeline is what makes it impossible for one stream to double-dip as
/// both impact pain and burn pain.</para>
/// </summary>
[GlobalClass]
public partial class SprayDropletBody : RigidBody2D
{
    private const int ContactBufferSize = 4;

    /// <summary>Soft discs one droplet paints to read as a billow rather than a dot.</summary>
    private const int PuffCount = 3;

    private Color _flameColor = new("ff9a3c");
    private Color _coreColor = new("ffe07a");
    private Color _smokeColor = new("4a4038");
    private Vector2 _lastSample;
    private Vector2 _launchVelocity;

    public float Radius { get; private set; } = 1.5f;

    public SprayDropletState State { get; private set; } = SprayDropletState.Pooled;

    /// <summary>Routed ticks this droplet has been live.</summary>
    public int TicksInState { get; private set; }

    /// <summary>Pixels of path actually flown, accumulated per routed tick.</summary>
    public float TravelledPx { get; private set; }

    /// <summary>
    /// The buddy part this droplet landed on, or <c>null</c> if it has hit nothing (or hit
    /// only the room). Read and cleared by the owning component on its routed tick — the
    /// solver callback only observes.
    /// </summary>
    public BuddyPartId? IgnitedPart { get; private set; }

    /// <summary>Where the part contact happened, for the burn's world point.</summary>
    public Vector2 IgnitionPoint { get; private set; }

    /// <summary>True once the solver has reported any contact for this flight.</summary>
    public bool HasContact { get; private set; }

    /// <summary>Whether the droplet is drawn at all. Presentation only (reduced particles).</summary>
    public bool DrawEnabled { get; set; } = true;

    /// <summary>The velocity this droplet was launched with, for test readouts.</summary>
    public Vector2 LaunchVelocity => _launchVelocity;

    /// <summary>
    /// How far through its life this droplet is, in <c>[0, 1]</c>. Presentation only, and the
    /// one number the mist is built out of: a puff is born small and hot at the nozzle, swells
    /// as it travels, and thins to nothing at the end of the stream. The physics is unchanged
    /// — the droplet is still the same tiny circle it always was.
    /// </summary>
    public float LifeFraction { get; private set; }

    /// <summary>Shapes and configures the body once, when the pool is built.</summary>
    public void Configure(FireSprayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Radius = profile.DropletRadius;
        _flameColor = profile.FlameColor;
        _coreColor = profile.FlameCoreColor;
        _smokeColor = profile.SmokeColor;
        Mass = profile.DropletMass;
        GravityScale = profile.DropletGravityScale;
        LinearDamp = 0.0f;
        LinearDampMode = DampMode.Replace;
        AngularDamp = 0.0f;
        AngularDampMode = DampMode.Replace;
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = profile.DropletRadius } });
        // Same reason the bullet keeps it off: Godot's continuous collision replaces the
        // body's velocity to land it on the surface, which would be invisible here anyway
        // because a droplet's speed is bounded well inside the smallest part's diameter.
        ContinuousCd = CcdMode.Disabled;
        LockRotation = true;
        ContactMonitor = true;
        MaxContactsReported = ContactBufferSize;
        CanSleep = false;
        Park();
    }

    /// <summary>Puts a pooled droplet into flight. Called only from a routed tick.</summary>
    public void Launch(Vector2 position, Vector2 velocity)
    {
        _lastSample = position;
        _launchVelocity = velocity;
        IgnitedPart = null;
        IgnitionPoint = Vector2.Zero;
        HasContact = false;
        TravelledPx = 0.0f;
        TicksInState = 0;
        LifeFraction = 0.0f;
        State = SprayDropletState.Live;

        Freeze = false;
        Sleeping = false;
        GlobalPosition = position;
        Rotation = 0.0f;
        LinearVelocity = velocity;
        AngularVelocity = 0.0f;
        CollisionLayer = CollisionLayers.Projectiles;
        // Buddy parts and the room only. A droplet must never disturb a loose object or
        // another droplet: the stream pushes nothing.
        CollisionMask = CollisionLayers.RoomBounds | CollisionLayers.BuddyParts;
        Visible = DrawEnabled;
        ResetPhysicsInterpolation();
        QueueRedraw();
    }

    /// <summary>
    /// Advances this droplet's own bookkeeping on the owning component's routed tick and
    /// reports whether it is finished. Unlike a bullet there is no settle or linger window:
    /// a droplet resolves nothing through the pipeline, so the tick that sees its contact
    /// is the tick it can go.
    /// </summary>
    public bool Advance(int lifetimeTicks, float maxTravelPx)
    {
        if (State == SprayDropletState.Pooled)
            return false;

        TicksInState++;
        TravelledPx += GlobalPosition.DistanceTo(_lastSample);
        _lastSample = GlobalPosition;
        // Whichever bound this droplet is going to reach first is the one the puff should
        // dissipate against, so the stream thins out where it really ends.
        float byTicks = TicksInState / (float)Math.Max(2, lifetimeTicks);
        float byTravel = maxTravelPx > 0.0f ? TravelledPx / maxTravelPx : 0.0f;
        LifeFraction = Math.Clamp(Math.Max(byTicks, byTravel), 0.0f, 1.0f);
        QueueRedraw();

        return HasContact ||
               TicksInState >= Math.Max(2, lifetimeTicks) ||
               TravelledPx >= maxTravelPx;
    }

    /// <summary>Returns the droplet to the pool, inert and out of the way.</summary>
    public void Park()
    {
        State = SprayDropletState.Pooled;
        TicksInState = 0;
        TravelledPx = 0.0f;
        LifeFraction = 0.0f;
        HasContact = false;
        IgnitedPart = null;
        IgnitionPoint = Vector2.Zero;
        _launchVelocity = Vector2.Zero;
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
        if (State != SprayDropletState.Live)
            return;

        // Observation only: what a droplet's contact means is decided on the owning
        // component's routed tick, never inside a solver callback.
        int contactCount = state.GetContactCount();
        for (int index = 0; index < contactCount; index++)
        {
            HasContact = true;
            if (IgnitedPart is not null)
                continue;

            if (state.GetContactColliderObject(index) is PuppetPartBody part)
            {
                IgnitedPart = part.PartId;
                IgnitionPoint = state.GetContactColliderPosition(index);
            }
        }
    }

    public override void _Draw()
    {
        if (State != SprayDropletState.Live || !DrawEnabled)
            return;

        // The legacy counterpart of the 3D mist (owner feedback 2026-08-01). It used to be a
        // hard streak and a bright dot, which read as discrete pellets rather than as fire.
        // Now each droplet paints a small stack of soft, semi-transparent puffs strung back
        // along its own flight path: they swell and cool as the droplet ages, so overlapping
        // droplets blend into one billowing, smoky column instead of a dotted line.
        //
        // Purely presentation. The collider is still the same tiny circle, the fan geometry
        // is untouched, and nothing here is read by the ignition path.
        Vector2 velocity = LinearVelocity.Length() > 1.0f ? LinearVelocity : _launchVelocity;
        Vector2 forward = velocity == Vector2.Zero ? Vector2.Right : velocity.Normalized();
        float life = Mathf.Clamp(LifeFraction, 0.0f, 1.0f);
        // Swells fast at first and keeps spreading; a linear growth reads as a wedge rather
        // than as a plume.
        float swell = 1.0f + (5.5f * Mathf.Sqrt(life));
        // Hot at the nozzle, sooty at the end of its reach.
        Color tint = _flameColor.Lerp(_smokeColor, life);
        float fade = (1.0f - (life * life)) * 0.34f;

        for (int puff = 0; puff < PuffCount; puff++)
        {
            float along = puff / (float)PuffCount;
            float size = Radius * swell * (1.0f - (along * 0.45f));
            Vector2 centre = -forward * (Radius * swell * 1.5f * along);
            DrawCircle(
                centre,
                size,
                new Color(tint, fade * (1.0f - (along * 0.55f))),
                true,
                -1.0f,
                true);
        }

        // The hot core survives only near the nozzle, which is what keeps the stream from
        // reading as plain smoke.
        float coreStrength = 1.0f - Mathf.Min(1.0f, life * 2.2f);
        if (coreStrength > 0.0f)
        {
            DrawCircle(
                Vector2.Zero,
                Radius * (1.0f + (1.6f * coreStrength)),
                new Color(_coreColor, 0.75f * coreStrength),
                true,
                -1.0f,
                true);
        }
    }
}
