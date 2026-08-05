using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Development-only visual calibration surface for the shared Win98 controls.</summary>
public partial class Win98ThemeShowcase : Control
{
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var backdrop = new ColorRect { Color = Color.Color8(0, 128, 128) };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var frame = new Win98WindowFrame
        {
            WindowTitle = "Desktop Buddy - UI Preview",
            StatusText = "Ready",
            Position = new Vector2(32, 28),
            Size = new Vector2(680, 440),
        };
        AddChild(frame);
        frame.Ready += () => Populate(frame);
    }

    private static void Populate(Win98WindowFrame frame)
    {
        frame.WindowTitle = "Desktop Buddy - UI Preview";
        frame.StatusText = "Win98 foundation: buttons, fields, lists and responsive chrome";

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        frame.ContentHost.AddChild(margin);

        var column = new VBoxContainer();
        margin.AddChild(column);
        column.AddChild(new Label { Text = "Shared controls" });

        var actions = new HBoxContainer();
        column.AddChild(actions);
        actions.AddChild(new Button { Text = "Normal", CustomMinimumSize = new Vector2(92, 24) });
        actions.AddChild(new Button { Text = "Default", CustomMinimumSize = new Vector2(92, 24) });
        actions.AddChild(new Button { Text = "Disabled", Disabled = true, CustomMinimumSize = new Vector2(92, 24) });

        column.AddChild(new LineEdit { Text = "Editable field" });

        var list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        list.AddItem("Buddy");
        list.AddItem("Paint editor");
        list.AddItem("Shop and tools");
        list.Select(0);
        column.AddChild(list);

        var checks = new HBoxContainer();
        checks.AddChild(new CheckBox { Text = "Always on top", ButtonPressed = true });
        checks.AddChild(new CheckBox { Text = "Sound effects" });
        column.AddChild(checks);
    }
}
