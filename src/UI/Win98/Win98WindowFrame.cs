using System;
using DesktopBuddy.Domain.Physics;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Responsive in-scene application frame. It deliberately emits commands rather than
/// owning native-window policy, keeping minimize/fullscreen/quit decisions in the shell.
/// </summary>
public partial class Win98WindowFrame : PanelContainer
{
    public const int ResizeTopLeft = 0;
    public const int ResizeTopRight = 1;
    public const int ResizeBottomLeft = 2;
    public const int ResizeBottomRight = 3;

    [Signal] public delegate void MinimizeRequestedEventHandler();
    [Signal] public delegate void MaximizeRestoreRequestedEventHandler();
    [Signal] public delegate void CloseRequestedEventHandler();
    [Signal] public delegate void TitleDragStartedEventHandler(Vector2 globalPointer);
    [Signal] public delegate void TitleDragMovedEventHandler(Vector2 globalPointer);
    [Signal] public delegate void TitleDragEndedEventHandler(Vector2 globalPointer);
    [Signal] public delegate void ResizeStartedEventHandler(int corner, Vector2 globalPointer);
    [Signal] public delegate void ResizeEndedEventHandler(int corner, Vector2 globalPointer);

    private Label _titleLabel = null!;
    private Label _statusLabel = null!;
    private PanelContainer _titleBar = null!;
    private bool _dragging;
    private bool _resizing;
    private int _resizeCorner = -1;

    public Control ContentHost { get; private set; } = null!;

    /// <summary>
    /// Title-bar command strip. The minimize/maximize/close boxes are its last three children, so
    /// a docked extra command (Help) inserts at <c>GetChildCount() - 3</c>.
    /// </summary>
    public HBoxContainer TitleBarCommands { get; private set; } = null!;
    /// <summary>Window-body tint in Compact; FullscreenOverlay uses <see cref="FullscreenOpacity"/>.</summary>
    public const float CompactOpacity = 1f;
    public const float FullscreenOpacity = 1f;

    public float ViewportOpacity { get; private set; } = CompactOpacity;
    public Rect2 ContentViewportRect =>
        GodotObject.IsInstanceValid(ContentHost) ? ContentHost.GetGlobalRect() : new Rect2();

    public string WindowTitle
    {
        get => _titleLabel?.Text ?? string.Empty;
        set
        {
            if (_titleLabel is not null)
                _titleLabel.Text = value;
        }
    }

    public string StatusText
    {
        get => _statusLabel?.Text ?? string.Empty;
        set
        {
            if (_statusLabel is not null)
                _statusLabel.Text = value;
        }
    }

    public override void _Ready()
    {
        // The frame is a full-rect chrome shell over lower CanvasLayers (buddy, editor). Only
        // its actual chrome — title bar, status bar, grips — may take the mouse: MouseFilter.Pass
        // would still win picking and propagate to ancestors only, so a Pass root swallows every
        // click meant for the layers underneath. Press/release for drag and resize are owned by
        // the title bar and grips themselves, so the root needs no events of its own.
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = Win98ThemeFactory.Create();
        CustomMinimumSize = new Vector2(
            RoomLayoutPolicy.MinimumRoomWidth,
            RoomLayoutPolicy.MinimumRoomHeight);

        AddThemeStyleboxOverride("panel", TransparentPanel());
        Build();
        SetViewportOpacity(CompactOpacity);
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (_dragging && inputEvent is InputEventMouseMotion motion)
        {
            EmitSignal(SignalName.TitleDragMoved, motion.GlobalPosition);
            AcceptEvent();
            return;
        }

        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } released)
            return;

        if (_dragging)
        {
            _dragging = false;
            EmitSignal(SignalName.TitleDragEnded, released.GlobalPosition);
            AcceptEvent();
        }

        if (_resizing)
        {
            int corner = _resizeCorner;
            _resizing = false;
            _resizeCorner = -1;
            EmitSignal(SignalName.ResizeEnded, corner, released.GlobalPosition);
            AcceptEvent();
        }
    }

    private bool _active = true;

    public void SetActive(bool active)
    {
        if (active == _active)
            return;
        _active = active;
        _titleBar.AddThemeStyleboxOverride(
            "panel",
            Win98ThemeFactory.Flat(active ? Win98ThemeFactory.ActiveTitle : Win98ThemeFactory.InactiveTitle));
    }

    public void SetViewportOpacity(float opacity) =>
        ViewportOpacity = Mathf.Clamp(opacity, 0f, 1f);

    private void Build()
    {
        var outer = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        outer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("margin_left", 0);
        outer.AddThemeConstantOverride("margin_top", 0);
        outer.AddThemeConstantOverride("margin_right", 0);
        outer.AddThemeConstantOverride("margin_bottom", 0);
        AddChild(outer);

        var column = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.AddThemeConstantOverride("separation", 0);
        outer.AddChild(column);

        _titleBar = BuildTitleBar();
        column.AddChild(_titleBar);

        ContentHost = new PanelContainer
        {
            Name = "ContentHost",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var borderStyle = Win98ThemeFactory.Recessed(Colors.Transparent, 1);
        borderStyle.DrawCenter = false;
        ContentHost.AddThemeStyleboxOverride("panel", borderStyle);
        column.AddChild(ContentHost);

        var status = new PanelContainer
        {
            Name = "Win98StatusBar",
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.StatusBarHeight),
            MouseFilter = MouseFilterEnum.Stop,
        };
        status.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 1));
        column.AddChild(status);

        _statusLabel = new Label
        {
            Name = "StatusText",
            Text = "Ready",
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        status.AddChild(_statusLabel);

        // Grips live under a plain Control: a Container child would be stretched to the
        // full rect, which is what made the resize cursor cover the whole window.
        var grips = new Control
        {
            Name = "ResizeGrips",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(grips);
        grips.AddChild(CreateResizeGrip("TopLeftGrip", ResizeTopLeft, CursorShape.Fdiagsize, Side.Left, Side.Top));
        grips.AddChild(CreateResizeGrip("TopRightGrip", ResizeTopRight, CursorShape.Bdiagsize, Side.Right, Side.Top));
        grips.AddChild(CreateResizeGrip("BottomLeftGrip", ResizeBottomLeft, CursorShape.Bdiagsize, Side.Left, Side.Bottom));
        grips.AddChild(CreateResizeGrip("BottomRightGrip", ResizeBottomRight, CursorShape.Fdiagsize, Side.Right, Side.Bottom));
    }

    private PanelContainer BuildTitleBar()
    {
        var bar = new PanelContainer
        {
            Name = "TitleBar",
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.TitleBarHeight),
            MouseDefaultCursorShape = CursorShape.Move,
            MouseFilter = MouseFilterEnum.Stop,
        };
        bar.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.ActiveTitle));
        bar.GuiInput += OnTitleBarInput;

        var row = new HBoxContainer
        {
            Name = "TitleBarRow",
            MouseFilter = MouseFilterEnum.Pass,
        };
        row.AddThemeConstantOverride("separation", 2);
        bar.AddChild(row);
        TitleBarCommands = row;

        var icon = new Label
        {
            Text = "▣",
            CustomMinimumSize = new Vector2(18, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.AddThemeColorOverride("font_color", Colors.White);
        row.AddChild(icon);

        _titleLabel = new Label
        {
            Text = "Desktop Buddy",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _titleLabel.AddThemeColorOverride("font_color", Colors.White);
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(_titleLabel);

        row.AddChild(CommandButton("_", "Minimize", () => EmitSignal(SignalName.MinimizeRequested)));
        row.AddChild(CommandButton("□", "Maximize or restore", () => EmitSignal(SignalName.MaximizeRestoreRequested)));
        Button close = CommandButton("×", "Close", () => EmitSignal(SignalName.CloseRequested));
        UiFeedbackAudioBootstrap.Tag(close, UiSfx.Exit);
        row.AddChild(close);
        return bar;
    }

    private static Button CommandButton(string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(20, 18),
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Stop,
        };
        button.Pressed += action;
        return button;
    }

    private Control CreateResizeGrip(string name, int corner, CursorShape cursor, Side horizontal, Side vertical)
    {
        const float grab = 8f;
        var grip = new Control
        {
            Name = name,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = cursor,
            CustomMinimumSize = new Vector2(grab, grab),
            Size = new Vector2(grab, grab),
            FocusMode = FocusModeEnum.None,
        };

        grip.SetAnchorsPreset(LayoutPreset.TopLeft);
        grip.AnchorLeft = horizontal == Side.Left ? 0f : 1f;
        grip.AnchorRight = horizontal == Side.Left ? 0f : 1f;
        grip.AnchorTop = vertical == Side.Top ? 0f : 1f;
        grip.AnchorBottom = vertical == Side.Top ? 0f : 1f;
        grip.OffsetLeft = horizontal == Side.Left ? 0f : -grab;
        grip.OffsetRight = horizontal == Side.Left ? grab : 0f;
        grip.OffsetTop = vertical == Side.Top ? 0f : -grab;
        grip.OffsetBottom = vertical == Side.Top ? grab : 0f;
        grip.GuiInput += inputEvent => OnResizeGripInput(corner, inputEvent);
        return grip;
    }

    private void OnTitleBarInput(InputEvent inputEvent)
    {
        if (_resizing || inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
            return;

        if (button.Pressed)
        {
            _dragging = true;
            EmitSignal(SignalName.TitleDragStarted, button.GlobalPosition);
        }
        else if (_dragging)
        {
            _dragging = false;
            EmitSignal(SignalName.TitleDragEnded, button.GlobalPosition);
        }

        AcceptEvent();
    }

    private void OnResizeGripInput(int corner, InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
            return;

        if (button.Pressed)
        {
            _resizing = true;
            _resizeCorner = corner;
            EmitSignal(SignalName.ResizeStarted, corner, button.GlobalPosition);
        }
        else if (_resizing && _resizeCorner == corner)
        {
            _resizing = false;
            _resizeCorner = -1;
            EmitSignal(SignalName.ResizeEnded, corner, button.GlobalPosition);
        }

        AcceptEvent();
    }

    private static StyleBoxFlat TransparentPanel()
    {
        var style = Win98ThemeFactory.Flat(Colors.Transparent);
        style.DrawCenter = false;
        style.BorderWidthLeft = 0;
        style.BorderWidthTop = 0;
        style.BorderWidthRight = 0;
        style.BorderWidthBottom = 0;
        style.ShadowSize = 0;
        return style;
    }
}
