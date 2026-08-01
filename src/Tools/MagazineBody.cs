using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// The magazine a reloading pistol throws on the floor. It is <b>cosmetic</b>, and the
/// collision setup is the whole statement of that: <see cref="CollisionObject2D.CollisionLayer"/>
/// is zero so nothing in the world can hit it, and its mask is the room bounds alone so it
/// falls, bounces, and lies there — it cannot touch the buddy, cannot enter the pain
/// pipeline, and cannot be picked up.
///
/// <para>It is also deliberately <b>not</b> a <c>LooseObjectRegistry</c> object. Ejected
/// brass must never consume one of the FR-014 budget's 24 slots (RAGDOLL §10), which is the
/// same rule projectiles follow, so it is pooled by the gun that dropped it instead.</para>
///
/// <para>If magazines are ever meant to be picked up or thrown, that is a loose-object
/// design with slot and attribution consequences, and a new owner decision — not a change
/// to this file.</para>
/// </summary>
[GlobalClass]
public partial class MagazineBody : RigidBody2D
{
    /// <summary>The brass head a spent case is drawn with, under its authored hull colour.</summary>
    private static readonly Color BrassColor = new("c9a227");

    private Vector2 _size = new(7.0f, 12.0f);
    private Color _color = new("1c1f26");
    private int _ticks;
    private int _lingerTicks = 600;

    public bool IsLive { get; private set; }

    /// <summary>
    /// Development telemetry proving the cosmetic body never contacts a buddy part. The
    /// collision mask makes this structurally zero; keeping the counter lets the Task G
    /// scenario exercise the real physics world instead of trusting configuration alone.
    /// </summary>
    public int BuddyContactCount { get; private set; }

    /// <summary>True after the dropped body has rebounded upward from the room floor.</summary>
    public bool SawUpwardBounce { get; private set; }

    /// <summary>Fade applied while the magazine is on its way out; 1 is fully drawn.</summary>
    public float FadeAlpha { get; private set; } = 1.0f;

    /// <summary>
    /// True when this body is a spent case thrown out on a shot rather than a magazine
    /// dropped on a reload. The physics and the layer discipline are identical — only the
    /// drawn size and colour differ — because a shotgun has no magazine to drop and reusing
    /// this lane is what keeps ejected brass out of the loose-object budget.
    /// </summary>
    public bool IsCasing { get; private set; }

    public void Configure(GunProfile profile, bool asCasing = false)
    {
        ArgumentNullException.ThrowIfNull(profile);

        IsCasing = asCasing;
        _color = asCasing ? profile.CasingColor : profile.AccentColor;
        _size = asCasing
            ? new Vector2(
                profile.VisualLengthPx * profile.CasingLengthFraction * 0.42f,
                profile.VisualLengthPx * profile.CasingLengthFraction)
            : new Vector2(profile.VisualLengthPx * 0.11f, profile.VisualLengthPx * 0.20f);
        Mass = asCasing ? 0.03f : 0.08f;
        GravityScale = 1.0f;
        // This tiny cosmetic has no gameplay impulse to preserve, so shape casting is
        // appropriate here: it prevents a long frame or a paused routing gate from ever
        // letting the magazine tunnel through the thin room floor.
        ContinuousCd = CcdMode.CastShape;
        LinearDamp = 0.4f;
        AngularDamp = 1.2f;
        LinearDampMode = DampMode.Replace;
        AngularDampMode = DampMode.Replace;
        var material = new PhysicsMaterial
        {
            Bounce = 0.18f,
            Friction = 0.75f,
        };
        PhysicsMaterialOverride = material;
        // The body owns a native reference after assignment. Release this temporary C#
        // wrapper deterministically so all three pooled magazines cannot survive until
        // engine shutdown as leaked resource handles.
        material.Dispose();
        ContactMonitor = true;
        MaxContactsReported = 4;
        BodyEntered += OnBodyEntered;
        var shape = new RectangleShape2D { Size = _size };
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
        SawUpwardBounce = false;
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
        Visible = true;
        ResetPhysicsInterpolation();
        QueueRedraw();
    }

    private void OnBodyEntered(Node body)
    {
        if (body is PuppetPartBody)
            BuddyContactCount++;
    }

    /// <summary>
    /// Advances the linger on the owning gun's routed tick and reports whether the
    /// magazine is finished. The last fifth of the linger is a fade, so it leaves rather
    /// than blinks out.
    /// </summary>
    public bool Advance()
    {
        if (!IsLive)
            return false;

        _ticks++;
        if (LinearVelocity.Y < -1.0f)
            SawUpwardBounce = true;
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

        float alpha = Mathf.Clamp(FadeAlpha, 0.0f, 1.0f);
        var body = new Color(_color, _color.A * alpha);
        DrawRect(new Rect2(-_size * 0.5f, _size), body, true);
        if (!IsCasing)
            return;

        // The brass head on the red hull: two colours are the whole difference between a
        // shotgun shell and a red crumb at this size.
        float head = _size.Y * 0.32f;
        DrawRect(
            new Rect2(new Vector2(-_size.X * 0.5f, (_size.Y * 0.5f) - head), new Vector2(_size.X, head)),
            new Color(BrassColor, alpha),
            true);
    }
}
