using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Sandbox;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// One built part standing in the room. It is an ordinary physics body on the loose-object layer,
/// so Buddies, tools, projectiles and other parts all collide with it exactly as they do with a
/// thrown ball; nothing about construction needs its own physics rules.
///
/// The body owns no durable state: <see cref="PlacedSandboxPart"/> is the truth, and the room
/// rebuilds every part at rest from that document on load, exactly as it rebuilds its Buddy roster.
/// </summary>
[GlobalClass]
public partial class SandboxPartBody : RigidBody2D
{
    private const float OutlineWidth = 2.0f;

    private static readonly Color WoodFill = new("b4813f");
    private static readonly Color MetalFill = new("9aa6b4");
    private static readonly Color RubberFill = new("3a3f47");
    private static readonly Color Outline = new("2a2118");
    // Win98 highlight navy with a white inner line, so the selection reads on any backdrop.
    private static readonly Color SelectionOuter = new("000080");
    private static readonly Color SelectionInner = new("ffffff");
    private static readonly Color NailFill = new("c8c8c8");
    private const float NailRadius = 3.5f;
    private static readonly Color ButtonRed = new("c0392b");
    private static readonly Color LampOff = new("6b6552");
    private static readonly Color LampOn = new("ffd84a");
    private static readonly Color LampGlow = new(1.0f, 0.85f, 0.3f, 0.28f);

    private bool _selected;
    private bool _frozen;

    /// <summary>The material colours, shared so the build palette previews the real part.</summary>
    public static Color FillFor(SandboxPartMaterial material) => material switch
    {
        SandboxPartMaterial.Metal => MetalFill,
        SandboxPartMaterial.Rubber => RubberFill,
        _ => WoodFill,
    };

    public static Color OutlineColor => Outline;

    private SandboxPartDefinition _definition = null!;
    private Color _fill = WoodFill;

    public SandboxPartId PartId { get; private set; }
    public bool IsConfigured { get; private set; }
    public SandboxPartDefinition Definition => _definition;

    /// <summary>
    /// False while the 3D presentation draws this part's shape: the flat body then draws only
    /// what sits on top of it — the dark outline (the Buddy's parts are outlined too), the
    /// selection outline and the frozen nail.
    /// </summary>
    public bool DrawsShape
    {
        get => _drawsShape;
        set
        {
            if (_drawsShape == value)
                return;
            _drawsShape = value;
            QueueRedraw();
        }
    }

    private bool _drawsShape = true;
    private bool _lit;

    /// <summary>A Lamp's light, from the signal network. Presentation only.</summary>
    public bool Lit
    {
        get => _lit;
        set
        {
            if (_lit == value)
                return;
            _lit = value;
            QueueRedraw();
        }
    }

    /// <summary>How far a Piston's head reaches past its face when out.</summary>
    public const float PistonReach = 24.0f;

    private int _pistonTicksLeft;

    /// <summary>
    /// Ticks a Piston's head stays out; zero when retracted. Set and counted down by the room's
    /// device tick. Presentation only — the shove itself happens once, when the head goes out.
    /// </summary>
    public int PistonTicksLeft
    {
        get => _pistonTicksLeft;
        set
        {
            if (_pistonTicksLeft == value)
                return;
            bool wasOut = _pistonTicksLeft > 0;
            _pistonTicksLeft = value;
            if (wasOut != value > 0)
                QueueRedraw();
        }
    }

    /// <summary>Build/Edit's selection outline. Presentation only; the document owns nothing of it.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            QueueRedraw();
        }
    }

    public void Configure(PlacedSandboxPart part, SandboxPartDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(definition);
        if (IsInsideTree())
            throw new InvalidOperationException("A sandbox part must be configured before entering the tree.");

        PartId = part.PartId;
        _definition = definition;
        _fill = FillFor(definition.Material);

        AddChild(new CollisionShape2D
        {
            Shape = definition.Shape == SandboxPartShape.Circle
                ? new CircleShape2D { Radius = definition.Radius }
                : new RectangleShape2D { Size = new Vector2(definition.Width, definition.Height) },
        });
        CollisionLayer = CollisionLayers.LooseObjects;
        CollisionMask = CollisionLayers.MaskLooseObjects;
        CanSleep = true;
        RotationDegrees = part.RotationDegrees;
        ApplyOverrides(part.Overrides);
        IsConfigured = true;
    }

    /// <summary>
    /// Applies this placement's bounded tuning. Absent values fall back to the shared definition,
    /// which is never mutated, so clearing an override restores the canonical part.
    /// </summary>
    public void ApplyOverrides(SandboxPartOverrides overrides)
    {
        SandboxPartOverrides clamped = overrides.Clamped();
        Mass = clamped.MassFor(_definition);
        GravityScale = clamped.GravityScaleValue;

        float bounce = clamped.BounceFor(_definition);
        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Bounce = bounce,
            Friction = _definition.Friction,
        };

        Freeze = clamped.Frozen;
        _frozen = clamped.Frozen;
        FreezeMode = FreezeModeEnum.Static;
        if (clamped.Frozen)
        {
            LinearVelocity = Vector2.Zero;
            AngularVelocity = 0.0f;
        }
        QueueRedraw();
    }

    /// <summary>Geometric hit test in world space; usable while the room is paused.</summary>
    public bool ContainsPoint(Vector2 world)
    {
        if (!IsConfigured)
            return false;

        Vector2 local = (world - GlobalPosition).Rotated(-GlobalRotation);
        if (_definition.Shape == SandboxPartShape.Circle)
            return local.LengthSquared() <= _definition.Radius * _definition.Radius;
        return Math.Abs(local.X) <= _definition.Width * 0.5f &&
            Math.Abs(local.Y) <= _definition.Height * 0.5f;
    }

    /// <summary>
    /// What makes a device read as one, drawn over its shape in both presentations like the nail:
    /// a Button's red cap, a Timer's clock face, a Lamp's bulb, a Piston's head.
    /// </summary>
    private void DrawDeviceFace()
    {
        float halfWidth = _definition.Width * 0.5f;
        float halfHeight = _definition.Height * 0.5f;
        switch (_definition.Device)
        {
            case SandboxDeviceKind.Button:
                var cap = new Rect2(-halfWidth * 0.55f, -halfHeight - 6.0f, halfWidth * 1.1f, 6.0f);
                DrawRect(cap, ButtonRed, filled: true);
                DrawRect(cap, Outline, filled: false, 1.5f);
                break;
            case SandboxDeviceKind.Timer:
                float face = Math.Min(halfWidth, halfHeight) * 0.72f;
                DrawCircle(Vector2.Zero, face, SelectionInner, true, -1.0f, true);
                DrawArc(Vector2.Zero, face, 0.0f, Mathf.Tau, 24, Outline, 1.5f, true);
                DrawLine(Vector2.Zero, new Vector2(0.0f, -face * 0.75f), Outline, 1.5f, true);
                DrawLine(Vector2.Zero, new Vector2(face * 0.5f, 0.0f), Outline, 1.5f, true);
                break;
            case SandboxDeviceKind.Lamp:
                float bulb = halfWidth * 0.8f;
                var centre = new Vector2(0.0f, -halfHeight * 0.25f);
                if (_lit)
                    DrawCircle(centre, bulb * 2.2f, LampGlow, true, -1.0f, true);
                DrawCircle(centre, bulb, _lit ? LampOn : LampOff, true, -1.0f, true);
                DrawArc(centre, bulb, 0.0f, Mathf.Tau, 20, Outline, 1.5f, true);
                break;
            case SandboxDeviceKind.Piston:
                // The rod and head on the face it pushes from (local up), out or home.
                float reach = _pistonTicksLeft > 0 ? PistonReach : 0.0f;
                var rod = new Rect2(-halfWidth * 0.18f, -halfHeight - reach, halfWidth * 0.36f, reach);
                var head = new Rect2(-halfWidth, -halfHeight - reach - 6.0f, halfWidth * 2.0f, 6.0f);
                if (reach > 0.0f)
                {
                    DrawRect(rod, NailFill, filled: true);
                    DrawRect(rod, Outline, filled: false, 1.5f);
                }
                DrawRect(head, MetalFill, filled: true);
                DrawRect(head, Outline, filled: false, 1.5f);
                break;
        }
    }

    public override void _Draw()
    {
        if (!IsConfigured)
            return;

        if (_definition.Shape == SandboxPartShape.Circle)
        {
            if (_drawsShape)
            {
                DrawCircle(Vector2.Zero, _definition.Radius, _fill, true, -1.0f, true);
                // A spoke, so a rolling wheel reads as rolling rather than sliding.
                DrawLine(Vector2.Zero, new Vector2(_definition.Radius, 0.0f), Outline, OutlineWidth, true);
            }
            DrawArc(Vector2.Zero, _definition.Radius, 0.0f, Mathf.Tau, 32, Outline, OutlineWidth, true);
            if (_selected)
            {
                DrawArc(Vector2.Zero, _definition.Radius + 3.0f, 0.0f, Mathf.Tau, 40, SelectionOuter, 3.0f, true);
                DrawArc(Vector2.Zero, _definition.Radius + 1.0f, 0.0f, Mathf.Tau, 40, SelectionInner, 1.0f, true);
            }
        }
        else
        {
            var rect = new Rect2(
                new Vector2(-_definition.Width * 0.5f, -_definition.Height * 0.5f),
                new Vector2(_definition.Width, _definition.Height));
            if (_drawsShape)
                DrawRect(rect, _fill, filled: true);
            DrawRect(rect, Outline, filled: false, OutlineWidth);
            if (_selected)
            {
                DrawRect(rect.Grow(3.0f), SelectionOuter, filled: false, 3.0f);
                DrawRect(rect.Grow(1.0f), SelectionInner, filled: false, 1.0f);
            }
        }

        DrawDeviceFace();

        // A nail through the middle: frozen parts do not move, and the player should see which.
        if (_frozen)
        {
            DrawCircle(Vector2.Zero, NailRadius, NailFill, true, -1.0f, true);
            DrawArc(Vector2.Zero, NailRadius, 0.0f, Mathf.Tau, 16, Outline, 1.5f, true);
        }
    }
}
