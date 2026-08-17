using System;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Presentation punctuation for the existing physical projectile. Contact detection and damage
/// remain owned by ProjectileBody; this partial only turns an observed hit into a short smoke puff
/// and adds a brighter layered tracer over the existing streak.
/// </summary>
public partial class ProjectileBody
{
    private ProjectileTrailGlow2D? _trailGlow;
    private int _smokeSpawnedForInteractionId = -1;

    public override void _Process(double delta)
    {
        EnsurePunctuationVisual();

        if (_trailGlow is not null)
        {
            bool live = State == ProjectileState.Live && Visible;
            _trailGlow.SetTrail(
                live ? LocalStreakForward() : Vector2.Zero,
                Radius,
                _trailColor,
                live);
        }

        if (HasHit && _smokeSpawnedForInteractionId != InteractionId)
        {
            _smokeSpawnedForInteractionId = InteractionId;
            SpawnImpactSmoke();
        }
    }

    private void EnsurePunctuationVisual()
    {
        if (GodotObject.IsInstanceValid(_trailGlow))
            return;

        _trailGlow = new ProjectileTrailGlow2D { Name = "BrightTracer" };
        AddChild(_trailGlow);
    }

    private void SpawnImpactSmoke()
    {
        Node? parent = GetParent();
        if (parent is null || !GodotObject.IsInstanceValid(parent))
            return;

        // The projectile is still held at the solver contact while HasHit first becomes true, so
        // its global centre is the stable visual contact anchor without perturbing physics.
        var smoke = new BulletImpactSmoke2D
        {
            Name = "BulletImpactSmoke",
            GlobalPosition = GlobalPosition,
        };
        parent.AddChild(smoke);
        smoke.GlobalPosition = GlobalPosition;
        smoke.Start(_approachVelocity);
    }
}

/// <summary>Layered emissive-looking 2D tracer. It is visual-only and follows the projectile.</summary>
internal sealed partial class ProjectileTrailGlow2D : Node2D
{
    private Vector2 _forward;
    private float _radius;
    private Color _color;
    private bool _live;

    public void SetTrail(Vector2 forward, float radius, Color color, bool live)
    {
        _forward = forward;
        _radius = radius;
        _color = color;
        _live = live;
        Visible = live;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_live || _forward == Vector2.Zero)
            return;

        float length = _radius * 7.5f;
        Vector2 tail = -_forward * length;
        Color halo = new(_color.R, _color.G, _color.B, 0.28f);
        Color core = _color.Lightened(0.38f);
        DrawLine(Vector2.Zero, tail, halo, MathF.Max(2.0f, _radius * 3.0f), true);
        DrawLine(Vector2.Zero, tail, core, MathF.Max(1.2f, _radius * 1.15f), true);
    }
}

/// <summary>
/// Tiny deterministic smoke burst at a bullet contact. It intentionally allocates no textures or
/// particle resources: five soft circles expand, drift and fade for a fraction of a second, then
/// the node frees itself. This keeps the effect readable against both the wall and floor.
/// </summary>
internal sealed partial class BulletImpactSmoke2D : Node2D
{
    private const float LifetimeSeconds = 0.28f;
    private float _age;
    private Vector2 _impactDirection;

    public void Start(Vector2 approachVelocity)
    {
        _impactDirection = approachVelocity.LengthSquared() > 0.001f
            ? approachVelocity.Normalized()
            : Vector2.Right;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= LifetimeSeconds)
        {
            QueueFree();
            return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        float t = Mathf.Clamp(_age / LifetimeSeconds, 0.0f, 1.0f);
        float alpha = 0.72f * (1.0f - t);
        float spread = 3.0f + (t * 8.0f);
        Vector2 away = -_impactDirection;
        Vector2 side = new(-away.Y, away.X);
        Vector2 rise = Vector2.Up * (t * 5.0f);

        DrawCircle(rise + away * spread * 0.35f, 2.8f + t * 3.6f,
            new Color(0.72f, 0.72f, 0.72f, alpha));
        DrawCircle(rise + side * spread * 0.55f, 2.2f + t * 3.0f,
            new Color(0.58f, 0.58f, 0.58f, alpha * 0.82f));
        DrawCircle(rise - side * spread * 0.50f, 2.0f + t * 2.7f,
            new Color(0.64f, 0.64f, 0.64f, alpha * 0.78f));
        DrawCircle(rise + away * spread * 0.75f + side * 1.5f, 1.7f + t * 2.3f,
            new Color(0.78f, 0.78f, 0.78f, alpha * 0.68f));
        DrawCircle(rise + away * spread * 0.60f - side * 2.0f, 1.5f + t * 2.1f,
            new Color(0.52f, 0.52f, 0.52f, alpha * 0.62f));
    }
}
