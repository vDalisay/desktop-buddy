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

    /// <summary>
    /// Six lines: three fitted the longest authored sentence but left every shorter one
    /// crowded against the footer, and the box is fixed-height so it never jumps (owner
    /// instruction 2026-08-21). Panels with room to spare pass their own count.
    /// </summary>
    private const int DescriptionLines = 6;
    private const int DescriptionLineHeight = 18;

    public readonly record struct Parts(
        Label HeaderValue,
        VBoxContainer List,
        Label Status,
        Label Description);

    public static Parts Build(PanelContainer panel, string listName, int descriptionLines = DescriptionLines)
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

        // The balance sits above the list, where the player looks for it, rather than in the
        // footer beside the description (owner instruction 2026-08-21).
        var header = new HBoxContainer { Name = "PanelHeader" };
        column.AddChild(header);
        var value = new Label { Name = "PanelHeaderValue" };
        value.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(20));
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        value.AddThemeColorOverride("font_color", ValueGreen);
        header.AddChild(value);

        ScrollContainer scroll = FramedScroll(column, expand: true);
        var list = new VBoxContainer { Name = listName };
        list.AddThemeConstantOverride("separation", Win98ThemeFactory.Px(4));
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(list);

        column.AddChild(new HSeparator());

        // How the highlighted row is actually used. It reads far better here than in a tooltip
        // the player has to hover and wait for (owner feedback 2026-08-20).
        //
        // The box is a fixed three lines tall so the footer does not jump as the player moves
        // between a one-line tool and a three-line one, and it scrolls rather than clips when a
        // larger UI scale or a longer sentence overflows it. Px() carries the scale, so the
        // reserved height tracks the font instead of being pinned to one resolution.
        ScrollContainer descriptionScroll = FramedScroll(column, expand: false);
        descriptionScroll.Name = "PanelDescriptionScroll";
        descriptionScroll.GetParent<Control>().CustomMinimumSize =
            new Vector2(0, Win98ThemeFactory.Px(descriptionLines * DescriptionLineHeight));
        var description = new Label
        {
            Name = "PanelDescription",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        descriptionScroll.AddChild(description);

        var footer = new HBoxContainer();
        column.AddChild(footer);
        var status = new Label
        {
            Name = "PanelStatus",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        footer.AddChild(status);

        return new Parts(value, list, status, description);
    }

    /// <summary>
    /// A scrolling area inside its own recessed frame.
    ///
    /// <para>The Win98 theme gives <c>ScrollContainer</c> a two-pixel recessed border, but a
    /// ScrollContainer does not inset its content by its own stylebox — so a row scrolled to
    /// either edge painted its checkbox or dropdown straight over that border, top and bottom
    /// (owner report 2026-08-20). A PanelContainer does inset by its stylebox, so moving the
    /// frame out to a wrapper puts the scrolling content strictly inside it. Both clip, so
    /// nothing can reach the frame from within.</para>
    /// </summary>
    private static ScrollContainer FramedScroll(VBoxContainer column, bool expand)
    {
        var frame = new PanelContainer
        {
            Name = "PanelScrollFrame",
            ClipContents = true,
            SizeFlagsVertical = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
        };
        frame.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Light, 2));
        column.AddChild(frame);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ClipContents = true,
        };
        // The frame owns the border now; a second one inside it would double the bevel.
        scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        frame.AddChild(scroll);
        return scroll;
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
