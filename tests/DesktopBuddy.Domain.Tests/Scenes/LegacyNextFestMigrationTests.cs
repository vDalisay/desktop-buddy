using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Scenes;

public sealed class LegacyNextFestMigrationTests
{
    [Fact]
    public void Legacy_projection_is_retry_deterministic_across_account_buddy_and_scene_documents()
    {
        Guid characterId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var progress = new ProgressSnapshot(
            Revision: 9,
            BalanceMilliCredits: 123_000,
            SelectedToolId: ContentIds.ToolPistol,
            UnlockedToolIds: [ContentIds.ToolGrab, ContentIds.ToolPistol],
            Mood: -14.0f,
            HarmfulContentIds: [ContentIds.ToolPistol],
            Traits: new BuddyTraits(61),
            Statistics: new ProgressStatistics(3, 1, 2, 0, 50_000),
            Times: new CumulativeTimes(600, 500, 100),
            Extensions: null,
            FunInterest: null,
            Fullness: 40.0f);
        var stored = new DecorationDefinitionId("decoration.lamp.legacy");
        var environment = new EnvironmentProgressSnapshot(
            Revision: 6,
            Layout: new EnvironmentLayout(),
            OwnedUnplaced: [stored, stored]);
        var anchor = new CanonicalRoomPosition(0.5f, 0.7f);

        LegacyNextFestMigrationProjection first = LegacyNextFestMigrationPolicy.Project(
            progress,
            characterId,
            environment,
            anchor);
        LegacyNextFestMigrationProjection retry = LegacyNextFestMigrationPolicy.Project(
            progress,
            characterId,
            environment,
            anchor);

        Assert.Equal(first.Player.Revision, retry.Player.Revision);
        Assert.Equal(first.Player.BalanceMilliCredits, retry.Player.BalanceMilliCredits);
        Assert.Equal(first.Player.SelectedToolId, retry.Player.SelectedToolId);
        Assert.Equal(first.Player.UnlockedContentIds, retry.Player.UnlockedContentIds);
        Assert.Equal(first.Player.Statistics.ScoredImpacts, retry.Player.Statistics.ScoredImpacts);
        Assert.Equal(first.Player.Statistics.Knockouts, retry.Player.Statistics.Knockouts);
        Assert.Equal(first.Player.Statistics.CareAwards, retry.Player.Statistics.CareAwards);
        Assert.Equal(first.Player.Statistics.EarnedMilliCredits, retry.Player.Statistics.EarnedMilliCredits);
        Assert.Equal(first.Player.Times, retry.Player.Times);
        Assert.Equal(first.Player.Extensions, retry.Player.Extensions);

        Assert.Equal(BuddyIdentityId.LegacyPrimary, first.Buddy.BuddyIdentityId);
        Assert.Equal(first.Buddy.BuddyIdentityId, retry.Buddy.BuddyIdentityId);
        Assert.Equal(first.Buddy.Revision, retry.Buddy.Revision);
        Assert.Equal(first.Buddy.Mood, retry.Buddy.Mood);
        Assert.Equal(first.Buddy.Fullness, retry.Buddy.Fullness);
        Assert.Equal(first.Buddy.HarmfulContentIds, retry.Buddy.HarmfulContentIds);
        Assert.Equal(first.Buddy.Traits, retry.Buddy.Traits);
        Assert.Equal(characterId, first.Buddy.CharacterId);
        Assert.Equal(characterId, retry.Buddy.CharacterId);

        Assert.Equal(SceneId.LegacyHome, first.Scene.SceneId);
        Assert.Equal(first.Scene.SceneId, retry.Scene.SceneId);
        Assert.Equal(first.Scene.Name, retry.Scene.Name);
        Assert.Equal(6, first.Scene.EnvironmentRevision);
        Assert.Equal(new[] { stored, stored }, first.Scene.OwnedUnplaced);
        Assert.Equal(first.Scene.EnvironmentRevision, retry.Scene.EnvironmentRevision);
        Assert.Equal(first.Scene.OwnedUnplaced, retry.Scene.OwnedUnplaced);
        Assert.Equal(BuddyPlacementId.LegacyPrimary, first.Scene.BuddyPlacements[0].PlacementId);
        Assert.Equal(first.Scene.BuddyPlacements[0], retry.Scene.BuddyPlacements[0]);
    }

    [Fact]
    public void Legacy_projection_preserves_global_buddy_and_scene_ownership_boundaries()
    {
        var progress = new ProgressSnapshot(
            Revision: 4,
            BalanceMilliCredits: 77_000,
            SelectedToolId: ContentIds.ToolGrab,
            UnlockedToolIds: [ContentIds.ToolGrab, ContentIds.ToolPet],
            Mood: 33.0f,
            HarmfulContentIds: [ContentIds.ToolGrenade],
            Traits: BuddyTraits.Default,
            Statistics: new ProgressStatistics(7, 2, 4, 1, 22_000),
            Times: new CumulativeTimes(100, 80, 20),
            Extensions: null,
            FunInterest: null,
            Fullness: 65.0f);
        var stored = new DecorationDefinitionId("decoration.table.legacy");

        LegacyNextFestMigrationProjection projection = LegacyNextFestMigrationPolicy.Project(
            progress,
            activeCharacterId: null,
            new EnvironmentProgressSnapshot(3, new EnvironmentLayout(), [stored]),
            new CanonicalRoomPosition(0.45f, 0.7f));

        Assert.Equal(77_000, projection.Player.BalanceMilliCredits);
        Assert.Equal(progress.UnlockedToolIds, projection.Player.UnlockedContentIds);
        Assert.Equal(progress.Statistics, projection.Player.Statistics);

        Assert.Equal(33.0f, projection.Buddy.Mood);
        Assert.Equal(65.0f, projection.Buddy.Fullness);
        Assert.Equal(progress.HarmfulContentIds, projection.Buddy.HarmfulContentIds);
        Assert.Null(projection.Buddy.CharacterId);

        Assert.Equal(3, projection.Scene.EnvironmentRevision);
        Assert.Equal(new[] { stored }, projection.Scene.OwnedUnplaced);
        Assert.Single(projection.Scene.BuddyPlacements);
        Assert.Equal(projection.Buddy.BuddyIdentityId, projection.Scene.BuddyPlacements[0].BuddyIdentityId);
    }
}
