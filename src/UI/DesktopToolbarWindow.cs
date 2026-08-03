using System;
using Godot;

namespace DesktopBuddy.Ui;

/// <summary>
/// Recovery and dock controls for the full-screen overlay only. Compact mode keeps its
/// controls inside the main window; this native window exists solely because full-screen Work
/// passes the main window through to the desktop.
/// </summary>
public partial class DesktopToolbarWindow : Window
{
    public static readonly Vector2I ToolbarSize = new(480, 48);
    private const int HorizontalPadding = 16;

    public HBoxContainer Bar { get; private set; } = null!;

    public void Configure()
    {
        Name = nameof(DesktopToolbarWindow);
        Title = "Desktop Buddy Controls";
        Size = ToolbarSize;
        MinSize = ToolbarSize;
        Borderless = true;
        Transparent = true;
        AlwaysOnTop = true;
        Unresizable = true;
        Unfocusable = false;
        MousePassthrough = false;
        ProcessMode = ProcessModeEnum.Always;
        Transient = true;
        TransientToFocused = false;
        Exclusive = false;

        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(panel);

        Bar = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        Bar.AddThemeConstantOverride("separation", 6);
        panel.AddChild(Bar);
        CloseRequested += Show;
    }

    public Button AddAction(string text, string name, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var button = new Button
        {
            Text = text,
            Name = name,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        button.Pressed += action;
        Bar.AddChild(button);
        return button;
    }

    public void RaiseAboveOwner()
    {
        if (Visible)
            AlwaysOnTop = true;
    }

    public void Place(Rect2I mainWindowRect)
    {
        int contentWidth = (int)Math.Ceiling(Bar.GetCombinedMinimumSize().X) + HorizontalPadding;
        int width = Math.Max(ToolbarSize.X, contentWidth);
        Vector2I wantedSize = new(width, ToolbarSize.Y);
        if (Size != wantedSize)
        {
            MinSize = wantedSize;
            Size = wantedSize;
        }

        int x = mainWindowRect.Position.X + Math.Max(0, (mainWindowRect.Size.X - width) / 2);
        int y = mainWindowRect.Position.Y + 8;
        Position = new Vector2I(x, y);
    }
}
