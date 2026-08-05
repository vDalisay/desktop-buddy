using System;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Responsive in-scene application frame. It deliberately emits commands rather than
/// owning native-window policy, keeping minimize/fullscreen/quit decisions in the shell.
/// </summary>
public partial class Win98WindowFrame : PanelContainer
{
    [Signal] public delegate void MinimizeRequestedEventHandler();
    [Signal] public delegate void MaximizeRestoreRequestedEventHandler();
    [Signal] public delegate void CloseRequestedEventHandler();
    [Signal] public delegate void TitleDragStartedEventHandler(Vector2 globalPointer);
    [Signal] public delegate void TitleDragMovedEventHandler(Vector2 globalPointer);
    [Signal] public delegate void TitleDragEndedEventHandler(Vector2 globalPointer);

    private Label _titleLabel = null!;
    private Label _statusLabel = null!;
    private PanelContainer _titleBar = null!;
    private bool _dragging;

    public Control ContentHost { get; private set; } = null!;

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

        // The root panel must never paint the theme's opaque default over gameplay.
        AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Colors.Transparent));
        Build();
        SetViewportOpacity(0.5f);
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (!_dragging)
            return;

        if (inputEvent is InputEventMouseMotion motion)
        {
            EmitSignal(SignalName.TitleDragMoved, motion.GlobalPosition);
            AcceptEvent();
        }
        else if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } released)
        {
            _dragging = false;
            EmitSignal(SignalName.TitleDragEnded, released.GlobalPosition);
            AcceptEvent();
        }
    }

    public void SetActive(bool active)
    {
        _titleBar.AddThemeStyleboxOverride(
            "panel",
            Win98ThemeFactory.Flat(active ? Win98ThemeFactory.ActiveTitle : Win98ThemeFactory.InactiveTitle));
    }

    /// <summary>Changes only the play-area fill; chrome remains fully opaque and readable.</summary>
    public void SetViewportOpacity(float opacity)
    {
        if (!GodotObject.IsInstanceValid(ContentHost))
            return;

        float alpha = Mathf.Clamp(opacity, 0f, 0.9f);
        Color fill = new(Win98ThemeFactory.Face.R, Win98ThemeFactory.Face.G, Win98ThemeFactory.Face.B, alpha);
        ContentHost.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(fill));
    }

    private void Build()
    {
        var outer = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Pass,
        };
        outer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("margin_left", 3);
        outer.AddThemeConstantOverride("margin_top", 3);
        outer.AddThemeConstantOverride("margin_right", 3);
        outer.AddThemeConstantOverride("margin_bottom", 3);
        AddChild(outer);

        var column = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Pass,
        };
        column.AddThemeConstantOverride("separation", 2);
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

    private void OnTitleBarInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
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
}
