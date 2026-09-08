using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.App;

/// <summary>
/// Dependencies with one lifetime per application run. The bootstrap composes
/// this once, before the sandbox enters the tree, and the sandbox only routes it.
///
/// <see cref="SceneProgress"/> is intentionally optional during the staged migration. Initial Demo,
/// itch.io and compatibility fixtures keep the legacy aggregate owner; Next Fest/Full Release can
/// inject the split account/Buddy/Scene owner without forcing every legacy UI call site to change in
/// the same commit.
/// </summary>
public sealed record RunContext(
    BuddyProgressState Progress,
    EconomyService Economy,
    IProgressStore ProgressStore,
    SaveCoordinator Saves,
    LocalSettingsSave Settings,
    SaveLoadStatus LoadStatus,
    IMonotonicTimeSource? TimeSource = null,
    CharacterSelectionState? CharacterSelection = null,
    CharacterStore? Characters = null,
    WorkProgressState? WorkProgress = null,
    EnvironmentProgressState? EnvironmentProgress = null,
    SceneProgressCoordinator? SceneProgress = null)
{
    public bool UsesSplitSceneProgress => SceneProgress is not null;
}
