using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.App;

public partial class SandboxRoot
{
    /// <summary>
    /// Scene-enabled semantic persistence owner for focused composition roots such as Environment.
    /// Null on Initial Demo/itch/legacy fixtures. Runtime gameplay components should continue using
    /// the narrower progress bindings unless they genuinely own Scene-level composition.
    /// </summary>
    public SceneProgressCoordinator? SceneProgress => _runContext?.SceneProgress;

    /// <summary>Local Character document library for Scene-level composition; null on saveless fixtures.</summary>
    public CharacterStore? Characters => _runContext?.Characters;

    /// <summary>
    /// Account-global runtime seam for UI and entitlement systems. Scene builds route wallet,
    /// unlocks, statistics and extensions to PlayerProgressState; Initial Demo keeps the legacy
    /// aggregate. Callers cannot reach Buddy-local mood/hunger state through this binding.
    /// </summary>
    public PlayerRuntimeProgressBinding PlayerProgress =>
        _runContext?.PlayerProgress ?? new PlayerRuntimeProgressBinding(Progress);
}
