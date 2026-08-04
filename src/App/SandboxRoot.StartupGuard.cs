using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Prevents the engine from entering the routed gameplay physics loop while the sandbox
/// composition root is still being assembled. If a child or dependency fails during
/// <see cref="SandboxRoot._Ready"/>, physics remains disabled so the first actionable startup
/// exception is not buried under one null-reference error per tick.
/// </summary>
public partial class SandboxRoot
{
    private bool _startupPhysicsEnabled;

    public override void _EnterTree()
    {
        SetPhysicsProcess(false);
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_startupPhysicsEnabled)
            return;

        bool ready =
            Pipeline is not null && GodotObject.IsInstanceValid(Pipeline) && Pipeline.IsInitialized &&
            VisualPresenter is not null && GodotObject.IsInstanceValid(VisualPresenter) &&
            VisualPresenter.IsInitialized &&
            Lifecycle is not null && GodotObject.IsInstanceValid(Lifecycle) &&
            TrayCommands is not null && GodotObject.IsInstanceValid(TrayCommands);

        if (!ready)
            return;

        _startupPhysicsEnabled = true;
        SetPhysicsProcess(true);
        SetProcess(false);
    }
}
