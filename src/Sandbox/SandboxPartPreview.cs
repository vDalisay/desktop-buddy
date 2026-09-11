using System;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Platform;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// A sunken well showing what is about to be placed, in 3D: a part as the room renders it, or a
/// rope, hinge, weld or wire doing its job between real part models (owner note 2026-09-11 — every
/// preview is the 3D thing, not a drawing).
///
/// <para>The preview can be played with (owner note 2026-09-11, "so it shows what actually
/// happens"): wood and metal knock with their sound, a Piston pushes, a Wheel spins when dragged,
/// a Button presses, a Timer ticks, a Lamp switches, a wired Button lights its Lamp, and a rope's
/// block or a hinge's arm can be grabbed, swung and yanked until the link snaps. The motion is a
/// small simulation of its own in the model's units — y up, pixels — tuned to read like the room,
/// not measured against it.</para>
/// </summary>
public partial class SandboxPartPreview : Control
{
    private const float Gravity = 900.0f;
    private const int Substeps = 8;
    private const float HandStiffness = 60.0f;
    private const float HandDamping = 12.0f;
    private const double BrokenSeconds = 1.3;

    /// <summary>Up, in the model's units (the room's 2D is y-down; the preview's 3D is y-up).</summary>
    private static readonly Vector2 ModelUp = new(0.0f, 1.0f);

    // A hinge preview's arm, per unit mass: its moment of inertia about the pin, and how hard the
    // grabbing hand turns it toward the pointer.
    private const float ArmInertia = 80.0f * 80.0f / 12.0f + SandboxPaletteModels.HingeArmCentre * SandboxPaletteModels.HingeArmCentre;
    private const float ArmHandStiffness = 90.0f;

    private static readonly string[] WoodKnocks = ["res://assets/sfx/bat/baseballbat_drop1.mp3", "res://assets/sfx/bat/baseballbat_drop2.mp3"];
    private static readonly string[] MetalKnocks = ["res://assets/sfx/gun/gun_drop1.mp3", "res://assets/sfx/gun/gun_drop2.mp3", "res://assets/sfx/gun/gun_drop3.mp3"];
    private static readonly string[] Clicks = ["res://assets/sfx/gun/toygun_trigger1.mp3", "res://assets/sfx/gun/toygun_trigger2.mp3"];

    private enum Demo { None, Knock, Wheel, Piston, Button, Timer, Lamp, Wire, Rope, Hinge }

    private SubViewportContainer? _container;
    private SandboxModelStage? _stage;
    private AudioStreamPlayer? _sound;
    private StyleBox? _well;

    private Demo _demo;
    private Node3D? _model;
    private Vector3 _home;
    private SandboxPartDefinition? _part;
    private string[] _knock = WoodKnocks;
    private float _strength = 1.0f;
    private float _elasticity;
    private float _stiffness;
    private Action? _rebuild;

    private bool _held;
    private Vector2 _hand;
    private Vector2 _grip;      // where on the held thing the hand took hold
    private Vector2 _position;  // rope: the knot on the block; a torn-off arm: its pin end
    private Vector2 _velocity;
    private float _angle;       // wheel turn, hinge arm angle, timer hand
    private float _spin;
    private float _restLength;
    private Vector2 _up = ModelUp;  // a rope's block: which way its cord runs
    private float _handHalf;       // a Timer's minute hand: half its length
    private float _press;       // a Button's cap, 0..1
    private bool _lit;
    private bool _running;
    private bool _broken;
    private double _clock;      // piston stroke time, timer interval, knock wobble, time since a snap

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _container = new SubViewportContainer
        {
            Name = "PreviewStage",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _container.OffsetLeft = _container.OffsetTop = 2.0f;
        _container.OffsetRight = _container.OffsetBottom = -2.0f;
        AddChild(_container);

        _stage = new SandboxModelStage(0.08f, 3.0f) { Name = "PreviewViewport", RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _container.AddChild(_stage);
        _sound = new AudioStreamPlayer { Name = "PreviewSound", Bus = AudioMix.Sfx, ProcessMode = ProcessModeEnum.Always, MaxPolyphony = 3 };
        AddChild(_sound);
        VisibilityChanged += () =>
        {
            if (_stage is not null)
                _stage.RenderTargetUpdateMode = IsVisibleInTree() ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        };
    }

    public void Show(SandboxPartDefinition? definition)
    {
        if (definition is null)
        {
            Stage(null, Demo.None, null);
            return;
        }
        _part = definition;
        _knock = definition.Material == SandboxPartMaterial.Metal ? MetalKnocks : WoodKnocks;
        Demo demo = definition.Device switch
        {
            SandboxDeviceKind.Piston => Demo.Piston,
            SandboxDeviceKind.Button => Demo.Button,
            SandboxDeviceKind.Timer => Demo.Timer,
            SandboxDeviceKind.Lamp => Demo.Lamp,
            SandboxDeviceKind.None when definition.Shape == SandboxPartShape.Circle => Demo.Wheel,
            SandboxDeviceKind.None => Demo.Knock,
            _ => Demo.None,
        };
        Stage(SandboxPaletteModels.ForPart(definition), demo, () => Show(definition));
    }

    public void ShowLink(SandboxLinkKind kind, float strength, float elasticity, float stiffness)
    {
        _strength = strength;
        _elasticity = elasticity;
        _stiffness = stiffness;
        _knock = MetalKnocks;
        Demo demo = kind switch
        {
            SandboxLinkKind.Rope => Demo.Rope,
            SandboxLinkKind.Hinge => Demo.Hinge,
            _ => Demo.Knock,
        };
        Stage(SandboxPaletteModels.ForLink(kind, strength, elasticity, stiffness), demo,
            () => ShowLink(kind, strength, elasticity, stiffness));
    }

    public void ShowWire(SandboxWireColor color) =>
        Stage(SandboxPaletteModels.ForWire(color), Demo.Wire, () => ShowWire(color));

    private void Stage(PaletteModel? model, Demo demo, Action? rebuild)
    {
        _stage?.Show(model);
        _model = model?.Node;
        _home = _model?.Position ?? Vector3.Zero;
        _demo = model is null ? Demo.None : demo;
        _rebuild = rebuild;
        _held = _broken = _lit = false;
        _running = true;
        _press = _angle = _spin = 0.0f;
        _velocity = Vector2.Zero;
        _clock = -1.0;
        if (_demo == Demo.Rope)
        {
            // The block starts hanging still; the rest length is what that sag leaves.
            _position = new Vector2(0.0f, -SandboxPaletteModels.RopeHang(_elasticity) + SandboxPaletteModels.RopeBlockHalfHeight);
            _restLength = SandboxPaletteModels.RopeAnchor.Y - _position.Y - Gravity / RopeStiffness();
        }
        _up = ModelUp;
        if (_demo == Demo.Hinge)
            _angle = Mathf.DegToRad(SandboxPaletteModels.HingeRestDegrees);
        if (_demo == Demo.Timer && _model?.GetNodeOrNull<Node3D>("MinuteHand") is { } minuteHand)
            _handHalf = minuteHand.Position.Y;

        MouseDefaultCursorShape = _demo == Demo.None ? CursorShape.Arrow : CursorShape.PointingHand;
        TooltipText = _demo switch
        {
            Demo.Knock => "Click to knock on it.",
            Demo.Wheel => "Drag to spin it.",
            Demo.Piston => "Click to fire it.",
            Demo.Button => "Click to press it.",
            Demo.Timer => "It ticks by itself. Click to stop or start it.",
            Demo.Lamp => "Click to switch it.",
            Demo.Wire => "Click the Button to send a pulse down the wire.",
            Demo.Rope => "Grab the block to swing it. Yank it to test the rope.",
            Demo.Hinge => "Grab the arm to swing it. Yank it to test the hinge.",
            _ => string.Empty,
        };
        Pose();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_demo == Demo.None || _stage is null || _container is null)
            return;
        if (@event is InputEventMouseMotion motion)
        {
            _hand = _stage.ToModel(motion.Position - _container.Position);
            if (_held)
                AcceptEvent();
            return;
        }
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
            return;

        _hand = _stage.ToModel(button.Position - _container.Position);
        AcceptEvent();
        if (!button.Pressed)
        {
            _held = false;
            return;
        }
        Press();
    }

    private void Press()
    {
        switch (_demo)
        {
            case Demo.Knock:
                Play(_knock);
                _clock = 0.0;
                break;
            case Demo.Wheel:
                _held = true;
                _grip = _hand;
                break;
            case Demo.Piston when _clock < 0.0:
                _clock = 0.0;
                break;
            case Demo.Button:
                _held = true;
                Play(Clicks);
                break;
            case Demo.Timer:
                _running = !_running;
                break;
            case Demo.Lamp:
                _lit = !_lit;
                Play(Clicks);
                break;
            case Demo.Wire when _hand.X < 0.0f:
                // The Button's half: the pulse runs down the wire and switches the Lamp.
                _held = true;
                _lit = !_lit;
                Play(Clicks);
                break;
            case Demo.Rope when !_broken && _hand.DistanceTo(BlockCentre()) < 24.0f:
                _held = true;
                _grip = _hand - _position;
                break;
            case Demo.Hinge when !_broken && OnArm(_hand, out float along):
                _held = true;
                _grip = new Vector2(Mathf.Max(12.0f, along), 0.0f);
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_demo == Demo.None || _model is null || !GodotObject.IsInstanceValid(_model))
            return;
        float dt = Mathf.Min((float)delta, 1.0f / 30.0f);
        switch (_demo)
        {
            case Demo.Knock when _clock >= 0.0:
                _clock += dt;
                if (_clock > 0.2)
                    _clock = -1.0;
                break;
            case Demo.Wheel:
                TurnWheel(dt);
                break;
            case Demo.Piston when _clock >= 0.0:
                _clock += dt;
                if (SandboxPartBody.PistonStrokeAt(_clock) is null)
                    _clock = -1.0;
                break;
            case Demo.Button or Demo.Wire:
                _press = Mathf.MoveToward(_press, _held ? 1.0f : 0.0f, dt / 0.06f);
                break;
            case Demo.Timer when _running:
                _clock = Math.Max(0.0, _clock) + dt;
                if (_clock >= SandboxPartOverrides.DefaultTimerSeconds)
                {
                    _clock = 0.0;
                    _angle -= Mathf.Tau / 12.0f;   // one step round the dial per pulse
                }
                break;
            case Demo.Rope:
                for (int step = 0; step < Substeps; step++)
                    SwingRope(dt / Substeps);
                break;
            case Demo.Hinge:
                for (int step = 0; step < Substeps; step++)
                    SwingArm(dt / Substeps);
                break;
        }
        if (_broken)
        {
            _clock += dt;
            if (_clock > BrokenSeconds)
                _rebuild?.Invoke();   // it snapped and fell: put up a fresh one to try again
        }
        Pose();
    }

    private void TurnWheel(float dt)
    {
        if (_held)
        {
            // The wheel turns as far as the pointer went round its hub.
            float turned = _grip.LengthSquared() > 4.0f && _hand.LengthSquared() > 4.0f ? _grip.AngleTo(_hand) : 0.0f;
            _grip = _hand;
            _angle += turned;
            _spin = Mathf.Lerp(_spin, turned / dt, 0.5f);
        }
        else
        {
            _angle += _spin * dt;
            _spin *= Mathf.Exp(-0.7f * dt);
        }
    }

    /// <summary>A rope of this stretch, per unit mass: a stretchier rope sags further under the same block.</summary>
    private float RopeStiffness() => Gravity / Mathf.Max(0.5f, 26.0f * _elasticity);

    private void SwingRope(float dt)
    {
        Vector2 force = new(0.0f, -Gravity);
        if (!_broken)
        {
            Vector2 toAnchor = SandboxPaletteModels.RopeAnchor - _position;
            float length = toAnchor.Length();
            if (length > _restLength && length > 0.01f)
            {
                Vector2 along = toAnchor / length;
                float k = RopeStiffness();
                float pull = k * (length - _restLength);
                float tension = Mathf.Max(0.0f, pull - 0.6f * Mathf.Sqrt(k) * _velocity.Dot(along));
                force += along * tension;
                if (_strength < 1.0f && pull > Gravity * (1.6f + 10.0f * _strength * _strength))
                    Snap();
            }
        }
        if (_held)
            force += HandStiffness * (_hand - _grip - _position) - HandDamping * _velocity;
        force -= 0.4f * _velocity;
        _velocity += force * dt;
        _position += _velocity * dt;
    }

    private void SwingArm(float dt)
    {
        if (_broken)
        {
            // Torn off: the arm tumbles away under gravity.
            _velocity += new Vector2(0.0f, -Gravity) * dt;
            _position += _velocity * dt;
            _angle += _spin * dt;
            return;
        }
        float springK = 470.0f * _stiffness * _stiffness;
        float rest = Mathf.DegToRad(SandboxPaletteModels.HingeRestDegrees);
        float torque = -Gravity * SandboxPaletteModels.HingeArmCentre * Mathf.Cos(_angle) / ArmInertia
            - springK * (_angle - rest)
            - (0.5f + 0.4f * Mathf.Sqrt(springK)) * _spin;
        if (_held)
        {
            Vector2 grabbed = Vector2.FromAngle(_angle) * _grip.X;
            torque += ArmHandStiffness * Mathf.AngleDifference(_angle, _hand.Angle()) - 10.0f * _spin;
            // The hand drags on the pin with how far the arm lags behind it: a weak hinge tears loose.
            if (_strength < 1.0f && _hand.DistanceTo(grabbed) > 14.0f + 110.0f * _strength * _strength)
            {
                Snap();
                _position = Vector2.Zero;
                Vector2 centre = Vector2.FromAngle(_angle) * SandboxPaletteModels.HingeArmCentre;
                _velocity = new Vector2(-centre.Y, centre.X) * _spin + (_hand - grabbed) * 4.0f;
                return;
            }
        }
        _spin += torque * dt;
        _angle += _spin * dt;
    }

    private void Snap()
    {
        _broken = true;
        _held = false;
        _clock = 0.0;
        Play(Clicks);
    }

    /// <summary>Brings the model up to the demo's state.</summary>
    private void Pose()
    {
        if (_model is null || !GodotObject.IsInstanceValid(_model))
            return;
        switch (_demo)
        {
            case Demo.Knock:
                float wobble = _clock >= 0.0 ? Mathf.Sin((float)_clock * 70.0f) * 2.0f * (1.0f - (float)_clock / 0.2f) : 0.0f;
                _model.Position = _home + new Vector3(wobble, 0.0f, 0.0f);
                break;
            case Demo.Wheel:
                _model.Rotation = new Vector3(0.0f, 0.0f, _angle);
                break;
            case Demo.Piston:
                SandboxPartLook.Pose(_model.GetNode<Node3D>("Part"), _part!, _clock >= 0.0 ? SandboxPartBody.PistonStrokeAt(_clock) ?? 0.0f : 0.0f, false);
                break;
            case Demo.Button:
                SandboxPartLook.Pose(_model, _part!, _press, false);
                break;
            case Demo.Lamp:
                SandboxPartLook.Pose(_model, _part!, 0.0f, _lit);
                break;
            case Demo.Timer:
                if (_model.GetNodeOrNull<Node3D>("MinuteHand") is { } hand)
                {
                    // The hand turns about the dial's centre, not its own middle.
                    Vector2 at = new Vector2(0.0f, _handHalf).Rotated(_angle);
                    hand.Position = new Vector3(at.X, at.Y, hand.Position.Z);
                    hand.Rotation = new Vector3(0.0f, 0.0f, _angle);
                }
                break;
            case Demo.Wire:
                SandboxPartLook.Pose(_model.GetNode<Node3D>("Button"), SandboxPaletteModels.ButtonDefinition, _press, false);
                SandboxPartLook.Pose(_model.GetNode<Node3D>("Lamp"), SandboxPaletteModels.LampDefinition, 0.0f, _lit);
                break;
            case Demo.Rope:
                // The block hangs in line with its cord; once the cord is gone it keeps its last lean.
                if (!_broken && _position.DistanceTo(SandboxPaletteModels.RopeAnchor) > 0.5f)
                    _up = (SandboxPaletteModels.RopeAnchor - _position).Normalized();
                var block = _model.GetNode<Node3D>("Block");
                Vector2 centre = BlockCentre();
                block.Position = new Vector3(centre.X, centre.Y, 0.0f);
                block.Rotation = new Vector3(0.0f, 0.0f, _up.Angle() - Mathf.Pi * 0.5f);
                var cord = _model.GetNode<Node3D>("Cord");
                cord.Visible = !_broken;
                if (!_broken)
                    SandboxPaletteModels.LayCord(cord, _position);
                break;
            case Demo.Hinge:
                var arm = _model.GetNode<Node3D>("Arm");
                arm.Position = _broken ? new Vector3(_position.X, _position.Y, 0.0f) : Vector3.Zero;
                arm.Rotation = new Vector3(0.0f, 0.0f, _angle);
                break;
        }
    }

    private Vector2 BlockCentre() => _position - _up * SandboxPaletteModels.RopeBlockHalfHeight;

    /// <summary>Whether a point lies on the hinge's arm, and how far along it from the pin.</summary>
    private bool OnArm(Vector2 point, out float along)
    {
        Vector2 local = point.Rotated(-_angle);
        along = local.X;
        return local.X > -8.0f && local.X < SandboxPaletteModels.HingeArmEnd + 6.0f && Mathf.Abs(local.Y) < 14.0f;
    }

    private void Play(string[] clips)
    {
        if (_sound is null || clips.Length == 0)
            return;
        string path = clips[(int)(GD.Randi() % (uint)clips.Length)];
        if (ResourceLoader.Exists(path) && ResourceLoader.Load<AudioStream>(path) is { } stream)
        {
            _sound.Stream = stream;
            _sound.PitchScale = (float)GD.RandRange(0.94, 1.06);
            _sound.Play();
        }
    }

    public override void _Draw() =>
        DrawStyleBox(_well ??= Win98ThemeFactory.Recessed(Colors.White, 2), new Rect2(Vector2.Zero, Size));
}
