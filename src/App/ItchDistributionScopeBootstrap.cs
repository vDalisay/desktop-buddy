using System;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Applies distribution-only shell removals that cannot be expressed by catalogue filtering.
/// The itch build intentionally has no Work Mode: remove its hotkey, status autoload and both
/// legacy/current shell buttons. In browser-WASM it also keeps a small startup watchdog alive so
/// CI cannot mistake an allocated but permanently grey canvas for a successful game boot.
/// </summary>
public sealed partial class ItchDistributionScopeBootstrap : Node
{
    private const ulong BrowserBootTimeoutMsec = 15_000;

    private bool _workCommandRemoved;
    private bool _legacyWorkCommandRemoved;
    private bool _browserBootWatchdogArmed;
    private ulong _browserBootDeadlineMsec;

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

        if (OperatingSystem.IsBrowser())
        {
            _browserBootWatchdogArmed = true;
            _browserBootDeadlineMsec = Time.GetTicksMsec() + BrowserBootTimeoutMsec;
        }
    }

    public override void _Process(double delta)
    {
        if (!_workCommandRemoved)
            _workCommandRemoved = HideControl("Win98WorkCommand");
        if (!_legacyWorkCommandRemoved)
            _legacyWorkCommandRemoved = HideControl("DockInteractionModeButton");

        if (_browserBootWatchdogArmed)
        {
            if (HasBootedSandbox())
            {
                _browserBootWatchdogArmed = false;
            }
            else if (Time.GetTicksMsec() >= _browserBootDeadlineMsec)
            {
                _browserBootWatchdogArmed = false;
                GD.PushError(
                    "RuntimeError: Desktop Buddy browser boot did not attach SandboxRoot within 15 seconds. " +
                    "Treat the Web smoke test as failed even if the Godot canvas exists.");
            }
        }

        if (_workCommandRemoved && _legacyWorkCommandRemoved && !_browserBootWatchdogArmed)
            SetProcess(false);
    }

    private bool HasBootedSandbox()
    {
        Node? bootstrap = GetNodeOrNull<Node>("/root/Bootstrap");
        if (!GodotObject.IsInstanceValid(bootstrap))
            return false;

        foreach (Node child in bootstrap.GetChildren())
        {
            if (child is SandboxRoot)
                return true;
        }

        return false;
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
