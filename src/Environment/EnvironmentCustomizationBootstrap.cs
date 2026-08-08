using Godot;

namespace DesktopBuddy.Environment;

/// <summary>
/// Reserved composition root for the environment-customization branch. It is intentionally
/// inert on the shared baseline: the branch may add Paint Background and Environment Decorator
/// composition here without touching project.godot or the shared command-bar bootstrap.
/// </summary>
public partial class EnvironmentCustomizationBootstrap : Node
{
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;
}
