using System;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Presentation punctuation and a narrow room-boundary sweep for the existing physical projectile.
/// Buddy/loose-object contact detection and damage remain owned by ProjectileBody's solver path.
/// The sweep exists only for the static room bounds, where a very fast capture-polish pistol can
/// cross a thin wall between discrete solver samples. It makes that wall contact deterministic
/// without turning engine CCD back on and thereby changing the measured Buddy impact impulse.
/// </summary>
public partial class ProjectileBody
{
    private ProjectileTrailGlow2D? _trailGlow;
    private int _smokeSpawnedForInteractionId = -1;
    private int _sweepInteractionId = -1;
    private Vector2 _sweepPreviousPosition;
    private Vector2 _impactFxPoint;
    private bool _hasImpactFxPoint;

    public override void _PhysicsProcess(double delta)
    {
        if (State != ProjectileState.Live)
            return;

        if (_sweepInteractionId != InteractionId)
        {
            _sweepInteractionId = InteractionId;
            _sweepPreviousPosition = LaunchPosition;
            _impactFxPoint = Vector2.Zero;
            _hasImpactFxPoint = false;
        }

        Vector2 current = GlobalPosition;
        if (!current.IsEqualApprox(_sweepPreviousPosition))
            SweepRoomBounds(_sweepPreviousPosition, current);

        if (State == ProjectileState.Live)
            _sweepPreviousPosition = GlobalPosition;
    }

    public override void _Process(double delta)
    {
        EnsurePunctuationVisual();

        if (_trailGlow is not null)
        {
            bool live = State == ProjectileState.Live && Visible;
            // The bright streak is what reads as the round - it is drawn eight radii long
            // and three wide, against a four-pixel dot - so it is the one that has to stop
            // where the shot struck. It rides on the body, which goes on being resolved out
            // of what it hit for the settling window the impulse needs, so it is placed at
            // the same pinned origin the body's own drawing uses (owner report 2026-08-26).
            _trailGlow.SetTrail(
                live ? WorldStreakForward() : Vector2.Zero,
                live ? VisualOrigin : GlobalPosition,
                Radius,
                _trailColor,
                live);
        }

        TraceShot();

        if (HasHit && _smokeSpawnedForInteractionId != InteractionId)
        {
            _smokeSpawnedForInteractionId = InteractionId;
            SpawnImpactSmoke(_hasImpactFxPoint ? _impactFxPoint : GlobalPosition);
        }
    }

    private void SweepRoomBounds(Vector2 from, Vector2 to)
    {
        World2D? world = GetWorld2D();
        if (world is null)
            return;

        PhysicsDirectSpaceState2D? space = world.DirectSpaceState;
        if (space is null)
            return;

        PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(
            from,
            to,
            CollisionLayers.RoomBounds);
        query.CollideWithBodies = true;
        query.CollideWithAreas = false;
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0 || !hit.TryGetValue("position", out Variant positionValue))
            return;

        Vector2 point = positionValue.AsVector2();
        _impactFxPoint = point;
        _hasImpactFxPoint = true;
        _contactObserved = true;
        _contactTicks = 0;
        _hitBodyId = 0;

        // This is a room wall/floor hit, so no gameplay attribution is waiting on the projectile.
        // Stop it exactly at the swept contact and retire it immediately; Buddy/loose-object hits
        // continue to use the normal settling window so their solver impulse can still be scored.
        GlobalPosition = point;
        LinearVelocity = Vector2.Zero;
        AngularVelocity = 0.0f;
        Spend();

        _smokeSpawnedForInteractionId = InteractionId;
        SpawnImpactSmoke(point);
    }

    /// <summary>
    /// Every rendered frame of a live shot, from the muzzle to the moment it despawns: where
    /// it is, which way the body has spun, and - the one that matters - which way the streak
    /// the player sees is really pointing. Asked for by the owner 2026-08-26 to settle where
    /// a hit's apparent twist comes from, and cheap enough to leave in: debug builds only, and
    /// only while a shot is in the air.
    /// </summary>
    private void TraceShot()
    {
        if (!BuildInfo.IsDebugBuild || State != ProjectileState.Live)
            return;

        Log.Debug("ShotTrace",
            $"shot={Name} id={InteractionId} hit={HasHit} " +
            $"pos=({GlobalPosition.X:0.#},{GlobalPosition.Y:0.#}) " +
            $"body_rot={Mathf.RadToDeg(Rotation):0.#}deg " +
            $"streak={Mathf.RadToDeg(VisualForward.Angle()):0.#}deg " +
            $"drawn_at=({VisualOrigin.X:0.#};{VisualOrigin.Y:0.#}) " +
            $"speed={LinearVelocity.Length():0} travelled={TravelledPx:0}px");
    }

    private void EnsurePunctuationVisual()
    {
        if (GodotObject.IsInstanceValid(_trailGlow))
            return;

        _trailGlow = new ProjectileTrailGlow2D { Name = "BrightTracer" };
        AddChild(_trailGlow);
    }

    private void SpawnImpactSmoke(Vector2 worldPoint)
    {
        if (!_emitsImpactSmoke)
            return;

        Node? parent = GetParent();
        if (parent is null || !GodotObject.IsInstanceValid(parent))
            return;

        var smoke = new BulletImpactSmoke2D
        {
            Name = "BulletImpactSmoke",
            GlobalPosition = worldPoint,
        };
        parent.AddChild(smoke);
        smoke.GlobalPosition = worldPoint;
        // The heading from before the collision bent it: aimed with the live velocity, the
        // plume blew off in a different direction on every hit - sometimes sideways, sometimes
        // back the way the shot came - because that value has already been turned by the
        // impact it is describing.
        smoke.Start(ImpactHeading, _impactSmokeColor);
    }
}

/// <summary>Layered emissive-looking 2D tracer. It is visual-only and follows the projectile.</summary>
internal sealed partial class ProjectileTrailGlow2D : Node2D
{
    private Vector2 _worldForward;
    private Vector2 _worldOrigin;
    private float _radius;
    private Color _color;
    private bool _live;

    /// <summary>
    /// Takes the streak in world terms - where it starts and which way it points - and does
    /// the conversion into its own space at draw time. Handed a heading already turned into
    /// this node's space instead, the streak would be drawn under whatever the body's
    /// rotation happened to be at the draw, which is not the rotation it was converted for:
    /// a body spinning out of an impact turns between the two, and the shot is drawn crooked
    /// for exactly the frames the player is reading the hit.
    /// </summary>
    public void SetTrail(Vector2 worldForward, Vector2 worldOrigin, float radius, Color color, bool live)
    {
        _worldForward = worldForward;
        _worldOrigin = worldOrigin;
        _radius = radius;
        _color = color;
        _live = live;
        Visible = live;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_live || _worldForward == Vector2.Zero)
            return;

        // Converted here, under the transform the streak is really drawn with, so no spin
        // the body picks up between one frame's bookkeeping and its draw can reach it.
        Vector2 origin = ToLocal(_worldOrigin);
        Vector2 forward = _worldForward.Rotated(-GlobalRotation);

        // A tripwire rather than a running commentary: the conversion above cannot put the
        // streak at an angle other than its world heading, so this stays silent unless
        // something reintroduces the body's spin into the drawing.
        if (BuildInfo.IsDebugBuild)
        {
            float drawn = forward.Rotated(GlobalRotation).Angle();
            float intended = _worldForward.Angle();
            if (Mathf.Abs(Mathf.AngleDifference(drawn, intended)) > 0.01f)
            {
                Log.Debug("ShotTrace",
                    $"  DRAWN CROOKED parent_rot={GlobalRotationDegrees:0.#}deg " +
                    $"drawn={Mathf.RadToDeg(drawn):0.#}deg intended={Mathf.RadToDeg(intended):0.#}deg");
            }
        }

        float length = _radius * 8.5f;
        Vector2 tail = origin - (forward * length);
        Color halo = new(_color.R, _color.G, _color.B, 0.38f);
        Color core = _color.Lightened(0.48f);
        DrawLine(origin, tail, halo, MathF.Max(2.4f, _radius * 3.5f), true);
        DrawLine(origin, tail, core, MathF.Max(1.4f, _radius * 1.25f), true);
    }
}

/// <summary>
/// Compatibility-renderer-safe CPU smoke burst. Instead of hand-drawing a few circles, one tiny
/// procedural soft-noise texture is emitted by an actual particle system with randomized speed,
/// scale and lifetime. The pool size is naturally bounded by one short-lived node per impact and
/// each node frees itself after the one-shot has finished.
/// </summary>
internal sealed partial class BulletImpactSmoke2D : Node2D
{
    private const float NodeLifetimeSeconds = 0.95f;

    /// <summary>
    /// How many differently-shaped soot puffs exist. They are built once and shared; a burst
    /// picks one, so two hits on the same wall do not stamp the same cloud.
    /// </summary>
    private const int TextureVariants = 4;
    private static readonly ImageTexture?[] SharedSmokeTextures = new ImageTexture?[TextureVariants];
    private float _remaining = NodeLifetimeSeconds;

    public void Start(Vector2 approachVelocity, Color tint)
    {
        Vector2 incoming = approachVelocity.LengthSquared() > 0.001f
            ? approachVelocity.Normalized()
            : Vector2.Right;
        Vector2 plume = (-incoming + (Vector2.Up * 0.85f)).Normalized();

        // Every burst rolls its own shape. Without this each hit emitted an identical puff,
        // which reads as a decal rather than smoke once you shoot the same wall twice.
        float scale = (float)GD.RandRange(0.78, 1.32);
        plume = plume.Rotated((float)GD.RandRange(-0.35, 0.35));

        var particles = new CpuParticles2D
        {
            Name = "SmokeParticles",
            Amount = GD.RandRange(9, 18),
            Lifetime = (float)GD.RandRange(0.48, 0.82),
            LifetimeRandomness = (float)GD.RandRange(0.24, 0.46),
            OneShot = true,
            Explosiveness = (float)GD.RandRange(0.82, 0.98),
            Randomness = (float)GD.RandRange(0.6, 0.9),
            Direction = plume,
            Spread = (float)GD.RandRange(42.0, 78.0),
            Gravity = new Vector2((float)GD.RandRange(-8.0, 8.0), (float)GD.RandRange(-30.0, -14.0)),
            InitialVelocityMin = (float)GD.RandRange(12.0, 26.0),
            InitialVelocityMax = (float)GD.RandRange(44.0, 74.0),
            ScaleAmountMin = 0.55f * scale,
            ScaleAmountMax = 1.55f * scale,
            // Particles start at their own angle and keep turning, so no two puffs present
            // the same face of the shared texture.
            AngleMin = -180.0f,
            AngleMax = 180.0f,
            AngularVelocityMin = -90.0f,
            AngularVelocityMax = 90.0f,
            // The tint is the gun's, so only how thick this particular puff reads varies.
            Color = new Color(
                tint.R,
                tint.G,
                tint.B,
                Mathf.Clamp(tint.A * (float)GD.RandRange(0.78, 1.22), 0.0f, 1.0f)),
            Texture = SmokeTexture(GD.RandRange(0, TextureVariants - 1)),
            LocalCoords = false,
            Emitting = true,
        };
        AddChild(particles);
    }

    public override void _Process(double delta)
    {
        _remaining -= (float)Math.Max(0.0, delta);
        if (_remaining <= 0.0f)
            QueueFree();
    }

    private static ImageTexture SmokeTexture(int variant)
    {
        variant = Math.Clamp(variant, 0, TextureVariants - 1);
        if (GodotObject.IsInstanceValid(SharedSmokeTextures[variant]))
            return SharedSmokeTextures[variant]!;

        const int size = 32;
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - center) / center;
                float ny = (y - center) / center;
                float distance = Mathf.Sqrt((nx * nx) + (ny * ny));
                if (distance >= 1.0f)
                    continue;

                // Cheap deterministic cloud breakup: several smooth trigonometric lobes perturb
                // a radial falloff so overlapping particles read as smoke rather than discs.
                // The variant index detunes every lobe, which is what makes the four puffs
                // different clouds rather than the same cloud at four rotations.
                float detune = 1.0f + (variant * 0.37f);
                float phase = variant * 1.9f;
                float noise =
                    0.74f +
                    (0.14f * Mathf.Sin((x * 0.73f * detune) + (y * 0.31f) + phase)) +
                    (0.12f * Mathf.Sin((x * 0.21f) - (y * 0.67f * detune) - phase)) +
                    (0.08f * Mathf.Cos(((x + y) * 0.91f) + (phase * 0.5f)));
                float edge = Mathf.Pow(1.0f - distance, 1.55f + (variant * 0.16f));
                float alpha = Mathf.Clamp(edge * noise, 0.0f, 1.0f);
                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, alpha));
            }
        }

        SharedSmokeTextures[variant] = ImageTexture.CreateFromImage(image);
        return SharedSmokeTextures[variant]!;
    }
}
