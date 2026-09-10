using System;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
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

    /// <summary>
    /// Account-global runtime seam. The value is intentionally created on demand rather than cached,
    /// so record copying cannot retain a binding for a different SceneProgress instance.
    /// </summary>
    public PlayerRuntimeProgressBinding PlayerProgress => CreatePlayerProgressBinding();

    /// <summary>
    /// Semantic autosave/flush seam for the active persistence model. Scene-enabled code must use
    /// this instead of reaching through to the compatibility <see cref="SaveCoordinator"/>.
    /// </summary>
    public IRunProgressPersistence RunProgressPersistence => CreateRunProgressPersistence();

    /// <summary>
    /// Compatibility progress binding for singular runtime/UI consumers that have not yet been
    /// migrated to explicit actor targeting. In a Scene run this is the first placement in the
    /// active Scene's durable order, never a reserved migration ID. The multi-Buddy host composes
    /// every placement separately; this seam only preserves old singular call sites during that
    /// migration.
    /// </summary>
    public BuddyRuntimeProgressBinding ActiveBuddyProgress
    {
        get
        {
            if (SceneProgress is null)
                return new BuddyRuntimeProgressBinding(Progress);

            SceneProgressBindingRegistry bindings = SceneProgress.CreateActiveBindings();
            if (bindings.Count > 0)
                return bindings.OrderedBindings[0].Progress;

            var buddies = SceneProgress.BuddyIdentities();
            if (buddies.Count == 0)
                throw new InvalidOperationException("Scene progress requires a Buddy identity for compatibility composition.");
            return new BuddyRuntimeProgressBinding(new BuddyProgressCoordinator(SceneProgress.Player, buddies[0]));
        }
    }

    /// <summary>
    /// Creates the account-global runtime seam for a consumer that must not depend on Buddy-local
    /// mood/hunger state merely to read or mutate wallet, unlock or lifetime progress.
    /// </summary>
    public PlayerRuntimeProgressBinding CreatePlayerProgressBinding() =>
        SceneProgress is not null
            ? new PlayerRuntimeProgressBinding(SceneProgress.Player)
            : new PlayerRuntimeProgressBinding(Progress);

    /// <summary>
    /// Creates the semantic autosave/flush seam for lifecycle and one-shot services. Scene-enabled
    /// code must use this instead of reaching through to the legacy SaveCoordinator, otherwise a
    /// migrated run could write the old aggregate format back over account-only progress.json.
    /// </summary>
    public IRunProgressPersistence CreateRunProgressPersistence() =>
        SceneProgress is not null
            ? new SceneRunProgressPersistence(SceneProgress)
            : new LegacyRunProgressPersistence(Saves);
}
