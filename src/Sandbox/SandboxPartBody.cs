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
/// How it looks lives in <see cref="SandboxPartLook"/>, shared with the Build palette's preview.
/// </summary>
[GlobalClass]
public partial class SandboxPartBody : RigidBody2D
{
    // Win98 highlight navy with a white inner line, so the selection reads on any backdrop.
    private static readonly Color SelectionOuter = new("000080");
    private static readonly Color SelectionInner = new("ffffff");
    private static readonly Color NailFill = new("c8c8c8");
    private const float NailRadius = 3.5f;

    // A Piston's stroke: out quickly, a beat at full reach, back more slowly.
    private const double PistonOutSeconds = 0.08;
    private const double PistonHoldSeconds = 0.18;
    private const double PistonBackSeconds = 0.2;

    // A Button lets go only after nothing has touched its cap for this long, so a body settling or
    // bouncing on it presses it once rather than chattering.
    private const double ButtonReleaseSeconds = 0.15;

    // A weapon mount's kick, and the fastest it can be made to fire.
    private const double WeaponRecoilSeconds = 0.25;

    private bool _selected;
    private bool _frozen;
    private bool _drawsShape = true;
    private bool _lit;
    private SandboxPartDefinition _baseDefinition = null!;
    private SandboxPartDefinition _definition = null!;
    private CollisionShape2D? _shape;
    private CollisionShape2D? _pistonHead;
    private CollisionShape2D? _buttonCap;
    private bool _buttonHeld;
    private int _buttonIdleTicks;
    private int _buttonFlashTicks;
    private int _buttonReleaseTicks = 1;
    private int _strokeTick;
    private int _recoilTicks;
    private int _recoilLength = 1;
    private int _outTicks = 1;
    private int _holdTicks = 1;
    private int _backTicks = 1;

    public SandboxPartId PartId { get; private set; }
    public bool IsConfigured { get; private set; }

    /// <summary>The part as placed: the shared definition at this placement's own size.</summary>
    public SandboxPartDefinition Definition => _definition;

    /// <summary>
    /// False while the 3D presentation draws this part's shape: the flat body then draws only
    /// what sits on top of it — the dark outline (the Buddy's parts are outlined too), the device
    /// face, the selection outline and the frozen nail.
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

    /// <summary>How far out a Piston's head is, 0 home to 1 at full reach.</summary>
    public float PistonExtension { get; private set; }

    /// <summary>True from the moment a Piston fires until its head is home again.</summary>
    public bool PistonBusy => _strokeTick > 0;

    /// <summary>How far a Button's cap is down, 0 up to 1 fully pressed.</summary>
    public float ButtonPress { get; private set; }

    /// <summary>How far a weapon mount is through its recoil, 1 the moment it fires and 0 at rest.</summary>
    public float WeaponRecoil => _recoilTicks / (float)Math.Max(1, _recoilLength);

    /// <summary>True while a weapon mount is still working its last shot; pulses meanwhile are ignored.</summary>
    public bool WeaponBusy => _recoilTicks > 0;

    /// <summary>
    /// Fires the mounted gun: false while the last shot is still being worked, exactly as a Piston
    /// refuses pulses until its head is home.
    /// </summary>
    public bool StartWeaponShot()
    {
        if (_definition.Device != SandboxDeviceKind.WeaponTrigger || _recoilTicks > 0)
            return false;
        _recoilLength = Math.Max(1, (int)Math.Round(WeaponRecoilSeconds * Engine.PhysicsTicksPerSecond));
        _recoilTicks = _recoilLength;
        QueueRedraw();
        return true;
    }

    /// <summary>Whatever a device's model moves: a Piston's head travel, a Button's press, a mount's kick.</summary>
    public float DeviceMotion => _definition.Device switch
    {
        SandboxDeviceKind.Button => ButtonPress,
        SandboxDeviceKind.WeaponTrigger => WeaponRecoil,
        _ => PistonExtension,
    };

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
        _baseDefinition = definition;
        _definition = part.Overrides.Clamped().SizedDefinition(definition);
        definition = _definition;

        if (definition.Device == SandboxDeviceKind.Piston)
        {
            // Two shapes: the base, and the head that moves out and is what things are pushed by.
            Rect2 baseRect = SandboxPartLook.PistonBase(definition);
            AddChild(new CollisionShape2D
            {
                Shape = new RectangleShape2D { Size = baseRect.Size },
                Position = baseRect.GetCenter(),
            });
            Rect2 head = SandboxPartLook.PistonHead(definition, 0.0f);
            _pistonHead = new CollisionShape2D
            {
                Shape = new RectangleShape2D { Size = head.Size },
                Position = head.GetCenter(),
            };
            AddChild(_pistonHead);
            int ticksPerSecond = Engine.PhysicsTicksPerSecond;
            _outTicks = Math.Max(1, (int)Math.Round(PistonOutSeconds * ticksPerSecond));
            _holdTicks = Math.Max(1, (int)Math.Round(PistonHoldSeconds * ticksPerSecond));
            _backTicks = Math.Max(1, (int)Math.Round(PistonBackSeconds * ticksPerSecond));
        }
        else
        {
            _shape = new CollisionShape2D
            {
                Shape = definition.Shape == SandboxPartShape.Circle
                    ? new CircleShape2D { Radius = definition.Radius }
                    : new RectangleShape2D { Size = new Vector2(definition.Width, definition.Height) },
            };
            AddChild(_shape);
        }
        if (definition.Device == SandboxDeviceKind.Button)
        {
            // The cap is solid: things rest on it, and whatever rests on it is pressing it.
            Rect2 cap = SandboxPartLook.ButtonCap(definition, 0.0f);
            _buttonCap = new CollisionShape2D
            {
                Shape = new RectangleShape2D { Size = cap.Size },
                Position = cap.GetCenter(),
            };
            AddChild(_buttonCap);
            _buttonReleaseTicks = Math.Max(1, (int)Math.Round(ButtonReleaseSeconds * Engine.PhysicsTicksPerSecond));
        }
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
        // A beam's length and thickness: the collision box follows, and the 3D model rebuilds
        // when it sees the new definition.
        SandboxPartDefinition sized = clamped.SizedDefinition(_baseDefinition);
        if (!ReferenceEquals(sized, _definition) && !sized.Equals(_definition))
        {
            _definition = sized;
            if (_shape?.Shape is RectangleShape2D box)
                box.Size = new Vector2(sized.Width, sized.Height);
        }
        MountedTool = clamped.MountedTool;
        Mass = clamped.MassFor(_definition);
        GravityScale = clamped.GravityScaleValue;
        PistonPush = clamped.PistonPushValue;

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

    /// <summary>The tool a Tool Mount holds, as a content ID, or null while it holds none.</summary>
    public string? MountedTool { get; private set; }

    /// <summary>A Piston's push speed, from its placement's setting.</summary>
    public float PistonPush { get; private set; } = SandboxPartOverrides.DefaultPistonPush;

    /// <summary>Starts a Piston's stroke. False while one is already under way, or for anything else.</summary>
    public bool StartPistonStroke()
    {
        if (_pistonHead is null || _strokeTick > 0)
            return false;
        _strokeTick = 1;
        SetPistonExtension(StrokeExtension(_strokeTick));
        return true;
    }

    /// <summary>
    /// One routed tick of this part's own motion: a Piston's stroke (the head shape moves with it)
    /// and a weapon mount's recoil settling back.
    /// </summary>
    public void AdvanceDevice()
    {
        if (_recoilTicks > 0)
        {
            _recoilTicks--;
            QueueRedraw();
        }
        if (_strokeTick == 0)
            return;
        _strokeTick++;
        if (_strokeTick > _outTicks + _holdTicks + _backTicks)
        {
            _strokeTick = 0;
            SetPistonExtension(0.0f);
            return;
        }
        SetPistonExtension(StrokeExtension(_strokeTick));
    }

    private float StrokeExtension(int tick) => StrokeCurve(tick, _outTicks, _holdTicks, _backTicks);

    /// <summary>The same stroke by elapsed time, for the Build preview; null once it is over.</summary>
    public static float? PistonStrokeAt(double seconds) =>
        seconds > PistonOutSeconds + PistonHoldSeconds + PistonBackSeconds
            ? null
            : StrokeCurve(seconds, PistonOutSeconds, PistonHoldSeconds, PistonBackSeconds);

    private static float StrokeCurve(double at, double outLength, double hold, double back)
    {
        if (at <= outLength)
        {
            float t = (float)(at / outLength);
            return 1.0f - (1.0f - t) * (1.0f - t); // fast off the mark, settling at full reach
        }
        if (at <= outLength + hold)
            return 1.0f;
        float b = Mathf.Clamp((float)((at - outLength - hold) / back), 0.0f, 1.0f);
        return 1.0f - b * b * (3.0f - 2.0f * b);
    }

    private void SetPistonExtension(float extension)
    {
        if (PistonExtension.Equals(extension))
            return;
        PistonExtension = extension;
        if (_pistonHead is not null)
            _pistonHead.Position = SandboxPartLook.PistonHead(_definition, extension).GetCenter();
        QueueRedraw();
    }

    /// <summary>
    /// One routed tick of a Button: <paramref name="touching"/> says whether anything is on its cap.
    /// True exactly when the Button goes down — the moment to send its pulse. It stays down while
    /// anything stays on it and comes back up once the cap has been clear for a moment.
    /// </summary>
    public bool UpdateButton(bool touching)
    {
        if (_buttonCap is null)
            return false;
        bool wentDown = false;
        if (touching)
        {
            _buttonIdleTicks = 0;
            wentDown = !_buttonHeld;
            _buttonHeld = true;
        }
        else if (_buttonHeld && ++_buttonIdleTicks >= _buttonReleaseTicks)
        {
            _buttonHeld = false;
        }
        if (_buttonFlashTicks > 0)
            _buttonFlashTicks--;
        SetButtonPress(_buttonHeld || _buttonFlashTicks > 0 ? 1.0f : 0.0f);
        return wentDown;
    }

    /// <summary>A click pressed it: the cap dips for a moment, as if a finger had.</summary>
    public void FlashButton()
    {
        if (_buttonCap is null)
            return;
        _buttonFlashTicks = _buttonReleaseTicks;
        SetButtonPress(1.0f);
    }

    /// <summary>Where the query for "something on the cap" looks: a band just over its top face.</summary>
    public Transform2D ButtonSensor(out Vector2 size)
    {
        Rect2 cap = SandboxPartLook.ButtonCap(_definition, 0.0f);
        size = new Vector2(cap.Size.X - 2.0f, SandboxPartLook.ButtonTravel + 4.0f);
        // From 3 px above the raised cap down to the pressed cap's top, so a body riding it down still counts.
        var centre = new Vector2(0.0f, cap.Position.Y - 3.0f + size.Y * 0.5f);
        return GlobalTransform * new Transform2D(0.0f, centre);
    }

    private void SetButtonPress(float press)
    {
        if (ButtonPress.Equals(press))
            return;
        ButtonPress = press;
        if (_buttonCap is not null)
            _buttonCap.Position = SandboxPartLook.ButtonCap(_definition, press).GetCenter();
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

    public override void _Draw()
    {
        if (!IsConfigured)
            return;

        SandboxPartLook.Draw(this, _definition, _drawsShape, _lit, DeviceMotion);

        if (_selected)
        {
            if (_definition.Shape == SandboxPartShape.Circle)
            {
                DrawArc(Vector2.Zero, _definition.Radius + 3.0f, 0.0f, Mathf.Tau, 40, SelectionOuter, 3.0f, true);
                DrawArc(Vector2.Zero, _definition.Radius + 1.0f, 0.0f, Mathf.Tau, 40, SelectionInner, 1.0f, true);
            }
            else
            {
                var rect = new Rect2(
                    new Vector2(-_definition.Width * 0.5f, -_definition.Height * 0.5f),
                    new Vector2(_definition.Width, _definition.Height));
                DrawRect(rect.Grow(3.0f), SelectionOuter, filled: false, 3.0f);
                DrawRect(rect.Grow(1.0f), SelectionInner, filled: false, 1.0f);
            }
        }

        // A nail through the middle: frozen parts do not move, and the player should see which.
        if (_frozen)
        {
            DrawCircle(Vector2.Zero, NailRadius, NailFill, true, -1.0f, true);
            DrawArc(Vector2.Zero, NailRadius, 0.0f, Mathf.Tau, 16, SandboxPartLook.OutlineColor, 1.5f, true);
        }
    }
}
