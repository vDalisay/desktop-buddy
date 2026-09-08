using DesktopBuddy.App;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Compile-time absent seam for distributions that do not ship achievements. The real rule engine,
/// local qualification state, publisher and Steam adapter are not compiled into those artifacts.
/// This node exists only so shared Steam/Workshop composition does not need a second source tree.
/// </summary>
public sealed partial class AchievementBootstrap : Node
{
    public void Configure(
        SandboxRoot sandbox,
        CharacterSelectionState? selection = null,
        CharacterStore? characters = null,
        object? environmentEvents = null,
        Node? initializedSteamBridge = null)
    {
        // Intentionally empty. Low-scope builds contain no achievement behavior or state.
    }
}
