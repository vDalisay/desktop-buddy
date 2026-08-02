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
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var header = new HBoxContainer();
        column.AddChild(header);
        var heading = new Label { Text = title };
        heading.AddThemeFontSizeOverride("font_size", 20);
        heading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(heading);
        var value = new Label { Name = "PanelHeaderValue" };
        value.AddThemeFontSizeOverride("font_size", 20);
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
        list.AddThemeConstantOverride("separation", 4);
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
    public static HBoxContainer Row(VBoxContainer list, string name, Label value, Button action)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        list.AddChild(line);

        line.AddChild(new Label
        {
            Text = name,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.CustomMinimumSize = new Vector2(70, 0);
        line.AddChild(value);
        action.CustomMinimumSize = new Vector2(84, 0);
        line.AddChild(action);
        return line;
    }
}
