using System;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Domain.Scenes;

/// <summary>
/// Pure destination projection for the Initial Demo -> Next Fest migration. It deliberately contains
/// no filesystem operations and no migration-complete flag: the persistence layer may mark migration
/// complete only after all projected documents have committed successfully.
/// </summary>
public readonly record struct LegacyNextFestMigrationProjection(
    PlayerProgressSnapshot Player,
    BuddyIdentitySnapshot Buddy,
    SceneDocument Scene);

public static class LegacyNextFestMigrationPolicy
{
    /// <summary>
    /// Projects one current schema-8-style one-Buddy aggregate into the new account/Buddy/Scene
    /// ownership model. Reserved IDs make this retry deterministic after a partial/failed disk write.
    /// Work progress remains account-global beside Player and is intentionally not copied here;
    /// background image bytes are likewise handled by the later atomic file-migration coordinator.
    /// </summary>
    public static LegacyNextFestMigrationProjection Project(
        in ProgressSnapshot legacyProgress,
        Guid? activeCharacterId,
        EnvironmentLayout legacyEnvironment,
        CanonicalRoomPosition buddyPosition)
    {
        ArgumentNullException.ThrowIfNull(legacyEnvironment);

        LegacyProgressPartition partition =
            LegacyProgressPartitionPolicy.Split(legacyProgress, activeCharacterId);
        SceneDocument scene = LegacySceneMigrationPolicy.CreateDefaultScene(
            partition.Buddy.BuddyIdentityId,
            legacyEnvironment,
            buddyPosition);

        return new LegacyNextFestMigrationProjection(
            partition.Player,
            partition.Buddy,
            scene);
    }
}
