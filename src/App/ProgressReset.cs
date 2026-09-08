using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
#if DESKTOP_BUDDY_ACHIEVEMENTS
using DesktopBuddy.Domain.Achievements;
#endif
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.App;

/// <summary>
/// "Reset Progress": everything the player has built goes back to a first run — the gameplay
/// save, Work progression, the decorated room, and the characters they made (owner instruction
/// 2026-08-21). Machine-local settings are the one thing kept, because they are preferences
/// rather than progress. Scene-enabled achievement qualification is monotonic and is therefore
/// retained while partial counters/working values reset.
/// </summary>
public static class ProgressReset
{
    /// <summary>How many character documents the last reset removed, for logs and scenarios.</summary>
    public static int DeletedCharacterCount { get; private set; }

    public static BuddyProgressState CreateNewProgress(double cashPerPain)
    {
        ulong traitSeed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        BuddyTraits traits = BuddyTraits.Sample(new SeededRandomSource(traitSeed));
        return new BuddyProgressState(cashPerPain, traits: traits);
    }

    /// <summary>
    /// Resets a Scene-enabled run as one manifest generation. Already-qualified achievements survive
    /// in achievement-enabled builds because Full Release must reconcile awards qualified under the
    /// Next Fest Demo and Steam awards are not revocable through Reset Progress. Partial counters and
    /// working values are omitted from the fresh Player snapshot. Character documents are deleted
    /// only after the manifest commit succeeds because filesystem deletion cannot be rolled back.
    /// </summary>
    public static async Task<bool> ResetSceneAsync(
        SceneProgressCoordinator scenes,
        EconomyService? economy = null,
        Func<CancellationToken, Task<int>>? deleteCharacters = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        DeletedCharacterCount = 0;

#if DESKTOP_BUDDY_ACHIEVEMENTS
        IReadOnlyDictionary<string, string> qualifiedAchievements =
            AchievementProgressStore.PreserveQualifiedAchievementValues(scenes.Player.Extensions);
#else
        IReadOnlyDictionary<string, string> qualifiedAchievements =
            new Dictionary<string, string>(StringComparer.Ordinal);
#endif

        BuddyProgressState freshLegacy = CreateNewProgress(scenes.Player.CashPerPain);
        LegacyNextFestMigrationProjection fresh = LegacyNextFestMigrationPolicy.Project(
            freshLegacy.Snapshot(),
            activeCharacterId: null,
            new EnvironmentProgressSnapshot(0, new EnvironmentLayout(), []),
            new CanonicalRoomPosition(0.5f, 0.5f));
        if (qualifiedAchievements.Count > 0)
        {
            fresh = fresh with
            {
                Player = fresh.Player with
                {
                    Extensions = new ProgressExtensionData(Values: qualifiedAchievements),
                },
            };
        }

        try
        {
            await scenes.ResetToFreshGraphAsync(fresh, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }

        if (deleteCharacters is not null)
            DeletedCharacterCount = await deleteCharacters(token).ConfigureAwait(false);

        economy?.NotifyBalanceChanged();
        return true;
    }

    /// <summary>
    /// Resets gameplay, Work progression and optional character selection in place. One explicit
    /// durable write owns the transaction; a failed write restores all exact prior snapshots and
    /// never touches machine-local settings. This path is retained for Initial Demo/itch builds,
    /// which do not compose the Next Fest achievement system.
    /// </summary>
    public static async Task<bool> ResetAsync(
        BuddyProgressState progress,
        SaveCoordinator saves,
        EconomyService? economy = null,
        CharacterSelectionState? characterSelection = null,
        Func<CancellationToken, Task<int>>? deleteCharacters = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(saves);
        DeletedCharacterCount = 0;
        characterSelection ??= saves.CharacterSelection;
        WorkProgressState? workProgress = saves.WorkProgress;
        EnvironmentProgressState? environmentProgress = saves.EnvironmentProgress;

        ProgressSnapshot before = progress.Snapshot();
        CharacterSelectionSnapshot? selectionBefore = characterSelection?.Snapshot();
        WorkProgressSnapshot? workBefore = workProgress?.Snapshot();
        EnvironmentProgressSnapshot? environmentBefore = environmentProgress?.Snapshot();
        ProgressSnapshot fresh = CreateNewProgress(progress.CashPerPain).Snapshot();
        progress.Adopt(fresh with { Revision = before.Revision + 1 });
        characterSelection?.SetActiveForExplicitTransaction(null);
        if (workProgress is not null && workBefore.HasValue)
        {
            workProgress.Adopt(new WorkProgressSnapshot(
                workBefore.Value.Revision == long.MaxValue
                    ? long.MaxValue
                    : workBefore.Value.Revision + 1,
                default,
                Array.Empty<string>(),
                false));
        }
        if (environmentProgress is not null && environmentBefore.HasValue)
        {
            environmentProgress.Adopt(new EnvironmentProgressSnapshot(
                environmentBefore.Value.Revision == long.MaxValue
                    ? long.MaxValue
                    : environmentBefore.Value.Revision + 1,
                new EnvironmentLayout()));
        }

        try
        {
            await saves.FlushProgressAsync(force: true, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            progress.Adopt(before);
            if (characterSelection is not null && selectionBefore.HasValue)
                characterSelection.Restore(selectionBefore.Value);
            if (workProgress is not null && workBefore.HasValue)
                workProgress.Adopt(workBefore.Value);
            if (environmentProgress is not null && environmentBefore.HasValue)
                environmentProgress.Adopt(environmentBefore.Value);
            return false;
        }

        // Only after the durable write has succeeded: a failed reset restores every snapshot,
        // and there would be no restoring a character document that had already been deleted.
        // A delegate rather than the store itself, so this file stays engine-free for the
        // domain tests that compile it.
        if (deleteCharacters is not null)
            DeletedCharacterCount = await deleteCharacters(token).ConfigureAwait(false);

        economy?.NotifyBalanceChanged();
        return true;
    }
}
