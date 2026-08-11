using System;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Classic Win98 modal frame: raised panel, flush blue title bar with an optional close box,
/// and a padded body. Shared by every paint-editor sub-window so they read as one system.
/// </summary>
public static class Win98Dialog
{
    public static PanelContainer Create(
        string name,
        string title,
        Vector2 size,
        out VBoxContainer body,
        Action? onClose = null,
        bool draggable = true)
    {
        var panel = new PanelContainer
        {
            Name = name,
            Visible = false,
            ProcessMode = Node.ProcessModeEnum.Always,
            CustomMinimumSize = size,
            Theme = Win98ThemeFactory.Create(),
        };
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -size.X / 2f;
        panel.OffsetTop = -size.Y / 2f;
        panel.OffsetRight = size.X / 2f;
        panel.OffsetBottom = size.Y / 2f;
        panel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 3));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        panel.AddChild(column);

        var titleBar = new PanelContainer { Name = "TitleBar" };
        titleBar.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.ActiveTitle));
        column.AddChild(titleBar);

        if (draggable)
            MakeDraggable(panel, titleBar);

        var titleRow = new HBoxContainer();
        titleBar.AddChild(titleRow);
        var label = new Label
        {
            Name = "Title",
            Text = title,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeColorOverride("font_color", Win98ThemeFactory.Light);
        titleRow.AddChild(label);

        if (onClose is not null)
        {
            var close = new Button
            {
                Name = "CloseBox",
                Text = "✕",
                TooltipText = "Close this window.",
                CustomMinimumSize = new Vector2(20, 18),
                FocusMode = Control.FocusModeEnum.All,
            };
            close.Pressed += onClose;
            titleRow.AddChild(close);
        }

        var margin = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        column.AddChild(margin);

        body = new VBoxContainer { Name = "Body" };
        body.AddThemeConstantOverride("separation", 10);
        margin.AddChild(body);
        return panel;
    }

    /// <summary>
    /// Classic title-bar drag. The panel is centre-anchored, so the drag shifts its offsets and
    /// the result is clamped to keep the title bar reachable inside the parent.
    /// </summary>
    private static void MakeDraggable(PanelContainer panel, Control grip)
    {
        bool dragging = false;
        Vector2 grab = Vector2.Zero;

        grip.MouseFilter = Control.MouseFilterEnum.Stop;
        grip.MouseDefaultCursorShape = Control.CursorShape.Move;
        grip.GuiInput += input =>
        {
            switch (input)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                    dragging = click.Pressed;
                    grab = click.Position;
                    break;
                case InputEventMouseMotion motion when dragging:
                    Vector2 delta = motion.Position - grab;
                    Vector2 limit = panel.GetParentAreaSize();
                    float left = Math.Clamp(
                        panel.OffsetLeft + delta.X,
                        -limit.X / 2f,
                        limit.X / 2f - 40f);
                    float top = Math.Clamp(
                        panel.OffsetTop + delta.Y,
                        -limit.Y / 2f,
                        limit.Y / 2f - 30f);
                    float width = panel.OffsetRight - panel.OffsetLeft;
                    float height = panel.OffsetBottom - panel.OffsetTop;
                    panel.OffsetLeft = left;
                    panel.OffsetTop = top;
                    panel.OffsetRight = left + width;
                    panel.OffsetBottom = top + height;
                    break;
            }
        };
    }

    /// <summary>Full-rect click blocker that keeps a dialog modal; reused across shows.</summary>
    public static Control Blocker(Control root, string name)
    {
        if (root.FindChild(name, false, false) is Control existing)
            return existing;

        var blocker = new ColorRect
        {
            Name = name,
            Color = new Color(0, 0, 0, 0.35f),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ProcessMode = Node.ProcessModeEnum.Always,
        };
        root.AddChild(blocker);
        blocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return blocker;
    }

    public static Button Action(BoxContainer row, string text, Action pressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(96, 30) };
        button.Pressed += pressed;
        row.AddChild(button);
        return button;
    }
}
