using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Final deterministic placement and sizing for character-management actions.</summary>
public partial class Win98PaintCharacterColumnBootstrap : Node
{
    private CharacterEditorHost? _host;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _host ??= GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        if (!GodotObject.IsInstanceValid(_host) || !_host!.IsEditorOpen)
            return;

        HidePager();
        EqualizeManagementButtons();
    }

    private void HidePager()
    {
        Button? previous = GetTree().Root.FindChild("PreviousButton", true, false) as Button;
        Button? next = GetTree().Root.FindChild("NextButton", true, false) as Button;
        Control? pager = previous?.GetParent() as Control ?? next?.GetParent() as Control;
        if (GodotObject.IsInstanceValid(pager))
        {
            pager!.Visible = false;
            pager.FocusMode = Control.FocusModeEnum.None;
        }
        if (GodotObject.IsInstanceValid(previous)) previous!.FocusMode = Control.FocusModeEnum.None;
        if (GodotObject.IsInstanceValid(next)) next!.FocusMode = Control.FocusModeEnum.None;
    }

    private void EqualizeManagementButtons()
    {
        Button duplicate = _host!.DuplicateButton;
        Button delete = _host.DeleteButton;
        Button randomize = _host.RandomizeButton;
        randomize.Visible = false;
        randomize.FocusMode = Control.FocusModeEnum.None;

        if (duplicate.GetParent() is not HBoxContainer row)
            return;
        duplicate.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        delete.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        duplicate.CustomMinimumSize = new Vector2(0, 30);
        delete.CustomMinimumSize = new Vector2(0, 30);
        row.AddThemeConstantOverride("separation", 2);
        foreach (Node child in row.GetChildren())
        {
            if (child != duplicate && child != delete && child is Control control)
                control.Visible = false;
        }
    }
}
