using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// The pin a grenade drops when the player pulls it. It is <b>cosmetic</b>, on exactly the
/// rules <see cref="MagazineBody"/> states for the pistol's ejected magazine:
/// <see cref="CollisionObject2D.CollisionLayer"/> is zero so nothing in the world can hit
/// it, and its mask is the room bounds alone so it falls, bounces, and lies there — it
/// cannot touch the buddy, cannot enter the pain pipeline, and cannot be picked up.
///
/// <para>It is deliberately <b>not</b> a <c>LooseObjectRegistry</c> object either. A pin
/// must never consume one of the FR-014 budget's 24 slots (RAGDOLL §10) — the same rule
/// projectiles and magazines follow — so it is pooled by the component that dropped it.</para>
///
/// <para>A separate type from <see cref="MagazineBody"/> rather than a shared one: the two
/// differ in shape, in what configures them, and in nothing else, and a single body that
/// took either profile would be a seam nobody asked for. If a third cosmetic ejecta ever
/// appears, that is the moment to extract the idiom.</para>
/// </summary>
[GlobalClass]
public partial class PinBody : RigidBody2D
{
    private const float RingRadius = 3.2f;
    private const float RingWidth = 1.4f;

    private Color _color = new("c9c3a6");
    private int _ticks;
    private int _lingerTicks = 480;
    private bool _legacyDrawEnabled = true;

    /// <summary>The wire ring's radius in px — what the 3D pin mesh is built from.</summary>
    public static float RingRadiusPx => RingRadius;

    public bool IsLive { get; private set; }

    /// <summary>
    /// Development telemetry proving the cosmetic body never contacts a buddy part. The
    /// collision mask makes this structurally zero; the counter lets a scenario exercise
    /// the real physics world instead of trusting configuration alone.
    /// </summary>
    public int BuddyContactCount { get; private set; }

    /// <summary>Fade applied while the pin is on its way out; 1 is fully drawn.</summary>
    public float FadeAlpha { get; private set; } = 1.0f;

    public void Configure(GrenadeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _color = profile.PinColor;
        Mass = 0.02f;
        GravityScale = 1.0f;
        // A tiny cosmetic with no gameplay impulse to preserve, so shape casting is
        // appropriate here: it prevents a long frame or a paused routing gate from ever
        // letting the pin tunnel through the thin room floor.
        ContinuousCd = CcdMode.CastShape;
        LinearDamp = 0.5f;
        AngularDamp = 1.4f;
        LinearDampMode = DampMode.Replace;
        AngularDampMode = DampMode.Replace;
        var material = new PhysicsMaterial
        {
            Bounce = 0.32f,
            Friction = 0.6f,
        };
        PhysicsMaterialOverride = material;
        // The body owns a native reference after assignment. Release this temporary C#
        // wrapper deterministically so the pooled pins cannot survive until engine
        // shutdown as leaked resource handles.
        material.Dispose();
        ContactMonitor = true;
        MaxContactsReported = 4;
        BodyEntered += OnBodyEntered;
        var shape = new CircleShape2D { Radius = RingRadius };
        var collider = new CollisionShape2D { Shape = shape };
        shape.Dispose();
        AddChild(collider);
        CanSleep = true;
        Park();
    }

    public void Drop(Vector2 position, Vector2 velocity, float spin, int lingerTicks)
    {
        _lingerTicks = Math.Max(1, lingerTicks);
        _ticks = 0;
        FadeAlpha = 1.0f;
        IsLive = true;

        Freeze = false;
        Sleeping = false;
        GlobalPosition = position;
        Rotation = 0.0f;
        LinearVelocity = velocity;
        AngularVelocity = spin;
        // Nothing may hit it; it may only hit the floor.
        CollisionLayer = 0u;
        CollisionMask = CollisionLayers.RoomBounds;
        Visible = _legacyDrawEnabled;
        ResetPhysicsInterpolation();
        QueueRedraw();
    }

    /// <summary>
    /// Whether this body draws its own flat ring. The 3D presentation turns it off and
    /// draws the pin as a mesh instead — the same one-silhouette-per-mode handover the
    /// grenade and the guns make, so the two are never on screen at once.
    /// </summary>
    public void SetLegacyDrawEnabled(bool enabled)
    {
        _legacyDrawEnabled = enabled;
        Visible = enabled && IsLive;
        QueueRedraw();
    }

    private void OnBodyEntered(Node body)
    {
        if (body is PuppetPartBody)
            BuddyContactCount++;
    }

    /// <summary>
    /// Advances the linger on the owning component's routed tick and reports whether the
    /// pin is finished. The last fifth of the linger is a fade, so it leaves rather than
    /// blinks out.
    /// </summary>
    public bool Advance()
    {
        if (!IsLive)
            return false;

        _ticks++;
        int fadeFrom = (_lingerTicks * 4) / 5;
        FadeAlpha = _ticks <= fadeFrom
            ? 1.0f
            : 1.0f - ((float)(_ticks - fadeFrom) / Math.Max(1, _lingerTicks - fadeFrom));
        QueueRedraw();
        return _ticks >= _lingerTicks;
    }

    public void Park()
    {
        IsLive = false;
        _ticks = 0;
        FadeAlpha = 1.0f;
        CollisionLayer = 0u;
        CollisionMask = 0u;
        LinearVelocity = Vector2.Zero;
        AngularVelocity = 0.0f;
        FreezeMode = FreezeModeEnum.Kinematic;
        Freeze = true;
        Visible = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!IsLive)
            return;

        var ring = new Color(_color, _color.A * Mathf.Clamp(FadeAlpha, 0.0f, 1.0f));
        DrawArc(Vector2.Zero, RingRadius, 0.0f, Mathf.Tau, 12, ring, RingWidth, true);
        // The straight leg of the pin, so it reads as a pin rather than as a bubble.
        DrawLine(Vector2.Zero, new Vector2(RingRadius * 2.2f, 0.0f), ring, RingWidth, true);
    }
}
