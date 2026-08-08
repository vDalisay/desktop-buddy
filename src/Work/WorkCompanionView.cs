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
    // Large enough for the mockup proportions while still behaving like a compact desktop
    // companion. The whole native window remains irregularly shaped by WindowsShape.cs.
    public static readonly Vector2I PreferredSize = new(720, 430);

    private const double ReactionSeconds = 0.11;
    private const float DragThreshold = 5.0f;

    private SandboxRoot _sandbox = null!;
    private Control _root = null!;
    private WorkCompanionArt _art = null!;
    private WorkCrtDisplay _counter = null!;
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

    private static readonly Rect2 BuddyHitRect = new(34, 62, 305, 292);
    private static readonly Rect2 CrtHitRect = new(462, 91, 174, 124);

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
        _counter.SetValue(_showLifetime ? lifetimeTotal : sessionTotal, _showLifetime);
    }

    public void SetCounterMode(bool showLifetime)
    {
        _showLifetime = showLifetime;
        if (GodotObject.IsInstanceValid(_counter))
            _counter.SetScope(showLifetime);
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
            // A tiny hover-only control is deliberately used instead of the previous large
            // "Motion: On" label, which visually competed with the companion art.
            _motionToggle.Text = enabled ? "II" : ">";
            _motionToggle.TooltipText = enabled
                ? "Pause buddy motion. Counters and rewards keep running."
                : "Resume buddy typing and click reactions.";
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

        _art = new WorkCompanionArt
        {
            Name = "WorkCompanionArt",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _art.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_art);

        _counter = new WorkCrtDisplay
        {
            Name = "WorkCrtCounter",
            Position = new Vector2(462, 91),
            Size = new Vector2(174, 124),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(_counter);

        _motionToggle = new Button
        {
            Name = "WorkMotionToggle",
            Position = new Vector2(12, 10),
            Size = new Vector2(31, 25),
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        _motionToggle.AddThemeFontSizeOverride("font_size", 11);
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
            Position = new Vector2(26, 55),
            Size = new Vector2(326, 305),
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(container);

        var viewport = new SubViewport
        {
            Size = new Vector2I(326, 305),
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
            Size = 228,
            Current = true,
        };
        world.AddChild(camera);
        world.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-30, -20, 0),
            LightEnergy = 0.82f,
        });
    }

    private void ApplyWorkPose()
    {
        if (!GodotObject.IsInstanceValid(_rig))
            return;

        // The pose is intentionally compact and upright: the buddy reads as seated behind the
        // keyboard rather than standing beside it. Feet stay low and hands hover over the keys.
        Vector2 torso = _source.ReadTransform(BuddyPartId.Torso).Position + new Vector2(-4, 20);
        Vector2 head = torso + new Vector2(0, -70);
        Vector2 leftHand = torso + new Vector2(-54, 36);
        Vector2 rightHand = torso + new Vector2(54, 36);
        Vector2 leftFoot = torso + new Vector2(-31, 83);
        Vector2 rightFoot = torso + new Vector2(31, 83);

        if (_animationsEnabled && _reactionRemaining > 0.0 && _reaction.HasValue)
        {
            if (_reaction == WorkActivityKind.MouseClick)
            {
                rightHand += new Vector2(18, 9);
            }
            else if (_typingSide == 0)
            {
                leftHand += new Vector2(0, -9);
                rightHand += new Vector2(0, 3);
            }
            else
            {
                rightHand += new Vector2(0, -9);
                leftHand += new Vector2(0, 3);
            }
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

    /// <summary>Original clean-room retro-computer illustration tuned to the approved mockup.</summary>
    private partial class WorkCompanionArt : Control
    {
        private static readonly Color Outline = new("#2E2B22");
        private static readonly Color BeigeLight = new("#E6DEB4");
        private static readonly Color Beige = new("#CDC59C");
        private static readonly Color BeigeDark = new("#A79E78");
        private static readonly Color DeskTop = new("#B8793F");
        private static readonly Color DeskLight = new("#D39A5A");
        private static readonly Color DeskDark = new("#6E4327");

        public override void _Draw()
        {
            DrawDesk();
            DrawComputer();
            DrawKeyboard();
            DrawMouse();
        }

        private void DrawDesk()
        {
            // Slim top with a darker front apron gives the same readable furniture silhouette
            // as the mockup without consuming most of the companion's vertical space.
            DrawRect(new Rect2(28, 346, 664, 49), DeskTop);
            DrawRect(new Rect2(28, 346, 664, 4), DeskLight);
            DrawRect(new Rect2(28, 391, 664, 6), DeskDark);
            DrawLine(new Vector2(28, 346), new Vector2(692, 346), Outline, 2.0f);
            DrawLine(new Vector2(28, 397), new Vector2(692, 397), Outline, 2.0f);

            // Front board/lip and two subtle legs, mostly hidden in normal taskbar placement.
            DrawRect(new Rect2(50, 397, 620, 20), new Color("#89532F"));
            DrawRect(new Rect2(58, 415, 54, 12), DeskDark);
            DrawRect(new Rect2(610, 415, 54, 12), DeskDark);
        }

        private void DrawComputer()
        {
            // CRT monitor body.
            DrawRect(new Rect2(435, 52, 236, 205), Outline);
            DrawRect(new Rect2(439, 56, 228, 197), BeigeLight);
            DrawRect(new Rect2(449, 67, 208, 174), Beige);

            // Recessed dark bezel and green glass. The number is drawn separately above it.
            DrawRect(new Rect2(455, 83, 188, 140), Outline);
            DrawRect(new Rect2(460, 88, 178, 130), new Color("#182019"));
            DrawRect(new Rect2(462, 91, 174, 124), new Color("#06250F"));

            // Monitor neck and swivel foot.
            DrawRect(new Rect2(530, 254, 45, 19), Outline);
            DrawRect(new Rect2(534, 254, 37, 18), BeigeDark);
            DrawRect(new Rect2(497, 270, 111, 15), Outline);
            DrawRect(new Rect2(501, 270, 103, 11), Beige);

            // Horizontal desktop PC chassis.
            DrawRect(new Rect2(430, 282, 244, 66), Outline);
            DrawRect(new Rect2(434, 286, 236, 58), BeigeLight);
            DrawRect(new Rect2(446, 300, 64, 12), BeigeDark);
            DrawRect(new Rect2(532, 299, 112, 25), Outline);
            DrawRect(new Rect2(537, 304, 102, 15), new Color("#B8B08A"));
            DrawLine(new Vector2(586, 304), new Vector2(586, 319), Outline, 2.0f);
            DrawRect(new Rect2(650, 327, 8, 6), new Color("#8EC64B"));
        }

        private void DrawKeyboard()
        {
            DrawRect(new Rect2(245, 324, 226, 49), Outline);
            DrawRect(new Rect2(249, 328, 218, 41), BeigeLight);

            const int columns = 12;
            const int rows = 3;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    float x = 258 + col * 16.2f + row * 2.0f;
                    float y = 333 + row * 10.2f;
                    DrawRect(new Rect2(x, y, 12.2f, 7.2f), Outline);
                    DrawRect(new Rect2(x + 1, y + 1, 10.2f, 5.2f), new Color("#EFE8C8"));
                }
            }

            DrawRect(new Rect2(297, 362, 105, 5), Outline);
            DrawRect(new Rect2(300, 362, 99, 3), new Color("#EFE8C8"));
        }

        private void DrawMouse()
        {
            Vector2 center = new(186, 354);
            DrawCircle(center, 23, Outline);
            DrawCircle(center, 20, BeigeLight);
            DrawLine(new Vector2(186, 334), new Vector2(186, 353), Outline, 2.0f);
            DrawLine(new Vector2(186, 352), new Vector2(202, 358), Outline, 1.5f);

            // Cable curves toward the keyboard using short clean segments; no per-frame state.
            Vector2[] cable =
            [
                new Vector2(202, 365),
                new Vector2(220, 374),
                new Vector2(235, 374),
                new Vector2(247, 367),
            ];
            for (int i = 0; i < cable.Length - 1; i++)
                DrawLine(cable[i], cable[i + 1], Outline, 2.0f);
        }
    }

    /// <summary>
    /// Lightweight seven-segment CRT renderer. It avoids rebuilding textures or relying on a
    /// desktop font, stays legible at very large lifetime totals, and keeps the mockup's green
    /// phosphor character instead of looking like a normal Win98 label.
    /// </summary>
    private partial class WorkCrtDisplay : Control
    {
        private static readonly bool[,] Segments =
        {
            { true,  true,  true,  true,  true,  true,  false }, // 0
            { false, true,  true,  false, false, false, false }, // 1
            { true,  true,  false, true,  true,  false, true  }, // 2
            { true,  true,  true,  true,  false, false, true  }, // 3
            { false, true,  true,  false, false, true,  true  }, // 4
            { true,  false, true,  true,  false, true,  true  }, // 5
            { true,  false, true,  true,  true,  true,  true  }, // 6
            { true,  true,  true,  false, false, false, false }, // 7
            { true,  true,  true,  true,  true,  true,  true  }, // 8
            { true,  true,  true,  true,  false, true,  true  }, // 9
        };

        private long _value;
        private bool _lifetime;

        public void SetValue(long value, bool lifetime)
        {
            value = Math.Max(0, value);
            if (_value == value && _lifetime == lifetime)
                return;
            _value = value;
            _lifetime = lifetime;
            QueueRedraw();
        }

        public void SetScope(bool lifetime)
        {
            if (_lifetime == lifetime)
                return;
            _lifetime = lifetime;
            QueueRedraw();
        }

        public override void _Draw()
        {
            string digits = _value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            float availableWidth = Math.Max(20.0f, Size.X - 18.0f);
            float availableHeight = Math.Max(20.0f, Size.Y - 33.0f);
            float digitWidth = Math.Min(28.0f, availableWidth / Math.Max(1, digits.Length));
            float digitHeight = Math.Min(64.0f, availableHeight);
            float totalWidth = digitWidth * digits.Length;
            float originX = (Size.X - totalWidth) * 0.5f;
            float originY = 19.0f + Math.Max(0.0f, (availableHeight - digitHeight) * 0.5f);
            float thickness = Math.Max(1.6f, digitWidth * 0.13f);

            Color glow = new(0.16f, 1.0f, 0.08f, 0.17f);
            Color lit = new(0.35f, 1.0f, 0.18f, 0.96f);
            for (int index = 0; index < digits.Length; index++)
            {
                int digit = digits[index] - '0';
                if (digit is < 0 or > 9)
                    continue;
                DrawDigit(
                    new Vector2(originX + index * digitWidth, originY),
                    digitWidth * 0.78f,
                    digitHeight,
                    thickness,
                    digit,
                    glow,
                    thickness * 2.7f);
                DrawDigit(
                    new Vector2(originX + index * digitWidth, originY),
                    digitWidth * 0.78f,
                    digitHeight,
                    thickness,
                    digit,
                    lit,
                    thickness);
            }

            string scope = _lifetime ? "LIFETIME" : "SESSION";
            ThemeDB.FallbackFont.DrawString(
                GetCanvasItem(),
                new Vector2(Size.X * 0.5f, Size.Y - 8.0f),
                scope,
                HorizontalAlignment.Center,
                88.0f,
                9,
                new Color(0.31f, 0.78f, 0.22f, 0.9f));
        }

        private void DrawDigit(
            Vector2 origin,
            float width,
            float height,
            float thickness,
            int digit,
            Color color,
            float drawThickness)
        {
            float x0 = origin.X;
            float x1 = origin.X + width;
            float y0 = origin.Y;
            float ym = origin.Y + height * 0.5f;
            float y1 = origin.Y + height;
            float inset = thickness * 0.8f;

            void Segment(int id, Vector2 from, Vector2 to)
            {
                if (Segments[digit, id])
                    DrawLine(from, to, color, drawThickness, antialiased: true);
            }

            Segment(0, new Vector2(x0 + inset, y0), new Vector2(x1 - inset, y0));
            Segment(1, new Vector2(x1, y0 + inset), new Vector2(x1, ym - inset));
            Segment(2, new Vector2(x1, ym + inset), new Vector2(x1, y1 - inset));
            Segment(3, new Vector2(x0 + inset, y1), new Vector2(x1 - inset, y1));
            Segment(4, new Vector2(x0, ym + inset), new Vector2(x0, y1 - inset));
            Segment(5, new Vector2(x0, y0 + inset), new Vector2(x0, ym - inset));
            Segment(6, new Vector2(x0 + inset, ym), new Vector2(x1 - inset, ym));
        }
    }
}
