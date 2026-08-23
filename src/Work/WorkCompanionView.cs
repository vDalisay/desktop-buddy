using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Onboarding;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.UI;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Work;

/// <summary>
/// Transparent Work-mode composition: sideways physics-free buddy preview, supplied retro PC
/// art, CRT counter, drag gesture, motion toggle and double-click exit gesture.
/// </summary>
public partial class WorkCompanionView : CanvasLayer
{
    // Large enough for the mockup proportions while still behaving like a compact desktop
    // companion. The whole native window remains irregularly shaped by WindowsShape.cs.
    public static readonly Vector2I PreferredSize = new(720, 430);

    private const string ComputerTexturePath = "res://assets/work/retro_pc.png";
    private const string RetroShaderPath = "res://shaders/work_retro_filter.gdshader";
    private const double ReactionSeconds = 0.11;
    private const float DragThreshold = 5.0f;

    // The buddy's own committed facing yaw (BuddyExpressionProfile.FacingYawDegrees), so the
    // Work pose reads as the same sideways look it uses when walking toward something.
    private const float SidewaysYawRadians = Mathf.Pi / 6.0f;

    // Clears the 28-unit torso radius so both hands read as reaching in front of the body:
    // the yaw pushes the forward-reaching hands 13-20 units away from the camera, so the lane
    // has to pay that back before it buys any clearance. Depth only — ortho camera, so screen
    // position is unchanged.
    private const float HandDepthLane = 70.0f;

    private SandboxRoot _sandbox = null!;
    private Control _root = null!;
    private WorkCrtDisplay _counter = null!;
    private Control _controlLayer = null!;
    private Button _resizeButton = null!;
    private Button _motionToggle = null!;
    private Button _exitButton = null!;
    private BuddyVisualRigView _rig = null!;
    private StaticBuddyVisualTransformSource _source = null!;
    private CompiledCharacterAppearance? _appearanceOverride;
    private bool _showLifetime;
    private bool _animationsEnabled = true;
    private bool _retroFilter = true;
    private TextureRect _computerArt = null!;
    private SubViewportContainer _buddyPreview = null!;
    private bool _dragCandidate;
    private bool _dragging;
    private bool _pressedCrt;
    private Vector2 _dragOrigin;
    private double _reactionRemaining;
    private int _reactionSide = -1;
    private int _nextReactionSide;
    private Vector2I _lastWindowSize;
    private Vector2 _compositionOffset;
    private float _compositionScale = 1.0f;

    private static readonly Rect2 BuddyHitRect = new(228, 78, 152, 228);
    // Remapped onto the pixel-art PC (owner instruction 2026-08-23). The sprite is the 32x32
    // icon scaled 18x into the same 1024 canvas the retired illustration used, dropped on the
    // same footprint, so the composition around it is unchanged: only the glass and the sprite
    // bounds moved. CrtHitRect is exactly the icon's screen hole.
    private static readonly Rect2 CrtHitRect = new(442, 111, 145, 105);
    private static readonly Rect2 ComputerHitRect = new(393, 71, 259, 259);
    /// <summary>
    /// The hover controls are measured in window pixels, never composition pixels, and they
    /// live outside the scaled composition root. Shrinking the companion must not shrink its
    /// own controls into something unclickable (owner instruction 2026-08-20), so these are the
    /// same size at every window size.
    /// </summary>
    private const int ControlButtonSize = 52;
    private const int ControlButtonGap = 6;
    private const int ControlClusterInset = 6;
    private const int ControlButtonCount = 3;

    private static readonly Vector2I ControlClusterSize = new(
        (ControlButtonSize * ControlButtonCount) + (ControlButtonGap * (ControlButtonCount - 1)),
        ControlButtonSize);

    public event Action? ExitRequested;
    public event Action? CounterModeToggleRequested;
    public event Action<bool>? AnimationPreferenceChanged;
    public event Action? ResizeRequested;
    public event Action<Vector2I>? DraggedBy;
    public event Action? DragFinished;

    public bool ShowLifetime => _showLifetime;
    public bool AnimationsEnabled => _animationsEnabled;

    public void Configure(
        SandboxRoot sandbox,
        bool showLifetime,
        bool animationsEnabled,
        CompiledCharacterAppearance? appearanceOverride = null,
        bool retroFilter = true)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("WorkCompanionView must be configured before entering the tree.");
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _showLifetime = showLifetime;
        _animationsEnabled = animationsEnabled;
        _appearanceOverride = appearanceOverride;
        _retroFilter = retroFilter;
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
        SetRetroFilterEnabled(_retroFilter);
        SyncCompositionToWindow();
    }

    public override void _Process(double delta)
    {
        SyncCompositionToWindow();
        TickNativeWindowShape(delta);
        RefreshTutorialGates();
        if (_reactionRemaining <= 0.0)
            return;
        _reactionRemaining = Math.Max(0.0, _reactionRemaining - delta);
        ApplyWorkPose();
    }

    /// <summary>
    /// Greys out whichever controls the current Work step is not asking for. Re-derived every
    /// frame rather than toggled on entry and exit, so the buttons come back on their own when
    /// the walkthrough finishes or is skipped — no unlock step to forget.
    /// </summary>
    private void RefreshTutorialGates()
    {
        if (GodotObject.IsInstanceValid(_resizeButton))
            _resizeButton.Disabled = !TutorialInputGate.Allows(TutorialWorkControl.Resize);
        if (GodotObject.IsInstanceValid(_motionToggle))
            _motionToggle.Disabled = !TutorialInputGate.Allows(TutorialWorkControl.Motion);
        if (GodotObject.IsInstanceValid(_exitButton))
            _exitButton.Disabled = !TutorialInputGate.Allows(TutorialWorkControl.Exit);
    }

    /// <summary>
    /// Whether the CRT pass runs over the companion. One material is shared by the PC art and
    /// the buddy's viewport, so both wear the same look and neither can drift from the other;
    /// off simply clears it (owner instruction 2026-08-23).
    /// </summary>
    public void SetRetroFilterEnabled(bool enabled)
    {
        _retroFilter = enabled;
        ShaderMaterial? material = enabled ? LoadRetroMaterial() : null;
        if (GodotObject.IsInstanceValid(_computerArt))
            _computerArt.Material = material;
        if (GodotObject.IsInstanceValid(_buddyPreview))
            _buddyPreview.Material = material;
    }

    public bool RetroFilterEnabled => _retroFilter;

    private static ShaderMaterial LoadRetroMaterial()
    {
        Shader shader = GD.Load<Shader>(RetroShaderPath) ??
            throw new InvalidOperationException($"Missing Work Mode retro shader: {RetroShaderPath}");
        return new ShaderMaterial { Shader = shader };
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

    public void NotifyActivity(WorkActivityKind _, long count = 1)
    {
        if (!_animationsEnabled || count <= 0)
            return;
        _reactionSide = (int)((_nextReactionSide + count - 1) & 1);
        _nextReactionSide = (int)((_nextReactionSide + count) & 1);
        _reactionRemaining = ReactionSeconds;
        ApplyWorkPose();
    }

    public void SetAnimationsEnabled(bool enabled, bool notify = true)
    {
        _animationsEnabled = enabled;
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
            Vector2 position = ToCompositionPosition(button.Position);
            if (button.Pressed)
            {
                // While a Work step asks for one control, the others go quiet. Leaving them live
                // let the player exit Work Mode mid-lesson, which stranded the guidance window
                // on screen with nothing behind it (owner report 2026-08-21).
                if (button.DoubleClick && BuddyHitRect.HasPoint(position))
                {
                    if (!TutorialInputGate.Allows(TutorialWorkControl.Exit))
                        return;
                    ExitRequested?.Invoke();
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (IsDragSurface(position) && !IsOverControlButton(button.Position))
                {
                    _dragCandidate = true;
                    _dragging = false;
                    _pressedCrt = CrtHitRect.HasPoint(position);
                    _dragOrigin = button.Position;
                }
            }
            else if (_dragCandidate)
            {
                bool wasDragging = _dragging;
                bool toggleCounter = _pressedCrt && !wasDragging;
                _dragCandidate = false;
                _dragging = false;
                _pressedCrt = false;
                if (wasDragging)
                    DragFinished?.Invoke();
                else if (toggleCounter && TutorialInputGate.Allows(TutorialWorkControl.Counter))
                    CounterModeToggleRequested?.Invoke();
            }
        }
        else if (input is InputEventMouseMotion motion && _dragCandidate)
        {
            Vector2 delta = motion.Position - _dragOrigin;
            if (!_dragging && delta.Length() >= DragThreshold)
                _dragging = true;
            if (_dragging && TutorialInputGate.Allows(TutorialWorkControl.Drag))
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
            Size = PreferredSize,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        AddChild(_root);

        Texture2D computerTexture = GD.Load<Texture2D>(ComputerTexturePath) ??
            throw new InvalidOperationException($"Missing Work Mode computer art: {ComputerTexturePath}");
        _computerArt = new TextureRect
        {
            Name = "WorkComputerArt",
            Position = new Vector2(245, -40),
            Size = new Vector2(460, 460),
            Texture = computerTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            // Pixel art: the composition scales it by well under 1, and the default filter
            // turned every hard edge to mush.
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(_computerArt);

        _counter = new WorkCrtDisplay
        {
            Name = "WorkCrtCounter",
            Position = CrtHitRect.Position,
            Size = CrtHitRect.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(_counter);

        // Controls live outside the scaled composition root, in plain window pixels, and only
        // appear while the pointer is over the companion. The Win98 title strip they used to sit
        // on is gone: it was chrome the companion did not need, and the buttons on it shrank
        // with the window until they were too small to hit (owner instruction 2026-08-20).
        _controlLayer = new Control
        {
            Name = "WorkControlCluster",
            Size = ControlClusterSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_controlLayer);

        _resizeButton = BuildControlButton(
            "WorkResizeButton",
            slot: 0,
            "⤡",
            "Resize the Work companion.",
            Control.CursorShape.Fdiagsize);
        _resizeButton.ButtonDown += () => ResizeRequested?.Invoke();

        _motionToggle = BuildControlButton(
            "WorkMotionToggle",
            slot: 1,
            string.Empty,
            string.Empty,
            Control.CursorShape.PointingHand);
        _motionToggle.ToggleMode = true;
        _motionToggle.Toggled += enabled => SetAnimationsEnabled(enabled);

        _exitButton = BuildControlButton(
            "WorkExitButton",
            slot: 2,
            "✕",
            "Leave Work Mode (same as double-clicking the buddy).",
            Control.CursorShape.PointingHand);
        _exitButton.Pressed += () => ExitRequested?.Invoke();
        // The coordinator sounds the exit for both routes out of Work Mode; see HookWork.
        UiFeedbackAudioBootstrap.Tag(_exitButton, UiSfx.Silent);

        // Hover tracking lives on the Window, not on _root: Godot emits mouse_exited on a
        // parent Control as soon as the pointer enters a child that accepts mouse input, so
        // hovering the toggle used to hide it out from under the click.
        SetHoverControlsVisible(false);
        Window window = GetWindow();
        window.MouseEntered += ShowHoverControls;
        window.MouseExited += HideHoverControls;
        TreeExiting += () =>
        {
            if (GodotObject.IsInstanceValid(window))
            {
                window.MouseEntered -= ShowHoverControls;
                window.MouseExited -= HideHoverControls;
            }
        };
    }

    /// <summary>One fixed-size control in the hover cluster, laid out left to right.</summary>
    private Button BuildControlButton(
        string name,
        int slot,
        string text,
        string tooltip,
        Control.CursorShape cursor)
    {
        var button = new Button
        {
            Name = name,
            Position = new Vector2(slot * (ControlButtonSize + ControlButtonGap), 0),
            Size = new Vector2(ControlButtonSize, ControlButtonSize),
            Text = text,
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = cursor,
        };
        button.AddThemeFontSizeOverride("font_size", 22);
        ApplyWin98ButtonStyle(button);
        _controlLayer.AddChild(button);
        return button;
    }

    /// <summary>
    /// The cluster is pinned to the top-right of the drawn composition rather than the raw
    /// window, so it tracks the art through the letterboxing that keeps the companion's aspect.
    /// </summary>
    private void PositionControlCluster()
    {
        if (!GodotObject.IsInstanceValid(_controlLayer))
            return;

        _controlLayer.Position = ControlClusterOrigin();
    }

    /// <summary>
    /// Top-right of the drawn composition, so the cluster tracks the art through the
    /// letterboxing that keeps the companion's aspect — but never left of the window itself.
    /// The cluster does not scale, so on a companion narrower than the cluster the unclamped
    /// origin goes negative and the buttons walk off the left edge.
    /// </summary>
    private Vector2 ControlClusterOrigin()
    {
        Vector2 drawn = (Vector2)PreferredSize * _compositionScale;
        return new Vector2(
            Math.Max(0.0f, _compositionOffset.X + drawn.X - ControlClusterSize.X - ControlClusterInset),
            Math.Max(0.0f, _compositionOffset.Y + ControlClusterInset));
    }

    /// <summary>The cluster in window pixels, for the native window region.</summary>
    private Rect2I ControlClusterWindowRect()
    {
        Vector2 origin = ControlClusterOrigin();
        return new Rect2I(
            new Vector2I(Mathf.RoundToInt(origin.X), Mathf.RoundToInt(origin.Y)),
            ControlClusterSize);
    }

    private void ShowHoverControls() => SetHoverControlsVisible(true);

    private void HideHoverControls() => SetHoverControlsVisible(false);

    private void SetHoverControlsVisible(bool visible)
    {
        if (GodotObject.IsInstanceValid(_controlLayer))
            _controlLayer.Visible = visible;
        if (GodotObject.IsInstanceValid(_resizeButton))
            _resizeButton.Visible = visible;
        if (GodotObject.IsInstanceValid(_motionToggle))
            _motionToggle.Visible = visible;
        if (GodotObject.IsInstanceValid(_exitButton))
            _exitButton.Visible = visible;
    }

    private void SyncCompositionToWindow()
    {
        Vector2I size = GetWindow().Size;
        if (size == _lastWindowSize || size.X <= 0 || size.Y <= 0)
            return;
        _lastWindowSize = size;
        _compositionScale = Math.Max(0.01f, Math.Min(
            size.X / (float)PreferredSize.X,
            size.Y / (float)PreferredSize.Y));
        Vector2 drawn = (Vector2)PreferredSize * _compositionScale;
        _compositionOffset = ((Vector2)size - drawn) * 0.5f;
        if (GodotObject.IsInstanceValid(_root))
        {
            _root.Position = _compositionOffset;
            _root.Scale = Vector2.One * _compositionScale;
        }

        PositionControlCluster();
        ScheduleNativeWindowShapeRefresh();
    }

    // Buddy and the computer are the whole companion now that the title strip is gone; the
    // control buttons over them are excluded separately by IsOverControlButton.
    private static bool IsDragSurface(Vector2 compositionPosition) =>
        BuddyHitRect.HasPoint(compositionPosition) ||
        ComputerHitRect.HasPoint(compositionPosition);

    /// <summary>
    /// Whether the pointer is over one of the hover controls, and so must not start a drag or a
    /// wheel resize.
    ///
    /// <para>The visibility test is load-bearing, not a shortcut. The cluster is a fixed 168 px
    /// wide while the composition scales with the window, so on a small companion it is wider
    /// than the art it sits on — and an unconditional hit test then vetoed every click anywhere
    /// on the companion, killing drag and wheel resize outright. Controls nobody can see are
    /// controls nobody can click.</para>
    /// </summary>
    private bool IsOverControlButton(Vector2 windowPosition) =>
        GodotObject.IsInstanceValid(_controlLayer) && _controlLayer.Visible &&
        (_resizeButton.GetGlobalRect().HasPoint(windowPosition) ||
         _motionToggle.GetGlobalRect().HasPoint(windowPosition) ||
         _exitButton.GetGlobalRect().HasPoint(windowPosition));

    private static void ApplyWin98ButtonStyle(Button button)
    {
        button.AddThemeStyleboxOverride("normal", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        button.AddThemeStyleboxOverride("hover", Win98ThemeFactory.Raised(Win98ThemeFactory.Highlight, 2));
        button.AddThemeStyleboxOverride("pressed", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 2));
        button.AddThemeStyleboxOverride("hover_pressed", Win98ThemeFactory.Recessed(Win98ThemeFactory.Highlight, 2));
        button.AddThemeColorOverride("font_color", Win98ThemeFactory.Dark);
        button.AddThemeColorOverride("font_hover_color", Win98ThemeFactory.Dark);
        button.AddThemeColorOverride("font_hover_pressed_color", Win98ThemeFactory.Dark);
        button.AddThemeColorOverride("font_pressed_color", Win98ThemeFactory.Dark);
    }

    private Vector2 ToCompositionPosition(Vector2 windowPosition) =>
        (windowPosition - _compositionOffset) / _compositionScale;

    private Rect2I ScaleCompositionRect(Rect2I rect) => new(
        new Vector2I(
            Mathf.RoundToInt(_compositionOffset.X + rect.Position.X * _compositionScale),
            Mathf.RoundToInt(_compositionOffset.Y + rect.Position.Y * _compositionScale)),
        new Vector2I(
            Mathf.CeilToInt(rect.Size.X * _compositionScale),
            Mathf.CeilToInt(rect.Size.Y * _compositionScale)));

    private void BuildBuddyPreview()
    {
        _buddyPreview = new SubViewportContainer
        {
            Name = "WorkBuddyPreview",
            Position = new Vector2(18, 28),
            Size = new Vector2(400, 315),
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        SubViewportContainer container = _buddyPreview;
        _root.AddChild(container);

        var viewport = new SubViewport
        {
            Size = new Vector2I(400, 315),
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
            Size = 215,
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

        // Same sideways silhouette the buddy uses when it walks: the shared 30 degree facing
        // yaw, standing next to the PC with both hands reaching toward it. Every part offset
        // stays inside the connector clamp (surface gap below ConnectorMinimumLength), so no
        // stretched neck, arm or leg tube shows between the spheres.
        Vector2 torso = _source.ReadTransform(BuddyPartId.Torso).Position + new Vector2(49, 10);
        Vector2 head = torso + new Vector2(0, -50);
        Vector2 leftHand = torso + new Vector2(26, 22);
        Vector2 rightHand = torso + new Vector2(40, 16);
        Vector2 leftFoot = torso + new Vector2(-16, 40);
        Vector2 rightFoot = torso + new Vector2(16, 40);

        if (_animationsEnabled && _reactionRemaining > 0.0 && _reactionSide >= 0)
        {
            if (_reactionSide == 0)
            {
                leftHand += new Vector2(0, -7);
                rightHand += new Vector2(0, 3);
            }
            else
            {
                rightHand += new Vector2(0, -7);
                leftHand += new Vector2(0, 3);
            }
        }

        BuddyVisualPartPose Pose(BuddyPartId id, Vector2 position)
        {
            var transform = new BuddyVisualTransform(position, 0.0f, Vector2.Zero);
            Vector3 pivot = WorldPlaneMapping.To3D(torso);
            Vector3 flat = WorldPlaneMapping.To3D(position);
            Vector3 yawed = pivot + (new Basis(Vector3.Up, SidewaysYawRadians) * (flat - pivot));
            // Tucked hands would otherwise sink into the torso sphere: the yaw alone only
            // carries them ~20 units forward, less than the torso radius. The lane offset is
            // depth only, so the (2D-derived) connector geometry stays clamped and invisible.
            if (id is BuddyPartId.LeftHand or BuddyPartId.RightHand)
                yawed.Z += HandDepthLane;
            return new BuddyVisualPartPose(
                transform,
                yawed,
                new Vector3(0.0f, SidewaysYawRadians, 0.0f));
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
