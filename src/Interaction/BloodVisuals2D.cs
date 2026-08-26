using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Damage;
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

    /// <summary>What a stain settles to once it has landed and dried a little.</summary>
    internal static readonly Color Dried = new("5f0f18");

    /// <summary>
    /// How many splat shapes exist. Blood that lands as the same disc every time reads as
    /// clumps of identical blobs — the owner's "clumpy" (report 2026-08-25) — so a stain
    /// picks one of these and a random rotation on top of it.
    /// </summary>
    internal const int SplatVariants = 6;

    /// <summary>Atlas layout. Six cells, three across and two down.</summary>
    internal const int AtlasColumns = 3;
    internal const int AtlasRows = 2;

    private const int CellSize = 64;

    private static ImageTexture? _atlas;
    private static ImageTexture? _teardrop;

    /// <summary>
    /// Where variant <paramref name="variant"/> sits in the atlas, as a UV offset. Handed to
    /// the batch as per-instance custom data so six shapes still cost one draw call — a
    /// texture per shape would mean a batch per shape, which is the cost being avoided.
    /// </summary>
    internal static Vector2 AtlasOffset(int variant)
    {
        variant = Math.Clamp(variant, 0, SplatVariants - 1);
        return new Vector2(
            (variant % AtlasColumns) / (float)AtlasColumns,
            (variant / AtlasColumns) / (float)AtlasRows);
    }

    /// <summary>
    /// Six irregular splats on one sheet. Each is a radial falloff whose <b>radius varies
    /// with angle</b>, rather than a circle with lobes stuck on it: the edge itself is
    /// ragged, which is what makes it read as something that hit a surface and spread
    /// instead of a blob dropped on top of one.
    /// </summary>
    internal static ImageTexture SplatAtlas()
    {
        if (GodotObject.IsInstanceValid(_atlas))
            return _atlas!;

        Image image = Image.CreateEmpty(
            CellSize * AtlasColumns, CellSize * AtlasRows, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);

        for (int variant = 0; variant < SplatVariants; variant++)
        {
            int originX = (variant % AtlasColumns) * CellSize;
            int originY = (variant / AtlasColumns) * CellSize;
            PaintSplat(image, originX, originY, variant);
        }

        _atlas = ImageTexture.CreateFromImage(image);
        return _atlas;
    }

    private static void PaintSplat(Image image, int originX, int originY, int variant)
    {
        float center = (CellSize - 1) * 0.5f;
        float detune = 1.0f + (variant * 0.41f);
        float phase = variant * 2.3f;

        for (int y = 0; y < CellSize; y++)
        {
            for (int x = 0; x < CellSize; x++)
            {
                float nx = (x - center) / center;
                float ny = (y - center) / center;
                float distance = MathF.Sqrt((nx * nx) + (ny * ny));
                if (distance >= 1.0f)
                    continue;

                float angle = MathF.Atan2(ny, nx);

                // Three detuned harmonics: a lopsided body, a few bulges, and a fine
                // ripple. Together they give an outline with no symmetry to spot.
                float edge =
                    0.74f +
                    (0.16f * MathF.Sin((angle * 2.0f * detune) + phase)) +
                    (0.09f * MathF.Sin((angle * 5.0f) - (phase * 1.7f))) +
                    (0.05f * MathF.Sin((angle * 9.0f * detune) + (phase * 0.5f)));

                if (distance > edge)
                    continue;

                // Solid nearly to the rim, then a short soft edge: blood has a meniscus,
                // it does not fade out like smoke.
                float t = distance / edge;
                float alpha = t < 0.82f ? 1.0f : 1.0f - ((t - 0.82f) / 0.18f);
                image.SetPixel(
                    originX + x,
                    originY + y,
                    new Color(1.0f, 1.0f, 1.0f, Mathf.Clamp(alpha, 0.0f, 1.0f)));
            }
        }
    }

    /// <summary>
    /// Picks this instance's atlas cell in the vertex stage. The only reason a shader is
    /// involved at all: <see cref="MultiMesh"/> has no per-instance texture, so the shape
    /// has to come out of one sheet.
    /// </summary>
    internal static ShaderMaterial SplatMaterial()
    {
        var shader = new Shader
        {
            Code =
                """
                shader_type canvas_item;

                void vertex() {
                    UV = UV * vec2(1.0 / 3.0, 0.5) + INSTANCE_CUSTOM.xy;
                }
                """,
        };
        return new ShaderMaterial { Shader = shader };
    }

    /// <summary>
    /// A falling drop: round at the heavy end, drawn to a point at the tail. The old drip
    /// was a stretched circle, which reads as a smear rather than something falling (owner
    /// report 2026-08-25). Local +Y is the heavy end.
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
/// The spray thrown out of a wound at the moment it opens. One short-lived one-shot particle
/// node per hit that frees itself, so the pool is bounded by construction.
/// </summary>
internal sealed partial class BloodSpray2D : Node2D
{
    private const float NodeLifetimeSeconds = 1.0f;

    private float _remaining = NodeLifetimeSeconds;

    /// <param name="direction">
    /// Which way the spray goes: back along the contact normal, so a bullet through the
    /// chest sprays out of the chest rather than into it.
    /// </param>
    /// <param name="intensity">Wound strength <c>0..1</c>; scales count, speed and size.</param>
    /// <param name="particleStride">
    /// The Reduced Particles divisor. Gore Mode still honours the accessibility settings:
    /// a player who wants fewer particles gets fewer, gore or not.
    /// </param>
    public void Start(Vector2 direction, float intensity, int particleStride)
    {
        intensity = Mathf.Clamp(intensity, 0.05f, 1.0f);
        Vector2 spray = direction.LengthSquared() > 0.001f ? direction.Normalized() : Vector2.Up;

        int amount = Math.Max(3, (int)(GD.RandRange(8, 16) * intensity) / Math.Max(1, particleStride));
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
            Spread = (float)GD.RandRange(24.0, 46.0),
            // Real gravity, so the spray arcs down instead of hanging like smoke.
            Gravity = new Vector2(0.0f, 1100.0f),
            InitialVelocityMin = 80.0f * intensity,
            InitialVelocityMax = (float)GD.RandRange(260.0, 430.0) * intensity,
            ScaleAmountMin = 0.06f + (0.05f * intensity),
            ScaleAmountMax = 0.14f + (0.16f * intensity),
            AngleMin = -180.0f,
            AngleMax = 180.0f,
            Color = BloodLook.Fresh,
            // Flecks, not discs: the same teardrop the falling drips use, tumbling.
            Texture = BloodLook.Teardrop(),
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
/// Every drop and every mark Gore Mode has put in the world, drawn as three
/// <see cref="MultiMeshInstance2D"/> batches.
///
/// <para><b>This replaced a node-per-drop, circles-per-stain design the owner reported twice
/// as tanking the frame rate</b> (2026-08-25). Three things were wrong with it, and all
/// three were structural rather than tuning:</para>
///
/// <list type="number">
///   <item><b>Every drop was a <see cref="Node2D"/></b> with its own <c>_PhysicsProcess</c>,
///   its own <c>_Draw</c>, and a physics raycast every tick. Drops are now plain structs in
///   one array stepped by one loop, and they find the floor by testing the room's own
///   rectangle rather than asking the physics server — no query at all.</item>
///   <item><b>Every stain was five <c>DrawCircle</c> calls</b>, so a bloody room cost a
///   thousand draw calls a frame. All the stains in a set are now instances of one quad in
///   one batch: <b>one</b> draw call each, whatever the count.</item>
///   <item><b>Overlapping discs read as clumps.</b> A stain is now a single irregular splat
///   with its own rotation and squash, so blood looks spread rather than piled.</item>
/// </list>
///
/// <para>The three batches exist because they change at different rates: the room's stains
/// only ever dry, the buddy's ride a ragdoll, and drops move every tick.</para>
/// </summary>
[GlobalClass]
public partial class BloodStainLayer2D : Node2D
{
    /// <summary>
    /// Marks on the room. Because they merge, this is a budget for bled-on <i>area</i>, and
    /// well past what one buddy covers before the first ones dry.
    /// </summary>
    private const int WorldCapacity = 96;

    /// <summary>Marks on the buddy. Six parts cannot wear more than a handful legibly.</summary>
    private const int PartCapacity = 24;

    /// <summary>Drops in the air at once, across every wound.</summary>
    private const int DropletCapacity = 48;

    /// <summary>How long a stain lasts before it has faded away entirely.</summary>
    private const double LifetimeSeconds = 24.0;

    /// <summary>
    /// A landing drip joins an existing pool whose centre is within this many of that
    /// pool's radii. Above one, so drips that only just touch still run together.
    /// </summary>
    private const float MergeRadiiFactor = 1.3f;

    /// <summary>How much of the incoming radius a merge adds. Pools spread, slowly.</summary>
    private const float MergeGrowth = 0.3f;

    /// <summary>Ceiling on a merged pool, so a long bleed spreads rather than domes.</summary>
    private const float MaximumPoolRadius = 22.0f;

    /// <summary>Rebuilds per second of the room batch while it is only drying.</summary>
    private const double FadeRebuildHz = 8.0;

    private const float DropletGravity = 1500.0f;

    /// <summary>Seconds a drop may fall before it is retired unlanded.</summary>
    private const float DropletMaxAgeSeconds = 3.5f;

    private readonly Stain[] _world = new Stain[WorldCapacity];
    private readonly Stain[] _part = new Stain[PartCapacity];
    private readonly Droplet[] _droplets = new Droplet[DropletCapacity];

    private int _nextWorld;
    private int _nextPart;
    private double _sinceFadeRebuild;
    private bool _worldDirty;

    private PuppetRig? _rig;
    private BoundaryController? _bounds;

    private MultiMeshInstance2D _worldBatch = null!;
    private MultiMeshInstance2D _partBatch = null!;
    private MultiMeshInstance2D _dropletBatch = null!;

    public int StainCount { get; private set; }

    /// <summary>Total stains ever added, including merges and those since dried away.</summary>
    public int TotalStainsAdded { get; private set; }

    /// <summary>Drops currently in the air.</summary>
    public int LiveDroplets { get; private set; }

    private struct Stain
    {
        public bool Used;
        public BuddyPartId Part;

        /// <summary>World point for a room stain; part-local offset for a part stain.</summary>
        public Vector2 Point;
        public float Radius;

        /// <summary>Vertical squash. A pool on the floor lies flat; a mark on a limb is rounder.</summary>
        public float Flatten;
        public float Rotation;
        public int Variant;
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
    /// <paramref name="rig"/> places part stains and <paramref name="bounds"/> is where drops
    /// land. Both are optional: without the rig the layer keeps only room stains, and without
    /// the bounds drops expire instead of pooling, which is what an isolated test composition
    /// should get rather than a crash.
    /// </summary>
    public void Initialize(PuppetRig? rig, BoundaryController? bounds = null)
    {
        _rig = rig;
        _bounds = bounds;
        ZAsRelative = false;
        // Under the impact feedback ring, over the buddy: blood is on him, not in front of
        // the effects that punctuate the hit that drew it.
        ZIndex = 149;

        _worldBatch = Batch("RoomStains", WorldCapacity, 149, splats: true);
        _partBatch = Batch("BuddyStains", PartCapacity, 150, splats: true);
        _dropletBatch = Batch("Droplets", DropletCapacity, 151, splats: false);
    }

    /// <summary>
    /// One batch: a unit quad instanced per mark. <c>UseColors</c> is what lets a single
    /// batch hold marks at different stages of drying without splitting the draw call, and
    /// <c>UseCustomData</c> carries the atlas cell for the splat batches.
    /// </summary>
    private MultiMeshInstance2D Batch(string name, int capacity, int zIndex, bool splats)
    {
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = splats,
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };
        var instance = new MultiMeshInstance2D
        {
            Name = name,
            Multimesh = multi,
            Texture = splats ? BloodLook.SplatAtlas() : BloodLook.Teardrop(),
            Material = splats ? BloodLook.SplatMaterial() : null,
            ZAsRelative = false,
            ZIndex = zIndex,
        };
        AddChild(instance);
        return instance;
    }

    /// <summary>
    /// A drop landed in the room. It joins the pool it landed in if there is one, and only
    /// takes a slot of its own otherwise — which bounds the count by the area bled on rather
    /// than by how long the bleeding has gone on.
    /// </summary>
    public void AddWorldStain(Vector2 worldPoint, float radius)
    {
        radius = Mathf.Clamp(radius, 2.0f, 13.0f);
        TotalStainsAdded++;
        _worldDirty = true;

        for (int index = 0; index < _world.Length; index++)
        {
            ref Stain pool = ref _world[index];
            if (!pool.Used || pool.Point.DistanceTo(worldPoint) > pool.Radius * MergeRadiiFactor)
                continue;

            pool.Radius = MathF.Min(MaximumPoolRadius, pool.Radius + (radius * MergeGrowth));
            // Fresh blood re-wets the pool: its clock restarts, so a wound that keeps
            // dripping keeps its puddle alive and one that stops leaves it to dry.
            pool.Age = 0.0;
            return;
        }

        Put(_world, ref _nextWorld, new Stain
        {
            Used = true,
            Point = worldPoint,
            Radius = radius,
            // Blood pools onto a surface rather than sitting on it as a ball.
            Flatten = (float)GD.RandRange(0.34, 0.5),
            Rotation = (float)GD.RandRange(0.0, Mathf.Tau),
            Variant = GD.RandRange(0, BloodLook.SplatVariants - 1),
            Color = BloodLook.Dried,
        });
    }

    /// <summary>A mark left on the buddy himself, in the struck part's own local space.</summary>
    public void AddPartStain(BuddyPartId part, Vector2 localPoint, float radius)
    {
        TotalStainsAdded++;
        Put(_part, ref _nextPart, new Stain
        {
            Used = true,
            Part = part,
            Point = localPoint,
            Radius = Mathf.Clamp(radius, 2.0f, 10.0f),
            Flatten = (float)GD.RandRange(0.75, 1.0),
            Rotation = (float)GD.RandRange(0.0, Mathf.Tau),
            Variant = GD.RandRange(0, BloodLook.SplatVariants - 1),
            Color = BloodLook.Fresh.Lerp(BloodLook.Dried, 0.4f),
        });
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

    private void Put(Stain[] into, ref int next, in Stain stain)
    {
        if (!into[next].Used)
            StainCount++;

        into[next] = stain;
        next = (next + 1) % into.Length;
        if (ReferenceEquals(into, _world))
            _worldDirty = true;
    }

    /// <summary>
    /// Wipes the room and the buddy clean, drops in the air included. Turning Gore Mode off
    /// must leave no trace of it, and the Repair Kit patches the buddy up the same way.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_world);
        Array.Clear(_part);
        Array.Clear(_droplets);
        _nextWorld = 0;
        _nextPart = 0;
        StainCount = 0;
        LiveDroplets = 0;
        _worldDirty = true;
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
    /// room is an axis-aligned rectangle, so this is four comparisons — the old version
    /// asked the physics server for a raycast per drop per tick instead.
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

        AddWorldStain(landing, droplet.Radius * (float)GD.RandRange(1.5, 2.4));
        return true;
    }

    public override void _Process(double delta)
    {
        double step = Math.Max(0.0, delta);

        bool anyPart = Age(_part, step);
        bool anyWorld = Age(_world, step);

        // The room only ever dries, and drying is not something anyone can see at frame
        // rate. Rebuilding its batch on a slow clock is the point of keeping it separate.
        _sinceFadeRebuild += step;
        if (_worldDirty || (anyWorld && _sinceFadeRebuild >= 1.0 / FadeRebuildHz))
        {
            _sinceFadeRebuild = 0.0;
            _worldDirty = false;
            RebuildWorld();
        }

        // These two ride a ragdoll and fall under gravity, so they need every frame.
        RebuildPart(anyPart);
        RebuildDroplets();
    }

    /// <summary>Advances a set and frees what has dried. True if any slot is still in use.</summary>
    private bool Age(Stain[] stains, double delta)
    {
        bool world = ReferenceEquals(stains, _world);
        bool any = false;
        for (int index = 0; index < stains.Length; index++)
        {
            ref Stain stain = ref stains[index];
            if (!stain.Used)
                continue;

            stain.Age += delta;
            if (StainFade.HasDried(stain.Age, LifetimeSeconds))
            {
                stain = default;
                StainCount--;
                _worldDirty |= world;
                continue;
            }

            any = true;
        }

        return any;
    }

    private void RebuildWorld()
    {
        MultiMesh multi = _worldBatch.Multimesh;
        int visible = 0;
        for (int index = 0; index < _world.Length; index++)
        {
            ref readonly Stain stain = ref _world[index];
            if (stain.Used && Write(multi, visible, in stain, ToLocal(stain.Point)))
                visible++;
        }

        multi.VisibleInstanceCount = visible;
    }

    private void RebuildPart(bool any)
    {
        MultiMesh multi = _partBatch.Multimesh;
        if (!any || _rig is null || !GodotObject.IsInstanceValid(_rig) || !_rig.IsInitialized)
        {
            multi.VisibleInstanceCount = 0;
            return;
        }

        int visible = 0;
        for (int index = 0; index < _part.Length; index++)
        {
            ref readonly Stain stain = ref _part[index];
            if (!stain.Used)
                continue;

            PuppetPartBody body = _rig.GetPart(stain.Part);
            if (!GodotObject.IsInstanceValid(body))
                continue;

            // The mark rides the part's rotation as well as its position, or blood on a
            // tumbling limb would slide around it.
            if (Write(multi, visible, in stain, ToLocal(body.ToGlobal(stain.Point)), body.GlobalRotation))
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
            float stretch = Mathf.Clamp(droplet.Velocity.Length() / 520.0f, 1.0f, 2.8f);
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
    /// Places one stain into a batch. False when it has faded past visibility, so a drying
    /// stain stops costing an instance before its slot is freed.
    /// </summary>
    private static bool Write(
        MultiMesh multi,
        int slot,
        in Stain stain,
        Vector2 center,
        float extraRotation = 0.0f)
    {
        float alpha = StainFade.AlphaFor(stain.Age, LifetimeSeconds);
        if (alpha <= 0.02f)
            return false;

        multi.SetInstanceTransform2D(slot, new Transform2D(
            stain.Rotation + extraRotation,
            new Vector2(stain.Radius * 2.0f, stain.Radius * 2.0f * stain.Flatten),
            0.0f,
            center));
        multi.SetInstanceColor(slot, stain.Color with { A = stain.Color.A * alpha });

        // Which of the six shapes this stain wears. The shader reads it as a UV offset.
        Vector2 cell = BloodLook.AtlasOffset(stain.Variant);
        multi.SetInstanceCustomData(slot, new Color(cell.X, cell.Y, 0.0f, 0.0f));
        return true;
    }
}
