using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Persistence;

public sealed partial class SceneProgressCoordinator
{
    /// <summary>
    /// Replaces the complete Scene-progress graph with one fresh reserved Home Scene and the
    /// reserved primary Buddy as one crash-consistent generation. Existing Player, Work, primary
    /// Buddy and Scene-library objects are mutated in place so live runtime bindings remain valid.
    /// A failure before the new manifest commit restores the exact previous graph and revisions.
    /// </summary>
    public async Task ResetToFreshGraphAsync(
        LegacyNextFestMigrationProjection fresh,
        CancellationToken token = default)
    {
        if (fresh.Buddy.BuddyIdentityId != BuddyIdentityId.LegacyPrimary)
            throw new ArgumentException("Scene reset requires the reserved primary Buddy identity.", nameof(fresh));
        if (fresh.Scene is null || fresh.Scene.SceneId != SceneId.LegacyHome)
            throw new ArgumentException("Scene reset requires the reserved Home Scene.", nameof(fresh));
        if (fresh.Scene.BuddyPlacements.Count != 1 ||
            fresh.Scene.BuddyPlacements[0].PlacementId != BuddyPlacementId.LegacyPrimary ||
            fresh.Scene.BuddyPlacements[0].BuddyIdentityId != BuddyIdentityId.LegacyPrimary)
        {
            throw new ArgumentException("Fresh Scene reset graph must contain only the reserved primary placement.", nameof(fresh));
        }

        // Finish any prior semantic generation before taking the rollback snapshot. This keeps the
        // saved-revision baselines aligned with exactly the state we restore if the reset write fails.
        await FlushAsync(force: true, token).ConfigureAwait(false);

        if (!_buddies.TryGetValue(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? primary) || primary is null)
        {
            throw new InvalidOperationException(
                "The staged production Scene runtime requires the reserved primary Buddy before reset.");
        }

        PlayerProgressSnapshot playerBefore = Player.Snapshot();
        WorkProgressSnapshot workBefore = Work.Snapshot();
        var buddiesBefore = _buddies.Values
            .OrderBy(buddy => buddy.BuddyIdentityId)
            .Select(buddy => new BuddyRollbackEntry(buddy, buddy.Snapshot()))
            .ToArray();
        SceneDocument[] scenesBefore = _scenes.Scenes.ToArray();
        SceneId activeSceneBefore = _scenes.ActiveSceneId;
        long sceneRevisionBefore = _sceneRevision;
        long identityRevisionBefore = _identityLibraryRevision;

        EnsureRevisionAvailable(playerBefore.Revision, "player");
        EnsureRevisionAvailable(workBefore.Revision, "Work");
        foreach (BuddyRollbackEntry entry in buddiesBefore)
            EnsureRevisionAvailable(entry.Snapshot.Revision, $"Buddy {entry.Snapshot.BuddyIdentityId}");

        long previousHomeEnvironmentRevision = -1;
        foreach (SceneDocument scene in scenesBefore)
        {
            if (scene.SceneId != SceneId.LegacyHome)
                continue;
            previousHomeEnvironmentRevision = scene.EnvironmentProgress.Revision;
            break;
        }
        if (previousHomeEnvironmentRevision == long.MaxValue)
            throw new InvalidOperationException("Home Scene environment revision is exhausted.");

        PlayerProgressSnapshot playerFresh = fresh.Player with
        {
            Revision = playerBefore.Revision + 1,
        };
        var workFresh = new WorkProgressSnapshot(
            workBefore.Revision + 1,
            default,
            Array.Empty<string>(),
            false,
            ActiveSession: null);
        BuddyIdentitySnapshot buddyFresh = fresh.Buddy with
        {
            Revision = primary.Revision + 1,
            CharacterId = null,
        };
        long environmentRevision = previousHomeEnvironmentRevision < 0
            ? 0
            : previousHomeEnvironmentRevision + 1;
        var environmentFresh = new EnvironmentProgressSnapshot(
            environmentRevision,
            new EnvironmentLayout(),
            Array.Empty<DecorationDefinitionId>());
        var homeFresh = new SceneDocument(
            SceneId.LegacyHome,
            fresh.Scene.Name,
            environmentFresh,
            fresh.Scene.BuddyPlacements);

        Player.Adopt(playerFresh);
        Work.Adopt(workFresh);
        primary.Adopt(buddyFresh);
        _buddies.Clear();
        _buddies.Add(BuddyIdentityId.LegacyPrimary, primary);
        _scenes.Adopt([homeFresh], SceneId.LegacyHome);
        Touch(ref _sceneRevision);
        Touch(ref _identityLibraryRevision);
        ValidateSceneBuddyReferences();

        try
        {
            await FlushAsync(force: true, token).ConfigureAwait(false);
        }
        catch
        {
            Player.Adopt(playerBefore);
            Work.Adopt(workBefore);
            _buddies.Clear();
            foreach (BuddyRollbackEntry entry in buddiesBefore)
            {
                entry.State.Adopt(entry.Snapshot);
                _buddies.Add(entry.Snapshot.BuddyIdentityId, entry.State);
            }
            _scenes.Adopt(scenesBefore, activeSceneBefore);
            _sceneRevision = sceneRevisionBefore;
            _identityLibraryRevision = identityRevisionBefore;
            ValidateSceneBuddyReferences();
            throw;
        }
    }

    private static void EnsureRevisionAvailable(long revision, string label)
    {
        if (revision == long.MaxValue)
            throw new InvalidOperationException($"{label} progress revision is exhausted.");
    }

    private sealed record BuddyRollbackEntry(
        BuddyIdentityState State,
        BuddyIdentitySnapshot Snapshot);
}
