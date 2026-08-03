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
    private const int BarPadding = 16;

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

        // Keep this child window in the owner's z-order group. Without transient ownership,
        // activating the buddy window can place it over the toolbar and make the controls
        // appear or behave as though they disappeared.
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

    /// <summary>
    /// Moves an existing dock control into this native window without retaining coordinates
    /// from the main window. Reparent's default keeps the global transform, which can leave a
    /// button hundreds of pixels outside this 48 px toolbar until another layout invalidation.
    /// </summary>
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

    /// <summary>
    /// Re-asserts topmost. Transient ownership normally keeps the toolbar above the buddy
    /// window; this remains as a defensive repair after platform window-flag changes.
    /// </summary>
    public void RaiseAboveOwner()
    {
        if (Visible)
            AlwaysOnTop = true;
    }

    public void Place(Rect2I mainWindowRect)
    {
        int content = (int)Math.Ceiling(Bar.GetCombinedMinimumSize().X) + BarPadding;
        int width = Math.Max(ToolbarSize.X, content);
        if (Size.X != width)
        {
            MinSize = new Vector2I(width, ToolbarSize.Y);
            Size = new Vector2I(width, ToolbarSize.Y);
        }

        int x = mainWindowRect.Position.X +
            Math.Max(0, (mainWindowRect.Size.X - Size.X) / 2);
        int y = mainWindowRect.Position.Y + 8;
        Position = new Vector2I(x, y);
        if (!Visible)
            Show();
    }
}
