using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.App;

/// <summary>
/// Dependencies with one lifetime per application run. The bootstrap composes
/// this once, before the sandbox enters the tree, and the sandbox only routes it.
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
    WorkProgressState? WorkProgress = null);