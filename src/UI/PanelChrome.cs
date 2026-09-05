using System;
using System.Collections.Generic;
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
    /// The floor, not the height. The box was fixed at six lines so it could never jump (owner
    /// instruction 2026-08-21); at 150% UI scale that reserved a band of empty white under
    /// every short sentence, so it now grows to its content and reserves only two lines
    /// (owner instruction 2026-09-06). Two rather than one so the common two-line description
    /// does not resize the footer as the player moves down a list.
    /// </summary>
    private const int MinimumDescriptionLines = 2;
    private const int DescriptionLineHeight = 18;

    public readonly record struct Parts(
        Label HeaderValue,
        VBoxContainer List,
        Label? Status,
        Label Description);

    /// <summary>
    /// <paramref name="status"/> adds a footer line under the description. Only the settings
    /// panel wants one, to prompt through a hotkey capture; the shop and tool lists echoed what
    /// the player had just done back at them, which said nothing the row did not already show
    /// (owner instruction 2026-08-22).
    /// </summary>
    public static Parts Build(
        PanelContainer panel,
        string listName,
        int minimumDescriptionLines = MinimumDescriptionLines,
        bool status = true)
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
        // The box wraps its sentence and stops there. A PanelContainer takes its height from
        // the Label's own wrapped minimum, so no scroll view is needed: text that would have
        // overflowed a fixed box simply makes the box a line taller. Px() carries the UI scale,
        // so the two-line floor tracks the font rather than one resolution.
        var descriptionFrame = new PanelContainer
        {
            Name = "PanelDescriptionFrame",
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
            CustomMinimumSize =
                new Vector2(0, Win98ThemeFactory.Px(minimumDescriptionLines * DescriptionLineHeight)),
        };
        descriptionFrame.AddThemeStyleboxOverride(
            "panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Light, 2));
        column.AddChild(descriptionFrame);

        var description = new Label
        {
            Name = "PanelDescription",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        descriptionFrame.AddChild(description);

        Label? statusLabel = null;
        if (status)
        {
            var footer = new HBoxContainer();
            column.AddChild(footer);
            statusLabel = new Label
            {
                Name = "PanelStatus",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            footer.AddChild(statusLabel);
        }

        return new Parts(value, list, statusLabel, description);
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
        // The row lives inside its own panel so a selected one can be painted like a list
        // selection without the name label having to carry the highlight itself.
        var frame = new PanelContainer { Name = "PanelRow", MouseFilter = Control.MouseFilterEnum.Pass };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        list.AddChild(frame);

        // Pass, not Stop: the row reports hover for the description footer while its own button
        // keeps taking the clicks.
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        line.AddThemeConstantOverride("separation", Win98ThemeFactory.Px(8));
        frame.AddChild(line);

        line.AddChild(new Label
        {
            Name = "PanelRowName",
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

    /// <summary>
    /// Hover-to-preview, click-to-lock behaviour for a list's description box. Hovering a row
    /// fills the description as it always did; clicking one selects it and pins its text there
    /// until that row is clicked again or another one is selected, so the player can read a
    /// description without keeping the pointer still (owner instruction 2026-08-22).
    /// </summary>
    public sealed class RowSelection
    {
        private static readonly StyleBoxEmpty Unselected = new();
        private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
        private readonly Label _description;
        private readonly Func<string, string> _describe;

        public RowSelection(Label description, Func<string, string> describe)
        {
            _description = description ?? throw new ArgumentNullException(nameof(description));
            _describe = describe ?? throw new ArgumentNullException(nameof(describe));
        }

        /// <summary>The locked row, or null when the list is following the pointer.</summary>
        public string? SelectedId { get; private set; }

        public void Add(string id, HBoxContainer line)
        {
            var row = new Row((PanelContainer)line.GetParent(), line.GetNode<Label>("PanelRowName"));
            _rows[id] = row;
            line.MouseEntered += () => Hover(id);
            line.GuiInput += inputEvent =>
            {
                if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                    Toggle(id);
            };
        }

        /// <summary>Fills the description unless a row is holding it.</summary>
        public void Hover(string id)
        {
            if (SelectedId is null)
                Describe(id);
        }

        public void Toggle(string id)
        {
            SelectedId = string.Equals(SelectedId, id, StringComparison.Ordinal) ? null : id;
            Describe(id);
            foreach ((string rowId, Row row) in _rows)
            {
                bool selected = string.Equals(SelectedId, rowId, StringComparison.Ordinal);
                if (!GodotObject.IsInstanceValid(row.Frame) || !GodotObject.IsInstanceValid(row.Name))
                    continue;
                row.Frame.AddThemeStyleboxOverride("panel",
                    selected ? Win98ThemeFactory.Flat(Win98ThemeFactory.Selection) : Unselected);
                row.Name.AddThemeColorOverride("font_color",
                    selected ? Win98ThemeFactory.Light : Win98ThemeFactory.Dark);
            }
        }

        private void Describe(string id)
        {
            string text = _describe(id);
            if (text.Length > 0 && GodotObject.IsInstanceValid(_description))
                _description.Text = text;
        }

        private readonly record struct Row(PanelContainer Frame, Label Name);
    }
}
