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
        MinSize = new Vector2I(1, ToolbarSize.Y);
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
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Bar.AddThemeConstantOverride("separation", 6);
        panel.AddChild(Bar);
        CloseRequested += Show;
    }

    public void Attach(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.Reparent(Bar, keepGlobalTransform: false);
        control.Visible = true;
        control.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        control.Position = Vector2.Zero;
        control.ResetSize();
        control.MouseFilter = Control.MouseFilterEnum.Stop;
        Bar.QueueSort();
    }

    public void RaiseAboveOwner()
    {
        if (Visible)
            AlwaysOnTop = true;
    }

    public void Place(Rect2I mainWindowRect)
    {
        // Match the live owner bounds exactly. The toolbar must not expand beyond the compact
        // buddy window or center itself against a stale saved rectangle.
        int width = Math.Max(1, mainWindowRect.Size.X);
        Vector2I wantedSize = new(width, ToolbarSize.Y);
        if (Size != wantedSize)
        {
            MinSize = new Vector2I(1, ToolbarSize.Y);
            Size = wantedSize;
        }

        Position = new Vector2I(mainWindowRect.Position.X, mainWindowRect.Position.Y + 8);
        if (!Visible)
            Show();
    }
}
