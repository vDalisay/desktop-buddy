using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Damage;
using Godot;

namespace DesktopBuddy.Interaction;

/// <summary>
/// The shared look of Gore Mode. Every colour and size in this file is presentation and
/// nothing here is ever consulted by the damage pipeline.
/// </summary>
internal static class BloodLook
{
    /// <summary>Fresh arterial spray, and the droplets that fall out of a wound.</summary>
    internal static readonly Color Fresh = new("a11119");

    /// <summary>What a stain settles to once it has landed and dried a little.</summary>
    internal static readonly Color Dried = new("6d0f16");

    /// <summary>One soft round particle, built once and shared by every spray in the run.</summary>
    private static ImageTexture? _droplet;

    internal static ImageTexture DropletTexture()
    {
        if (GodotObject.IsInstanceValid(_droplet))
            return _droplet!;

        const int size = 16;
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

                // A hard core with a short soft edge: blood beads, it does not diffuse like
                // the bullet smoke this texture is otherwise a sibling of.
                image.SetPixel(x, y, new Color(1.0f, 1.0f, 1.0f, MathF.Pow(1.0f - distance, 0.55f)));
            }
        }

        _droplet = ImageTexture.CreateFromImage(image);
        return _droplet;
    }
}

/// <summary>
/// The spray thrown out of a wound at the moment it opens. One short-lived one-shot
/// particle node per hit that frees itself, so the pool is bounded by construction — the
/// same shape as the bullet impact smoke it sits beside.
/// </summary>
internal sealed partial class BloodSpray2D : Node2D
{
    private const float NodeLifetimeSeconds = 1.15f;

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

        int amount = Math.Max(3, (int)(GD.RandRange(10, 22) * intensity) / Math.Max(1, particleStride));
        var particles = new CpuParticles2D
        {
            Name = "BloodSprayParticles",
            Amount = amount,
            Lifetime = (float)GD.RandRange(0.35, 0.7),
            LifetimeRandomness = 0.5f,
            OneShot = true,
            Explosiveness = 0.95f,
            Randomness = 0.8f,
            Direction = spray,
            Spread = (float)GD.RandRange(28.0, 52.0),
            // Real gravity, so the spray arcs down instead of hanging like smoke.
            Gravity = new Vector2(0.0f, 900.0f),
            InitialVelocityMin = 60.0f * intensity,
            InitialVelocityMax = (float)GD.RandRange(240.0, 420.0) * intensity,
            ScaleAmountMin = 0.28f + (0.3f * intensity),
            ScaleAmountMax = 0.6f + (0.85f * intensity),
            Color = BloodLook.Fresh,
            Texture = BloodLook.DropletTexture(),
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
/// One drop falling out of an open wound. It is drawn, not simulated by the physics
/// server: a rigid body per drip would put dozens of contact-generating bodies into the
/// room and could shove the buddy, which would make a presentation-only setting change
/// the simulation. It free-falls, sweeps its own segment against the room bounds exactly
/// as a fast projectile does, and hands the contact point to the stain layer.
/// </summary>
internal sealed partial class BloodDroplet2D : Node2D
{
    private const float Gravity = 1400.0f;
    private const float MaximumLifetimeSeconds = 4.0f;

    private BloodStainLayer2D _stains = null!;
    private Action? _retired;
    private Vector2 _velocity;
    private float _radius = 2.0f;
    private float _age;
    private bool _reported;

    /// <param name="retired">
    /// Called exactly once when this drop is done, however it ended. The component that
    /// spawned it keeps a live count off this so a heavy bleed cannot fill the room with
    /// drawing nodes.
    /// </param>
    public void Start(BloodStainLayer2D stains, Vector2 velocity, float radius, Action? retired)
    {
        _stains = stains;
        _velocity = velocity;
        _radius = radius;
        _retired = retired;
        ZAsRelative = false;
        ZIndex = 151;
    }

    /// <summary>
    /// Retirement is reported from <see cref="Node._ExitTree"/> rather than beside each
    /// <c>QueueFree</c>, so a drop that leaves by any route — landing, timing out, or the
    /// whole layer being cleared out from under it — still gives its slot back.
    /// </summary>
    public override void _ExitTree()
    {
        if (_reported)
            return;

        _reported = true;
        _retired?.Invoke();
    }

    public override void _PhysicsProcess(double delta)
    {
        float step = (float)Math.Max(0.0, delta);
        _age += step;

        Vector2 from = GlobalPosition;
        _velocity += new Vector2(0.0f, Gravity * step);
        Vector2 to = from + (_velocity * step);

        if (Land(from, to))
            return;

        GlobalPosition = to;
        QueueRedraw();

        // A drop that never met anything — the buddy held out over a hole in the bounds —
        // is retired rather than falling forever.
        if (_age >= MaximumLifetimeSeconds)
            QueueFree();
    }

    private bool Land(Vector2 from, Vector2 to)
    {
        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        if (space is null || from.IsEqualApprox(to))
            return false;

        PhysicsRayQueryParameters2D query =
            PhysicsRayQueryParameters2D.Create(from, to, CollisionLayers.RoomBounds);
        query.CollideWithBodies = true;
        query.CollideWithAreas = false;
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0 || !hit.TryGetValue("position", out Variant position))
            return false;

        if (GodotObject.IsInstanceValid(_stains))
            _stains.AddWorldStain(position.AsVector2(), _radius * (float)GD.RandRange(1.6, 2.8));

        QueueFree();
        return true;
    }

    public override void _Draw()
    {
        // Stretched along travel: a falling drop reads as a streak, not a dot.
        float stretch = Mathf.Clamp(_velocity.Length() / 600.0f, 1.0f, 3.2f);
        Vector2 along = _velocity.LengthSquared() > 1.0f ? _velocity.Normalized() : Vector2.Down;
        DrawSetTransform(Vector2.Zero, along.Angle() - (MathF.PI * 0.5f), new Vector2(1.0f, stretch));
        DrawCircle(Vector2.Zero, _radius, BloodLook.Fresh);
    }
}

/// <summary>
/// Everything blood has already landed on. Two kinds of stain live here and they differ in
/// exactly one way: a world stain is a point in the room and never moves, and a part stain
/// is fixed to a buddy part and rides it, so blood on the chest stays on the chest while
/// the buddy is thrown around.
///
/// <para><b>Three rules keep this cheap, and the first version shipped with none of them</b>
/// (owner report 2026-08-25: the blood "looks a bit bad and tanks the performance a lot,
/// also it seems to infinitely stay").</para>
///
/// <list type="number">
///   <item><b>Stains dry up.</b> Every stain has a lifetime and fades over the last of it,
///   so the room cleans itself instead of accumulating for as long as the application is
///   open.</item>
///   <item><b>Nearby stains merge instead of stacking.</b> A drip landing on wet blood
///   grows and re-wets the pool it landed in rather than taking a slot of its own. That is
///   what stops the mound of identical overlapping blobs under a bleeding buddy, and it
///   bounds the count by the <i>area</i> bled on rather than by how long the bleeding has
///   been going on.</item>
///   <item><b>Only what moves redraws every frame.</b> Part stains ride a ragdoll and need
///   the frame rate; world stains only ever fade, which nobody can see happening faster
///   than <see cref="FadeRedrawHz"/>. The first version redrew all two hundred stains every
///   frame — a thousand <c>DrawCircle</c> calls — because one of them was on a moving
///   arm.</item>
/// </list>
/// </summary>
[GlobalClass]
public partial class BloodStainLayer2D : Node2D
{
    /// <summary>
    /// Marks on the room. Because they merge, this is a budget for bled-on <i>area</i>, and
    /// eighty pools is far more ground than one buddy covers before the first ones dry.
    /// </summary>
    private const int WorldCapacity = 80;

    /// <summary>
    /// Marks on the buddy. Deliberately small and kept apart from the room's: these are the
    /// ones that cost a redraw every frame, and six parts cannot wear more than a handful
    /// legibly anyway.
    /// </summary>
    private const int PartCapacity = 20;

    /// <summary>Lobes per blob, beyond the body circle. Two is enough to break the disc.</summary>
    private const int LobesPerStain = 2;

    /// <summary>How long a stain lasts before it has faded away entirely.</summary>
    private const double LifetimeSeconds = 26.0;

    /// <summary>
    /// A landing drip joins an existing pool whose centre is within this many of that
    /// pool's radii. Above one, so drips that only just touch still run together.
    /// </summary>
    private const float MergeRadiiFactor = 1.35f;

    /// <summary>How much of the incoming radius a merge adds. Pools spread, slowly.</summary>
    private const float MergeGrowth = 0.28f;

    /// <summary>Ceiling on a merged pool, so a long bleed spreads rather than domes.</summary>
    private const float MaximumPoolRadius = 19.0f;

    /// <summary>Redraws per second while nothing is happening but drying.</summary>
    private const double FadeRedrawHz = 8.0;

    private readonly Stain[] _world = new Stain[WorldCapacity];
    private readonly Stain[] _part = new Stain[PartCapacity];
    private int _nextWorld;
    private int _nextPart;
    private double _sinceFadeRedraw;

    private PuppetRig? _rig;

    /// <summary>
    /// The room's stains draw here rather than on this node. They never move, so they need a
    /// redraw only when one is added or has visibly faded; part stains ride a ragdoll and
    /// need every frame. Sharing one canvas would mean the moving handful dragged all
    /// hundred through a full redraw at frame rate, which is the cost that was reported.
    /// </summary>
    private BloodStainCanvas2D _worldCanvas = null!;

    public int StainCount { get; private set; }

    /// <summary>Total stains ever added, including merges and those since dried away.</summary>
    public int TotalStainsAdded { get; private set; }

    private struct Stain
    {
        public bool Used;
        public BuddyPartId Part;

        /// <summary>World point for a room stain; part-local offset for a part stain.</summary>
        public Vector2 Point;
        public float Radius;

        /// <summary>Vertical squash. A pool on the floor lies flat; a mark on a limb is rounder.</summary>
        public float Flatten;
        public Color Color;
        public ulong Seed;
        public double Age;
    }

    /// <summary>
    /// The rig is needed only to place part stains. Without it the layer still works and
    /// simply keeps room stains, which is what a scene with no buddy in it should get.
    /// </summary>
    public void Initialize(PuppetRig? rig)
    {
        _rig = rig;
        _worldCanvas = new BloodStainCanvas2D { Name = "RoomStains", Painter = DrawWorldStains };
        AddChild(_worldCanvas);
        ZAsRelative = false;
        // Under the impact feedback ring and the droplets, over the buddy: blood is on him,
        // not in front of the effects that punctuate the hit that drew it.
        ZIndex = 149;
    }

    /// <summary>
    /// A drop landed in the room. It joins the pool it landed in if there is one, and only
    /// takes a slot of its own otherwise.
    /// </summary>
    public void AddWorldStain(Vector2 worldPoint, float radius)
    {
        radius = Mathf.Clamp(radius, 1.5f, 14.0f);
        TotalStainsAdded++;

        for (int index = 0; index < _world.Length; index++)
        {
            ref Stain pool = ref _world[index];
            if (!pool.Used || pool.Point.DistanceTo(worldPoint) > pool.Radius * MergeRadiiFactor)
                continue;

            pool.Radius = MathF.Min(MaximumPoolRadius, pool.Radius + (radius * MergeGrowth));
            // Fresh blood re-wets the pool: its clock restarts, so a wound that keeps
            // dripping keeps its puddle alive and one that stops leaves it to dry.
            pool.Age = 0.0;
            _worldCanvas.QueueRedraw();
            return;
        }

        Put(_world, ref _nextWorld, new Stain
        {
            Used = true,
            Point = worldPoint,
            Radius = radius,
            // Blood pools onto a surface rather than sitting on it as a ball.
            Flatten = 0.42f,
            Color = BloodLook.Dried,
            Seed = GD.Randi(),
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
            Radius = Mathf.Clamp(radius, 1.5f, 11.0f),
            Flatten = 0.85f,
            Color = BloodLook.Fresh.Lerp(BloodLook.Dried, 0.45f),
            Seed = GD.Randi(),
        });
    }

    private void Put(Stain[] into, ref int next, in Stain stain)
    {
        if (!into[next].Used)
            StainCount++;

        into[next] = stain;
        next = (next + 1) % into.Length;
        if (ReferenceEquals(into, _world))
            _worldCanvas.QueueRedraw();
        else
            QueueRedraw();
    }

    /// <summary>
    /// Wipes the room and the buddy clean. Turning Gore Mode off must leave no trace of it
    /// behind, and the Repair Kit patches the buddy up the same way.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_world);
        Array.Clear(_part);
        _nextWorld = 0;
        _nextPart = 0;
        StainCount = 0;
        _worldCanvas.QueueRedraw();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (StainCount == 0)
            return;

        double step = Math.Max(0.0, delta);
        bool anyPart = Age(_part, step);
        bool anyWorld = Age(_world, step);

        // Part stains ride the ragdoll, so they need every frame.
        if (anyPart)
            QueueRedraw();

        // The room only ever dries, and drying is not something anyone can see at frame
        // rate. This split is the whole point of the second canvas.
        if (!anyWorld)
            return;

        _sinceFadeRedraw += step;
        if (_sinceFadeRedraw >= 1.0 / FadeRedrawHz)
        {
            _sinceFadeRedraw = 0.0;
            _worldCanvas.QueueRedraw();
        }
    }

    /// <summary>Advances a set and frees what has dried. True if any slot is still in use.</summary>
    private bool Age(Stain[] stains, double delta)
    {
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
                continue;
            }

            any = true;
        }

        return any;
    }

    /// <summary>The room's stains, painted onto <see cref="_worldCanvas"/> on its own clock.</summary>
    private void DrawWorldStains(BloodStainCanvas2D canvas)
    {
        for (int index = 0; index < _world.Length; index++)
        {
            ref readonly Stain stain = ref _world[index];
            if (stain.Used)
                DrawStain(canvas, in stain, canvas.ToLocal(stain.Point));
        }
    }

    /// <summary>Only what rides the buddy. The room is the other canvas's business.</summary>
    public override void _Draw()
    {
        if (_rig is null || !GodotObject.IsInstanceValid(_rig) || !_rig.IsInitialized)
            return;

        for (int index = 0; index < _part.Length; index++)
        {
            ref readonly Stain stain = ref _part[index];
            if (!stain.Used)
                continue;

            PuppetPartBody body = _rig.GetPart(stain.Part);
            if (GodotObject.IsInstanceValid(body))
                DrawStain(this, in stain, ToLocal(body.ToGlobal(stain.Point)));
        }
    }

    /// <summary>
    /// A blob is the body circle plus a couple of overlapping lobes, squashed toward the
    /// surface it is lying on. The lobe offsets come from the stain's own stored seed,
    /// which is what keeps a stain the same shape on every frame it is redrawn.
    /// </summary>
    private static void DrawStain(CanvasItem canvas, in Stain stain, Vector2 center)
    {
        float alpha = StainFade.AlphaFor(stain.Age, LifetimeSeconds);
        if (alpha <= 0.01f)
            return;

        Color color = stain.Color with { A = stain.Color.A * alpha };
        canvas.DrawSetTransform(center, 0.0f, new Vector2(1.0f, stain.Flatten));
        canvas.DrawCircle(Vector2.Zero, stain.Radius, color);
        for (int lobe = 0; lobe < LobesPerStain; lobe++)
        {
            ulong hash = Hash(stain.Seed + (ulong)lobe);
            float angle = (hash % 3600) / 3600.0f * Mathf.Tau;
            float distance = stain.Radius * (0.5f + (((hash >> 12) % 100) / 100.0f * 0.7f));
            float lobeRadius = stain.Radius * (0.34f + (((hash >> 24) % 100) / 100.0f * 0.4f));
            canvas.DrawCircle(Vector2.FromAngle(angle) * distance, lobeRadius, color);
        }

        canvas.DrawSetTransform(Vector2.Zero, 0.0f, Vector2.One);
    }

    /// <summary>Cheap deterministic mixer; the shape of a stain must never flicker.</summary>
    private static ulong Hash(ulong value)
    {
        value ^= value >> 33;
        value *= 0xFF51AFD7ED558CCDUL;
        value ^= value >> 33;
        return value;
    }
}

/// <summary>
/// A bare canvas the stain layer paints the room's marks onto. It exists purely so those
/// marks can carry their own redraw clock, independent of the part stains that have to
/// follow a moving ragdoll every frame.
/// </summary>
internal sealed partial class BloodStainCanvas2D : Node2D
{
    public Action<BloodStainCanvas2D>? Painter { get; set; }

    public override void _Draw() => Painter?.Invoke(this);
}
