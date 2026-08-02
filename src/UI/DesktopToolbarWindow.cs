using System;
using DesktopBuddy.Domain.Platform;
using Godot;

namespace DesktopBuddy.Ui;

/// <summary>
/// The always-available horizontal mode/dock bar. It is a separate native window so the main
/// full-screen overlay can use whole-window mouse passthrough in Work mode without making the
/// recovery toggle unreachable.
/// </summary>
public partial class DesktopToolbarWindow : Window
{
    public static readonly Vector2I ToolbarSize = new(480, 48);

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
        Unfocusable = true;
        MousePassthrough = false;
        ProcessMode = ProcessModeEnum.Always;

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

    public void Place(Rect2I mainWindowRect, WindowLayoutMode layoutMode)
    {
        int x = mainWindowRect.Position.X +
            Math.Max(0, (mainWindowRect.Size.X - Size.X) / 2);
        int y = mainWindowRect.Position.Y + 8;
        Position = new Vector2I(x, y);
        if (!Visible)
            Show();
    }
}
