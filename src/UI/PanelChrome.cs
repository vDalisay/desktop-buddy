using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Ui;

/// <summary>
/// The shared layout for a dock list panel: padded column, title row with a right-aligned
/// value, a scrolling list of rows, and a status line. Both the shop and the tool picker use
/// it so they stay visually identical without repeating the chrome.
/// </summary>
public static class PanelChrome
{
    public readonly record struct Parts(Label HeaderValue, VBoxContainer List, Label Status);

    public static Parts Build(PanelContainer panel, string title, string listName)
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

        var header = new HBoxContainer();
        column.AddChild(header);
        var heading = new Label { Text = title };
        heading.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(20));
        heading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(heading);
        var value = new Label { Name = "PanelHeaderValue" };
        value.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(20));
        value.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(value);

        column.AddChild(new HSeparator());

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
        var status = new Label
        {
            Name = "PanelStatus",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(status);

        return new Parts(value, list, status);
    }

    /// <summary>One list row: a name that takes the slack, a right-aligned value, an action.</summary>
    public static HBoxContainer Row(VBoxContainer list, string name, Label value, Control action)
    {
        var line = new HBoxContainer();
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
