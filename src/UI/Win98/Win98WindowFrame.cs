using System;
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
    public float ViewportOpacity { get; private set; } = 0.5f;
    public Rect2 ContentViewportRect =>
        GodotObject.IsInstanceValid(ContentHost) ? ContentHost.GetGlobalRect() : Rect2.Zero;

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
        MouseFilter = MouseFilterEnum.Pass;
        Theme = Win98ThemeFactory.Create();
        CustomMinimumSize = new Vector2(320, 240);

        AddThemeStyleboxOverride("panel", TransparentPanel());
        Build();
        SetViewportOpacity(0.5f);
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

    public void SetActive(bool active)
    {
        _titleBar.AddThemeStyleboxOverride(
            "panel",
            Win98ThemeFactory.Flat(active ? Win98ThemeFactory.ActiveTitle : Win98ThemeFactory.InactiveTitle));
    }

    public void SetViewportOpacity(float opacity) =>
        ViewportOpacity = Mathf.Clamp(opacity, 0f, 0.95f);

    private void Build()
    {
        var outer = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Pass,
        };
        outer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("margin_left", 0);
        outer.AddThemeConstantOverride("margin_top", 0);
        outer.AddThemeConstantOverride("margin_right", 0);
        outer.AddThemeConstantOverride("margin_bottom", 0);
        AddChild(outer);

        var column = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Pass,
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
            CustomMinimumSize = new Vector2(0, 22),
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

        AddChild(CreateResizeGrip("TopLeftGrip", ResizeTopLeft, CursorShape.Fdiagsize, Side.Left, Side.Top));
        AddChild(CreateResizeGrip("TopRightGrip", ResizeTopRight, CursorShape.Bdiagsize, Side.Right, Side.Top));
        AddChild(CreateResizeGrip("BottomLeftGrip", ResizeBottomLeft, CursorShape.Bdiagsize, Side.Left, Side.Bottom));
        AddChild(CreateResizeGrip("BottomRightGrip", ResizeBottomRight, CursorShape.Fdiagsize, Side.Right, Side.Bottom));
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
            MouseFilter = MouseFilterEnum.Pass,
        };
        row.AddThemeConstantOverride("separation", 2);
        bar.AddChild(row);

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
        row.AddChild(CommandButton("×", "Close", () => EmitSignal(SignalName.CloseRequested)));
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
        var grip = new Control
        {
            Name = name,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = cursor,
            CustomMinimumSize = new Vector2(16, 16),
            Size = new Vector2(16, 16),
            FocusMode = FocusModeEnum.None,
        };

        grip.SetAnchorsPreset(LayoutPreset.TopLeft);
        grip.AnchorLeft = horizontal == Side.Left ? 0f : 1f;
        grip.AnchorRight = horizontal == Side.Left ? 0f : 1f;
        grip.AnchorTop = vertical == Side.Top ? 0f : 1f;
        grip.AnchorBottom = vertical == Side.Top ? 0f : 1f;
        grip.OffsetLeft = horizontal == Side.Left ? 0f : -16f;
        grip.OffsetRight = horizontal == Side.Left ? 16f : 0f;
        grip.OffsetTop = vertical == Side.Top ? 0f : -16f;
        grip.OffsetBottom = vertical == Side.Top ? 16f : 0f;
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
