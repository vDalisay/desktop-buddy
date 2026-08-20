using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Ui;

/// <summary>
/// The shared layout for a dock list panel: a padded column whose scrolling row list runs from
/// the very top, over a footer carrying the hovered row's description, the status line, and a
/// right-aligned value. Both the shop and the tool picker use it so they stay visually identical
/// without repeating the chrome.
///
/// <para>There is deliberately no heading label. The panel already sits in a Win98 frame whose
/// blue title bar names it, and printing the name twice cost the list a band of space at the
/// top where it is most useful (owner feedback 2026-08-20).</para>
/// </summary>
public static class PanelChrome
{
    /// <summary>Win98's own money green, matching the shell's balance readout.</summary>
    private static readonly Color ValueGreen = Color.Color8(0, 112, 0);

    public readonly record struct Parts(
        Label HeaderValue,
        VBoxContainer List,
        Label Status,
        Label Description);

    public static Parts Build(PanelContainer panel, string listName)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", Win98ThemeFactory.Px(12));
        margin.AddThemeConstantOverride("margin_right", Win98ThemeFactory.Px(12));
        margin.AddThemeConstantOverride("margin_top", Win98ThemeFactory.Px(10));
        margin.AddThemeConstantOverride("margin_bottom", Win98ThemeFactory.Px(10));
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", Win98ThemeFactory.Px(8));
        margin.AddChild(column);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);
        var list = new VBoxContainer { Name = listName };
        list.AddThemeConstantOverride("separation", Win98ThemeFactory.Px(4));
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(list);

        column.AddChild(new HSeparator());

        // How the highlighted row is actually used. It reads far better here than in a tooltip
        // the player has to hover and wait for (owner feedback 2026-08-20).
        var description = new Label
        {
            Name = "PanelDescription",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(description);

        var footer = new HBoxContainer();
        column.AddChild(footer);
        var status = new Label
        {
            Name = "PanelStatus",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        footer.AddChild(status);
        var value = new Label { Name = "PanelHeaderValue" };
        value.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(20));
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.AddThemeColorOverride("font_color", ValueGreen);
        footer.AddChild(value);

        return new Parts(value, list, status, description);
    }

    /// <summary>One list row: a name that takes the slack, a right-aligned value, an action.</summary>
    public static HBoxContainer Row(VBoxContainer list, string name, Label value, Control action)
    {
        // Pass, not Stop: the row reports hover for the description footer while its own button
        // keeps taking the clicks.
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        line.AddThemeConstantOverride("separation", Win98ThemeFactory.Px(8));
        list.AddChild(line);

        line.AddChild(new Label
        {
            Text = name,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(70), 0);
        line.AddChild(value);
        action.CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(84), 0);
        line.AddChild(action);
        return line;
    }
}
