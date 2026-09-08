using DesktopBuddy.Persistence;

namespace DesktopBuddy.App;

public partial class SandboxRoot
{
    /// <summary>
    /// Scene-enabled semantic persistence owner for focused composition roots such as Environment.
    /// Null on Initial Demo/itch/legacy fixtures. Runtime gameplay components should continue using
    /// the narrower progress bindings unless they genuinely own Scene-level composition.
    /// </summary>
    public SceneProgressCoordinator? SceneProgress => _runContext?.SceneProgress;
}
