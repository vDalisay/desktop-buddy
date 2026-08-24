using System;
using DesktopBuddy.Buddy.Physics;
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
    private Vector2 _velocity;
    private float _radius = 2.0f;
    private float _age;

    public void Start(BloodStainLayer2D stains, Vector2 velocity, float radius)
    {
        _stains = stains;
        _velocity = velocity;
        _radius = radius;
        ZAsRelative = false;
        ZIndex = 151;
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
/// Everything blood has already landed on. Two kinds of stain live here and they are
/// different in exactly one way: a world stain is a point in the room and never moves, and
/// a part stain is fixed to a buddy part and rides it, so blood on the chest stays on the
/// chest while the buddy is thrown around.
///
/// <para>Both are capped by one ring buffer. A stain layer that grew without limit would
/// turn a long session into a redraw cost that scales with how much fun the player has
/// had, so the oldest stain is overwritten instead — the room keeps the marks of what just
/// happened rather than everything that ever did.</para>
/// </summary>
[GlobalClass]
public partial class BloodStainLayer2D : Node2D
{
    /// <summary>
    /// How many stains are kept. Two hundred blobs is a thoroughly bloody room and still
    /// one cheap <see cref="_Draw"/>.
    /// </summary>
    private const int Capacity = 200;

    /// <summary>Lobes per blob. Enough to break the circle, few enough to stay cheap.</summary>
    private const int LobesPerStain = 4;

    private readonly Stain[] _stains = new Stain[Capacity];
    private int _next;

    private PuppetRig? _rig;

    /// <summary>True while any stain is fixed to a part, so the layer must redraw as it moves.</summary>
    private bool _hasPartStains;

    public int StainCount { get; private set; }

    /// <summary>Total stains ever added, including those the ring buffer has since dropped.</summary>
    public int TotalStainsAdded { get; private set; }

    private struct Stain
    {
        public bool Used;
        public bool OnPart;
        public BuddyPartId Part;

        /// <summary>World point for a room stain; part-local offset for a part stain.</summary>
        public Vector2 Point;
        public float Radius;
        public Color Color;
        public ulong Seed;
    }

    /// <summary>
    /// The rig is needed only to place part stains. Without it the layer still works and
    /// simply keeps room stains, which is what a scene with no buddy in it should get.
    /// </summary>
    public void Initialize(PuppetRig? rig)
    {
        _rig = rig;
        ZAsRelative = false;
        // Under the impact feedback ring and the droplets, over the buddy: blood is on him,
        // not in front of the effects that punctuate the hit that drew it.
        ZIndex = 149;
    }

    public void AddWorldStain(Vector2 worldPoint, float radius) =>
        Add(new Stain
        {
            Used = true,
            OnPart = false,
            Point = worldPoint,
            Radius = Mathf.Clamp(radius, 1.5f, 26.0f),
            Color = BloodLook.Dried,
            Seed = GD.Randi(),
        });

    /// <summary>A mark left on the buddy himself, in the struck part's own local space.</summary>
    public void AddPartStain(BuddyPartId part, Vector2 localPoint, float radius)
    {
        _hasPartStains = true;
        Add(new Stain
        {
            Used = true,
            OnPart = true,
            Part = part,
            Point = localPoint,
            Radius = Mathf.Clamp(radius, 1.5f, 20.0f),
            Color = BloodLook.Fresh.Lerp(BloodLook.Dried, 0.35f),
            Seed = GD.Randi(),
        });
    }

    private void Add(in Stain stain)
    {
        if (!_stains[_next].Used)
            StainCount++;

        _stains[_next] = stain;
        _next = (_next + 1) % Capacity;
        TotalStainsAdded++;
        QueueRedraw();
    }

    /// <summary>
    /// Wipes the room and the buddy clean. Turning Gore Mode off must leave no trace of it
    /// behind, and the Repair Kit patches the buddy up the same way.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_stains);
        _next = 0;
        StainCount = 0;
        _hasPartStains = false;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Only part stains move. A room full of floor stains and no blood on the buddy
        // costs nothing per frame.
        if (_hasPartStains && StainCount > 0)
            QueueRedraw();
    }

    public override void _Draw()
    {
        for (int index = 0; index < _stains.Length; index++)
        {
            ref Stain stain = ref _stains[index];
            if (!stain.Used)
                continue;

            if (!TryResolve(in stain, out Vector2 local))
                continue;

            DrawBlob(local, stain.Radius, stain.Color, stain.Seed);
        }
    }

    /// <summary>Where this stain is in the layer's own space right now.</summary>
    private bool TryResolve(in Stain stain, out Vector2 local)
    {
        if (!stain.OnPart)
        {
            local = ToLocal(stain.Point);
            return true;
        }

        local = Vector2.Zero;
        if (_rig is null || !GodotObject.IsInstanceValid(_rig) || !_rig.IsInitialized)
            return false;

        PuppetPartBody body = _rig.GetPart(stain.Part);
        if (!GodotObject.IsInstanceValid(body))
            return false;

        local = ToLocal(body.ToGlobal(stain.Point));
        return true;
    }

    /// <summary>
    /// A blob is a few overlapping circles rather than one, so stains read as splatter. The
    /// offsets come from the stain's own stored seed, which is what keeps a stain the same
    /// shape on every frame it is redrawn.
    /// </summary>
    private void DrawBlob(Vector2 center, float radius, Color color, ulong seed)
    {
        DrawCircle(center, radius, color);
        for (int lobe = 0; lobe < LobesPerStain; lobe++)
        {
            ulong hash = Hash(seed + (ulong)lobe);
            float angle = (hash % 3600) / 3600.0f * Mathf.Tau;
            float distance = radius * (0.45f + (((hash >> 12) % 100) / 100.0f * 0.75f));
            float lobeRadius = radius * (0.3f + (((hash >> 24) % 100) / 100.0f * 0.45f));
            DrawCircle(center + Vector2.FromAngle(angle) * distance, lobeRadius, color);
        }
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
