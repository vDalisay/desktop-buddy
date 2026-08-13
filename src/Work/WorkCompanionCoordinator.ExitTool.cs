using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Work;

public partial class WorkCompanionCoordinator
{
    private bool _exitToolHooked;

    private void EnsureExitToolHook()
    {
        if (_exitToolHooked)
            return;
        ActiveChanged += RestoreGrabAfterWorkExit;
        _exitToolHooked = true;
    }

    /// <summary>
    /// User-testing owner rule: leaving the dedicated Work companion is a deliberate return
    /// to direct buddy interaction, so it always starts from normal Grab instead of silently
    /// respawning whichever weapon/tool happened to be selected before Work Mode.
    /// </summary>
    private void RestoreGrabAfterWorkExit(bool active)
    {
        if (active || !GodotObject.IsInstanceValid(_sandbox) ||
            !GodotObject.IsInstanceValid(_sandbox.Pipeline) || !_sandbox.Pipeline.IsInitialized)
        {
            return;
        }

        _sandbox.Pipeline.SelectTool(ToolId.Grab);
    }
}
