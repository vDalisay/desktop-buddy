using System;
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
        // Focusable on purpose. An unfocusable bar cannot take activation back once the main
        // window has it, and stops receiving mouse events entirely — no hover, no clicks. The
        // shell already treats focus moving to an owned window as still using the game, so
        // taking focus here does not drop Play mode (DesktopShellController.ResolveFocusLoss).
        Unfocusable = false;
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

    /// <summary>
    /// Re-asserts topmost. The main window is always-on-top too, so activating it — any click
    /// inside the buddy box — raises it above this bar, which then renders behind the money
    /// counter. Toggling the flag reorders the bar without activating it, so raising it never
    /// steals focus from the game.
    /// </summary>
    public void RaiseAboveOwner()
    {
        if (!Visible)
            return;
        AlwaysOnTop = false;
        AlwaysOnTop = true;
    }

    public void Place(Rect2I mainWindowRect)
    {
        int x = mainWindowRect.Position.X +
            Math.Max(0, (mainWindowRect.Size.X - Size.X) / 2);
        int y = mainWindowRect.Position.Y + 8;
        Position = new Vector2I(x, y);
        if (!Visible)
            Show();
    }
}
