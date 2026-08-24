using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Applies distribution-only shell removals that cannot be expressed by catalogue filtering.
/// The itch build intentionally has no Work Mode: remove its hotkey, status autoload and both
/// legacy/current shell buttons. The node becomes inert immediately in every other build.
/// </summary>
public sealed partial class ItchDistributionScopeBootstrap : Node
{
    private bool _workCommandRemoved;
    private bool _legacyWorkCommandRemoved;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (!DemoScope.IsItchIo)
        {
            SetProcess(false);
            return;
        }

        if (InputMap.HasAction("toggle_input_mode"))
            InputMap.ActionEraseEvents("toggle_input_mode");

        Node? milestoneBootstrap = GetNodeOrNull<Node>("/root/WorkMilestoneProgressBootstrap");
        if (GodotObject.IsInstanceValid(milestoneBootstrap))
            milestoneBootstrap.QueueFree();
    }

    public override void _Process(double delta)
    {
        if (!_workCommandRemoved)
            _workCommandRemoved = HideControl("Win98WorkCommand");
        if (!_legacyWorkCommandRemoved)
            _legacyWorkCommandRemoved = HideControl("DockInteractionModeButton");

        if (_workCommandRemoved && _legacyWorkCommandRemoved)
            SetProcess(false);
    }

    private bool HideControl(string nodeName)
    {
        Control? control = GetTree().Root.FindChild(nodeName, true, false) as Control;
        if (!GodotObject.IsInstanceValid(control))
            return false;

        control.Visible = false;
        control.MouseFilter = Control.MouseFilterEnum.Ignore;
        control.FocusMode = Control.FocusModeEnum.None;
        return true;
    }
}
