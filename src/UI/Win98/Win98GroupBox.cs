using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// The period group box: an etched hairline frame with its caption sitting on the top-left of
/// the frame line. Godot has no fieldset, so the caption is a label drawn over the border in a
/// small margin container rather than a gap cut into it.
/// </summary>
public partial class Win98GroupBox : PanelContainer
{
    /// <summary>The column callers add their rows to.</summary>
    public VBoxContainer Content { get; private set; } = null!;

    public void Configure(string caption)
    {
        AddThemeStyleboxOverride("panel", Win98ThemeFactory.Etched());

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        AddChild(column);

        var title = new Label
        {
            Name = "GroupCaption",
            Text = caption,
            // The caption breaks the frame line, so it needs the panel colour behind it.
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeStyleboxOverride("normal", Win98ThemeFactory.Flat(Win98ThemeFactory.Face));
        column.AddChild(title);

        Content = new VBoxContainer { Name = "GroupContent" };
        Content.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        Content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        column.AddChild(Content);
    }
}
