using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;

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
    IMonotonicTimeSource? TimeSource = null);
