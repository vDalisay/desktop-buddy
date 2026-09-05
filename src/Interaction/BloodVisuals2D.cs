using System;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Interaction;

/// <summary>
/// The shared look of Gore Mode. Every colour and shape in this file is presentation and
/// nothing here is ever consulted by the damage pipeline.
/// </summary>
internal static class BloodLook
{
    /// <summary>Fresh, straight out of the wound.</summary>
    internal static readonly Color Fresh = new("9e1420");

    /// <summary>What a grain settles to once it has landed and dried a little.</summary>
    internal static readonly Color Dried = new("6b0f18");

    private static ImageTexture? _grain;
    private static ImageTexture? _teardrop;

    /// <summary>
    /// One round grain of blood.
    ///
    /// <para>This replaced an atlas of six irregular splats picked per instance by a vertex
    /// shader. The splats were built from angular harmonics, and the owner's word for the
    /// result was <b>"shards"</b> — the ragged outline that was supposed to read as spread
    /// liquid read as broken glass instead, and sampling across the atlas cells made them
    /// flicker on the way out (report 2026-08-25). A plain round grain has neither problem,
    /// needs no shader, and is what "like sand but red" actually asks for: the liquid look
    /// comes from many small grains piling up, not from one clever shape.</para>
    /// </summary>
    internal static ImageTexture Grain()
    {
        if (GodotObject.IsInstanceValid(_grain))
            return _grain!;

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
                float distance = MathF.Sqrt((nx * nx) + (ny * ny));
                if (distance >= 1.0f)
                    continue;

                // Solid nearly to the rim with a short soft edge. Blood beads; it has a
                // meniscus rather than fading out like smoke.
                float alpha = distance < 0.74f ? 1.0f : 1.0f - ((distance - 0.74f) / 0.26f);
                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, Mathf.Clamp(alpha, 0.0f, 1.0f)));
            }
        }

        _grain = ImageTexture.CreateFromImage(image);
        return _grain;
    }

    /// <summary>
    /// A falling drop: round at the heavy end, drawn to a point at the tail. Local +Y is the
    /// heavy end, so a drop rotated to face its travel leads with its weight.
    /// </summary>
    internal static ImageTexture Teardrop()
    {
        if (GodotObject.IsInstanceValid(_teardrop))
            return _teardrop!;

        const int width = 24;
        const int height = 48;
        Image image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        float centerX = (width - 1) * 0.5f;

        for (int y = 0; y < height; y++)
        {
            // v runs 0 at the tail to 1 at the heavy end.
            float v = y / (float)(height - 1);

            // A swell toward the bottom, so the shoulders are round and the tail tapers to
            // nothing rather than ending in a wedge.
            float halfWidth = centerX * MathF.Pow(v, 1.7f) * (1.0f - (0.25f * v * v));
            if (halfWidth <= 0.5f)
                continue;

            for (int x = 0; x < width; x++)
            {
                float dx = MathF.Abs(x - centerX) / halfWidth;
                if (dx >= 1.0f)
                    continue;

                float alpha = dx < 0.7f ? 1.0f : 1.0f - ((dx - 0.7f) / 0.3f);
                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, Mathf.Clamp(alpha, 0.0f, 1.0f)));
            }
        }

        _teardrop = ImageTexture.CreateFromImage(image);
        return _teardrop;
    }
}

/// <summary>
/// The spray thrown out of a wound. One short-lived one-shot particle node per burst that
/// frees itself, so the pool is bounded by construction.
/// </summary>
internal sealed partial class BloodSpray2D : Node2D
{
    private const float NodeLifetimeSeconds = 1.0f;

    private float _remaining = NodeLifetimeSeconds;

    /// <param name="direction">Which way the blood goes.</param>
    /// <param name="intensity">Wound strength <c>0..1</c>; scales count, speed and size.</param>
    /// <param name="particleStride">
    /// The Reduced Particles divisor. Gore Mode still honours the accessibility settings:
    /// a player who wants fewer particles gets fewer, gore or not.
    /// </param>
    public void Start(Vector2 direction, float intensity, int particleStride)
    {
        intensity = Mathf.Clamp(intensity, 0.05f, 1.0f);
        Vector2 spray = direction.LengthSquared() > 0.001f ? direction.Normalized() : Vector2.Up;

        int amount = Math.Max(4, (int)(GD.RandRange(14, 26) * intensity) / Math.Max(1, particleStride));
        var particles = new CpuParticles2D
        {
            Name = "BloodSprayParticles",
            Amount = amount,
            Lifetime = (float)GD.RandRange(0.3, 0.55),
            LifetimeRandomness = 0.5f,
            OneShot = true,
            Explosiveness = 0.95f,
            Randomness = 0.8f,
            Direction = spray,
            Spread = (float)GD.RandRange(22.0, 42.0),
            // Real gravity, so the spray arcs down instead of hanging like smoke.
            Gravity = new Vector2(0.0f, 1100.0f),
            InitialVelocityMin = 90.0f * intensity,
            InitialVelocityMax = (float)GD.RandRange(280.0, 460.0) * intensity,
            // Small: a spray is a shower of grains, not a handful of blobs.
            ScaleAmountMin = 0.05f + (0.04f * intensity),
            ScaleAmountMax = 0.11f + (0.10f * intensity),
            AngleMin = -180.0f,
            AngleMax = 180.0f,
            Color = BloodLook.Fresh,
            Texture = BloodLook.Grain(),
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
}

/// <summary>
/// Every drop in the air and every grain of blood on the floor, drawn as two
/// <see cref="MultiMeshInstance2D"/> batches — one draw call each, whatever the count.
///
/// <para><b>Blood lands in the room and nowhere else.</b> An earlier version also stuck
/// marks to the buddy's parts, which the owner did not want: blood on him should be the
/// spray passing over him and gone, not a decal riding his chest (report 2026-08-25). That
/// removal also took the per-frame batch and the rig dependency with it, so what is left
/// only ever dries.</para>
///
/// <para><b>Grains, not splats.</b> Each mark is one small round grain. A pool is what
/// happens when a wound drips on the same spot for a while and the grains pile up, which is
/// both cheaper and closer to how liquid actually reads than trying to draw a clever
/// outline for it.</para>
///
/// <para><b>Drops are data, not nodes.</b> They live in one array stepped by one loop and
/// find the floor by testing the room's own rectangle — no node, no <c>_Draw</c>, and no
/// physics query per drop per tick.</para>
/// </summary>
[GlobalClass]
public partial class BloodStainLayer2D : Node2D
{
    /// <summary>
    /// Grains of blood kept on the floor. Generous because they are small and batched: this
    /// is the budget that decides how large a pool can get before its oldest edge dries.
    /// </summary>
    private const int GrainCapacity = 320;

    /// <summary>Drops in the air at once, across every wound.</summary>
    private const int DropletCapacity = 64;

    /// <summary>How long a grain lasts before it has faded away entirely.</summary>
    private const double LifetimeSeconds = 22.0;

    /// <summary>Rebuilds per second of the floor batch while it is only drying.</summary>
    private const double FadeRebuildHz = 10.0;

    private const float DropletGravity = 1500.0f;

    /// <summary>Seconds a drop may fall before it is retired unlanded.</summary>
    private const float DropletMaxAgeSeconds = 3.5f;

    /// <summary>
    /// Grains one landing drop scatters. More than one so a drop splashes rather than
    /// stamping a single dot, which is what makes a run of drips read as spreading liquid.
    /// </summary>
    private const int GrainsPerLanding = 3;

    private readonly Grain[] _grains = new Grain[GrainCapacity];
    private readonly Droplet[] _droplets = new Droplet[DropletCapacity];

    private int _nextGrain;
    private double _sinceFadeRebuild;
    private bool _grainsDirty;

    private BoundaryController? _bounds;

    private MultiMeshInstance2D _grainBatch = null!;
    private MultiMeshInstance2D _dropletBatch = null!;

    /// <summary>Grains of blood currently on the floor.</summary>
    public int StainCount { get; private set; }

    /// <summary>Total grains ever laid down, including those since dried away.</summary>
    public int TotalStainsAdded { get; private set; }

    /// <summary>Drops currently in the air.</summary>
    public int LiveDroplets { get; private set; }

    private struct Grain
    {
        public bool Used;
        public Vector2 Point;
        public float Radius;

        /// <summary>Vertical squash. Blood on a surface lies flatter than it is wide.</summary>
        public float Flatten;
        public float Rotation;
        public Color Color;
        public double Age;
    }

    private struct Droplet
    {
        public bool Used;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Radius;
        public float Age;
    }

    /// <summary>
    /// <paramref name="bounds"/> is where drops land. Optional: without it drops expire in
    /// the air instead of pooling, which is what an isolated test composition should get
    /// rather than a crash.
    /// </summary>
    public void Initialize(BoundaryController? bounds = null)
    {
        _bounds = bounds;
        ZAsRelative = false;
        // Under the impact feedback ring, over the buddy.
        ZIndex = 149;

        _grainBatch = Batch("RoomBlood", GrainCapacity, 149, BloodLook.Grain());
        _dropletBatch = Batch("Droplets", DropletCapacity, 151, BloodLook.Teardrop());
    }

    /// <summary>
    /// One batch: a unit quad instanced per mark. <c>UseColors</c> is what lets a single
    /// batch hold marks at different stages of drying without splitting the draw call.
    /// </summary>
    private MultiMeshInstance2D Batch(string name, int capacity, int zIndex, Texture2D texture)
    {
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };
        var instance = new MultiMeshInstance2D
        {
            Name = name,
            Multimesh = multi,
            Texture = texture,
            ZAsRelative = false,
            ZIndex = zIndex,
        };
        AddChild(instance);
        return instance;
    }

    /// <summary>
    /// Lays down one scatter of grains where a drop landed. The oldest grains are recycled
    /// once the floor is full, so a long bleed keeps the marks of what just happened rather
    /// than everything that ever did.
    /// </summary>
    public void AddWorldStain(Vector2 worldPoint, float radius)
    {
        radius = Mathf.Clamp(radius, 1.2f, 7.0f);
        for (int grain = 0; grain < GrainsPerLanding; grain++)
        {
            // The first grain lands where the drop did; the rest scatter around it.
            Vector2 scatter = grain == 0
                ? Vector2.Zero
                : new Vector2(
                    (float)GD.RandRange(-radius * 1.6, radius * 1.6),
                    (float)GD.RandRange(-radius * 0.5, radius * 0.5));

            Put(new Grain
            {
                Used = true,
                Point = worldPoint + scatter,
                Radius = radius * (float)GD.RandRange(0.5, 1.0),
                Flatten = (float)GD.RandRange(0.45, 0.75),
                Rotation = (float)GD.RandRange(0.0, Mathf.Tau),
                Color = BloodLook.Dried,
            });
        }
    }

    /// <summary>
    /// Starts one falling drop. Silently ignored once the air is full: the cadence is the
    /// wound's business, and thinning it here is cheaper than any pool.
    /// </summary>
    public void AddDroplet(Vector2 origin, Vector2 velocity, float radius)
    {
        for (int index = 0; index < _droplets.Length; index++)
        {
            ref Droplet droplet = ref _droplets[index];
            if (droplet.Used)
                continue;

            droplet = new Droplet
            {
                Used = true,
                Position = origin,
                Velocity = velocity,
                Radius = Mathf.Clamp(radius, 1.0f, 5.0f),
            };
            LiveDroplets++;
            return;
        }
    }

    private void Put(in Grain grain)
    {
        if (!_grains[_nextGrain].Used)
            StainCount++;

        _grains[_nextGrain] = grain;
        _nextGrain = (_nextGrain + 1) % _grains.Length;
        TotalStainsAdded++;
        _grainsDirty = true;
    }

    /// <summary>
    /// Wipes the room clean, drops in the air included. Turning Gore Mode off must leave no
    /// trace of it, and the Repair Kit patches the buddy up the same way.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_grains);
        Array.Clear(_droplets);
        _nextGrain = 0;
        StainCount = 0;
        LiveDroplets = 0;
        _grainsDirty = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (LiveDroplets == 0)
            return;

        float step = (float)Math.Max(0.0, delta);
        Rect2? room = _bounds is not null && GodotObject.IsInstanceValid(_bounds)
            ? _bounds.InnerBounds
            : null;

        for (int index = 0; index < _droplets.Length; index++)
        {
            ref Droplet droplet = ref _droplets[index];
            if (!droplet.Used)
                continue;

            droplet.Age += step;
            droplet.Velocity += new Vector2(0.0f, DropletGravity * step);
            droplet.Position += droplet.Velocity * step;

            if (TryLand(in droplet, room) || droplet.Age >= DropletMaxAgeSeconds)
            {
                droplet = default;
                LiveDroplets--;
            }
        }
    }

    /// <summary>
    /// Whether this drop has reached the edge of the room, staining where it hit if so. The
    /// room is an axis-aligned rectangle, so this is four comparisons rather than a physics
    /// raycast per drop per tick.
    /// </summary>
    private bool TryLand(in Droplet droplet, Rect2? room)
    {
        if (room is not { } bounds)
            return false;

        Vector2 landing = droplet.Position;
        bool landed = false;

        if (landing.Y >= bounds.End.Y)
        {
            landing.Y = bounds.End.Y;
            landed = true;
        }
        else if (landing.Y <= bounds.Position.Y)
        {
            landing.Y = bounds.Position.Y;
            landed = true;
        }

        if (landing.X >= bounds.End.X)
        {
            landing.X = bounds.End.X;
            landed = true;
        }
        else if (landing.X <= bounds.Position.X)
        {
            landing.X = bounds.Position.X;
            landed = true;
        }

        if (!landed)
            return false;

        AddWorldStain(landing, droplet.Radius * (float)GD.RandRange(1.1, 1.7));
        return true;
    }

    public override void _Process(double delta)
    {
        double step = Math.Max(0.0, delta);
        bool anyGrain = Age(step);

        // The floor only ever dries, and drying is not something anyone can see at frame
        // rate. Rebuilding on a slow clock is why this is a separate batch.
        _sinceFadeRebuild += step;
        if (_grainsDirty || (anyGrain && _sinceFadeRebuild >= 1.0 / FadeRebuildHz))
        {
            _sinceFadeRebuild = 0.0;
            _grainsDirty = false;
            RebuildGrains();
        }

        RebuildDroplets();
    }

    /// <summary>Advances the floor and frees what has dried. True if any grain remains.</summary>
    private bool Age(double delta)
    {
        bool any = false;
        for (int index = 0; index < _grains.Length; index++)
        {
            ref Grain grain = ref _grains[index];
            if (!grain.Used)
                continue;

            grain.Age += delta;
            if (grain.Age >= LifetimeSeconds)
            {
                grain = default;
                StainCount--;
                _grainsDirty = true;
                continue;
            }

            any = true;
        }

        return any;
    }

    private void RebuildGrains()
    {
        MultiMesh multi = _grainBatch.Multimesh;
        int visible = 0;
        for (int index = 0; index < _grains.Length; index++)
        {
            ref readonly Grain grain = ref _grains[index];
            if (!grain.Used)
                continue;

            // Fade to nothing rather than winking out: an instance dropped at a visible
            // alpha is the "glitch just before it fades" the owner saw (report 2026-08-25).
            float alpha = Fade(grain.Age);
            multi.SetInstanceTransform2D(visible, new Transform2D(
                grain.Rotation,
                new Vector2(grain.Radius * 2.0f, grain.Radius * 2.0f * grain.Flatten),
                0.0f,
                ToLocal(grain.Point)));
            multi.SetInstanceColor(visible, grain.Color with { A = grain.Color.A * alpha });
            visible++;
        }

        multi.VisibleInstanceCount = visible;
    }

    private void RebuildDroplets()
    {
        MultiMesh multi = _dropletBatch.Multimesh;
        if (LiveDroplets == 0)
        {
            multi.VisibleInstanceCount = 0;
            return;
        }

        int visible = 0;
        for (int index = 0; index < _droplets.Length; index++)
        {
            ref readonly Droplet droplet = ref _droplets[index];
            if (!droplet.Used)
                continue;

            // A drop points the way it is going and stretches as it picks up speed, so it
            // reads as falling rather than as a bead sliding down the screen.
            float stretch = Mathf.Clamp(droplet.Velocity.Length() / 520.0f, 1.0f, 2.6f);
            Vector2 along = droplet.Velocity.LengthSquared() > 1.0f
                ? droplet.Velocity.Normalized()
                : Vector2.Down;

            multi.SetInstanceTransform2D(visible, new Transform2D(
                along.Angle() - (Mathf.Pi * 0.5f),
                new Vector2(droplet.Radius * 2.0f, droplet.Radius * 2.0f * stretch),
                0.0f,
                ToLocal(droplet.Position)));
            multi.SetInstanceColor(visible, BloodLook.Fresh);
            visible++;
        }

        multi.VisibleInstanceCount = visible;
    }

    /// <summary>
    /// Full strength, then a smooth dry-out over the tail of the grain's life. Squared so
    /// it thins slowly at first and vanishes without a step at the end.
    /// </summary>
    private static float Fade(double age)
    {
        float remaining = Domain.Damage.StainFade.AlphaFor(age, LifetimeSeconds);
        return remaining * remaining;
    }
}
