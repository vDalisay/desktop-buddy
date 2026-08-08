using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Presentation3D;
using Godot;

namespace DesktopBuddy.Work;

/// <summary>
/// Transparent Work-mode composition: physics-free buddy preview, original retro PC/desk
/// drawing, CRT counter, drag gesture, motion toggle and double-click exit gesture.
/// </summary>
public partial class WorkCompanionView : CanvasLayer
{
    public static readonly Vector2I PreferredSize = new(560, 320);

    private const double ReactionSeconds = 0.11;
    private const float DragThreshold = 5.0f;

    private SandboxRoot _sandbox = null!;
    private Control _root = null!;
    private WorkCompanionArt _art = null!;
    private Label _counter = null!;
    private Label _counterMode = null!;
    private Button _motionToggle = null!;
    private BuddyVisualRigView _rig = null!;
    private StaticBuddyVisualTransformSource _source = null!;
    private CompiledCharacterAppearance? _appearanceOverride;
    private bool _showLifetime;
    private bool _animationsEnabled = true;
    private bool _dragCandidate;
    private bool _dragging;
    private Vector2 _dragOrigin;
    private int _typingSide;
    private WorkActivityKind? _reaction;
    private double _reactionRemaining;

    private static readonly Rect2 BuddyHitRect = new(18, 26, 235, 225);
    private static readonly Rect2 CrtHitRect = new(379, 58, 128, 82);

    public event Action? ExitRequested;
    public event Action? CounterModeToggleRequested;
    public event Action<bool>? AnimationPreferenceChanged;
    public event Action<Vector2I>? DraggedBy;
    public event Action? DragFinished;

    public bool ShowLifetime => _showLifetime;
    public bool AnimationsEnabled => _animationsEnabled;

    public void Configure(
        SandboxRoot sandbox,
        bool showLifetime,
        bool animationsEnabled,
        CompiledCharacterAppearance? appearanceOverride = null)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("WorkCompanionView must be configured before entering the tree.");
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _showLifetime = showLifetime;
        _animationsEnabled = animationsEnabled;
        _appearanceOverride = appearanceOverride;
        ProcessMode = ProcessModeEnum.Always;
        Layer = 200;
    }

    public override void _Ready()
    {
        BuildUi();
        BuildBuddyPreview();
        ApplyWorkPose();
        SetCounterMode(_showLifetime);
        SetAnimationsEnabled(_animationsEnabled, notify: false);
    }

    public override void _Process(double delta)
    {
        if (_reactionRemaining <= 0.0)
            return;
        _reactionRemaining = Math.Max(0.0, _reactionRemaining - delta);
        ApplyWorkPose();
    }

    public void SetCounter(long sessionTotal, long lifetimeTotal)
    {
        if (!GodotObject.IsInstanceValid(_counter))
            return;
        long value = _showLifetime ? lifetimeTotal : sessionTotal;
        string text = value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(_counter.Text, text, StringComparison.Ordinal))
            _counter.Text = text;
    }

    public void SetCounterMode(bool showLifetime)
    {
        _showLifetime = showLifetime;
        if (GodotObject.IsInstanceValid(_counterMode))
            _counterMode.Text = showLifetime ? "LIFETIME" : "SESSION";
    }

    public void NotifyActivity(WorkActivityKind kind)
    {
        if (!_animationsEnabled)
            return;
        _reaction = kind;
        _reactionRemaining = ReactionSeconds;
        if (kind == WorkActivityKind.KeyboardPress)
            _typingSide ^= 1;
        ApplyWorkPose();
    }

    public void SetAnimationsEnabled(bool enabled, bool notify = true)
    {
        _animationsEnabled = enabled;
        _reaction = null;
        _reactionRemaining = 0.0;
        if (GodotObject.IsInstanceValid(_motionToggle))
        {
            _motionToggle.ButtonPressed = enabled;
            _motionToggle.Text = enabled ? "Motion: On" : "Motion: Off";
            _motionToggle.TooltipText = enabled
                ? "Pause Work buddy motion while keeping counters active."
                : "Resume Work buddy typing and click reactions.";
        }
        if (GodotObject.IsInstanceValid(_rig))
            ApplyWorkPose();
        if (notify)
            AnimationPreferenceChanged?.Invoke(enabled);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!Visible || _root is null)
            return;

        if (input is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            Vector2 position = button.Position;
            if (button.Pressed)
            {
                if (CrtHitRect.HasPoint(position))
                {
                    if (!button.DoubleClick)
                        CounterModeToggleRequested?.Invoke();
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (button.DoubleClick && BuddyHitRect.HasPoint(position))
                {
                    ExitRequested?.Invoke();
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (!_motionToggle.GetGlobalRect().HasPoint(position))
                {
                    _dragCandidate = true;
                    _dragging = false;
                    _dragOrigin = position;
                }
            }
            else if (_dragCandidate)
            {
                bool wasDragging = _dragging;
                _dragCandidate = false;
                _dragging = false;
                if (wasDragging)
                    DragFinished?.Invoke();
            }
        }
        else if (input is InputEventMouseMotion motion && _dragCandidate)
        {
            Vector2 delta = motion.Position - _dragOrigin;
            if (!_dragging && delta.Length() >= DragThreshold)
                _dragging = true;
            if (_dragging)
            {
                Vector2 rounded = motion.Relative.Round();
                var step = new Vector2I((int)rounded.X, (int)rounded.Y);
                if (step != Vector2I.Zero)
                    DraggedBy?.Invoke(step);
            }
        }
    }

    private void BuildUi()
    {
        _root = new Control
        {
            Name = "WorkCompanionRoot",
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _art = new WorkCompanionArt { Name = "WorkCompanionArt", MouseFilter = Control.MouseFilterEnum.Ignore };
        _art.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_art);

        _counter = new Label
        {
            Name = "WorkCrtCounter",
            Position = new Vector2(384, 79),
            Size = new Vector2(118, 38),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _counter.AddThemeColorOverride("font_color", new Color(0.34f, 1.0f, 0.22f));
        _counter.AddThemeFontSizeOverride("font_size", 25);
        _root.AddChild(_counter);

        _counterMode = new Label
        {
            Name = "WorkCrtMode",
            Position = new Vector2(402, 118),
            Size = new Vector2(82, 18),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _counterMode.AddThemeColorOverride("font_color", new Color(0.28f, 0.72f, 0.22f));
        _counterMode.AddThemeFontSizeOverride("font_size", 9);
        _root.AddChild(_counterMode);

        _motionToggle = new Button
        {
            Name = "WorkMotionToggle",
            Position = new Vector2(8, 8),
            Size = new Vector2(94, 28),
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.All,
        };
        _motionToggle.Toggled += enabled => SetAnimationsEnabled(enabled);
        _root.AddChild(_motionToggle);

        // Hover tracking lives on the Window, not on _root: Godot emits mouse_exited on a
        // parent Control as soon as the pointer enters a child that accepts mouse input, so
        // hovering the toggle used to hide it out from under the click.
        _motionToggle.Visible = false;
        Window window = GetWindow();
        window.MouseEntered += ShowMotionToggle;
        window.MouseExited += HideMotionToggle;
        TreeExiting += () =>
        {
            if (GodotObject.IsInstanceValid(window))
            {
                window.MouseEntered -= ShowMotionToggle;
                window.MouseExited -= HideMotionToggle;
            }
        };
    }

    private void ShowMotionToggle()
    {
        if (GodotObject.IsInstanceValid(_motionToggle))
            _motionToggle.Visible = true;
    }

    private void HideMotionToggle()
    {
        if (GodotObject.IsInstanceValid(_motionToggle))
            _motionToggle.Visible = false;
    }

    private void BuildBuddyPreview()
    {
        var container = new SubViewportContainer
        {
            Name = "WorkBuddyPreview",
            Position = new Vector2(4, 24),
            Size = new Vector2(255, 230),
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(container);

        var viewport = new SubViewport
        {
            Size = new Vector2I(255, 230),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            OwnWorld3D = true,
        };
        container.AddChild(viewport);
        var world = new Node3D { ProcessMode = ProcessModeEnum.Always };
        viewport.AddChild(world);

        _source = new StaticBuddyVisualTransformSource(_sandbox.Buddy.Rig.Profile, Vector2.Zero, ":)");
        _rig = new BuddyVisualRigView
        {
            Name = "WorkBuddyRig",
            ProcessMode = ProcessModeEnum.Always,
        };
        _rig.Initialize(_sandbox.Buddy.VisualProfile, _source);
        world.AddChild(_rig);

        BuddyVisualRigView live = _sandbox.VisualPresenter.RigView;
        CompiledCharacterAppearance? appearance = _appearanceOverride ?? live.ActiveAppearance;
        if (appearance is not null)
            _rig.ApplyAppearance(appearance);
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            BuddyPartId part = (BuddyPartId)index;
            _rig.SetSurfaceUnderlay(part, live.SurfaceUnderlay(part));
        }

        var camera = new Camera3D
        {
            Position = new Vector3(0, 0, 600),
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 270,
            Current = true,
        };
        world.AddChild(camera);
        world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30, -20, 0) });
    }

    private void ApplyWorkPose()
    {
        if (!GodotObject.IsInstanceValid(_rig))
            return;

        Vector2 torso = _source.ReadTransform(BuddyPartId.Torso).Position + new Vector2(0, 16);
        Vector2 head = torso + new Vector2(0, -64);
        Vector2 leftHand = torso + new Vector2(-47, 27);
        Vector2 rightHand = torso + new Vector2(47, 27);
        Vector2 leftFoot = torso + new Vector2(-29, 71);
        Vector2 rightFoot = torso + new Vector2(29, 71);

        if (_animationsEnabled && _reactionRemaining > 0.0 && _reaction.HasValue)
        {
            if (_reaction == WorkActivityKind.MouseClick)
                rightHand += new Vector2(14, 7);
            else if (_typingSide == 0)
                leftHand += new Vector2(0, -7);
            else
                rightHand += new Vector2(0, -7);
        }

        BuddyVisualPartPose Pose(BuddyPartId id, Vector2 position)
        {
            var transform = new BuddyVisualTransform(position, 0.0f, Vector2.Zero);
            return new BuddyVisualPartPose(
                transform,
                WorldPlaneMapping.To3D(position),
                Vector3.Zero);
        }

        _rig.ApplyPose(new BuddyVisualPoseFrame(
            Pose(BuddyPartId.Head, head),
            Pose(BuddyPartId.Torso, torso),
            Pose(BuddyPartId.LeftHand, leftHand),
            Pose(BuddyPartId.RightHand, rightHand),
            Pose(BuddyPartId.LeftFoot, leftFoot),
            Pose(BuddyPartId.RightFoot, rightFoot),
            0.0f,
            BuiltInCharacterAppearance.NeutralFaceState,
            string.Empty,
            0.0f));
    }

    private partial class WorkCompanionArt : Control
    {
        public override void _Draw()
        {
            DrawRect(new Rect2(14, 235, 532, 50), new Color("#B77B43"));
            DrawRect(new Rect2(14, 235, 532, 4), new Color("#E6AE6B"));
            DrawRect(new Rect2(14, 282, 532, 5), new Color("#6D452A"));

            DrawRect(new Rect2(330, 176, 198, 58), new Color("#D7D0A7"));
            DrawRect(new Rect2(338, 184, 182, 42), new Color("#BDB690"));
            DrawRect(new Rect2(349, 30, 178, 145), new Color("#DDD6AD"));
            DrawRect(new Rect2(363, 47, 150, 105), new Color("#393B31"));
            DrawRect(new Rect2(379, 58, 128, 82), new Color("#082712"));
            DrawLine(new Vector2(408, 176), new Vector2(408, 188), new Color("#8D876A"), 8);

            DrawRect(new Rect2(164, 221, 183, 37), new Color("#D9D2AD"));
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 10; col++)
                    DrawRect(new Rect2(173 + col * 16, 226 + row * 9, 12, 6), new Color("#F0E9CB"));
            DrawCircle(new Vector2(130, 246), 18, new Color("#D9D2AD"));
            DrawLine(new Vector2(130, 246), new Vector2(162, 253), new Color("#7E775F"), 2);
        }
    }
}
