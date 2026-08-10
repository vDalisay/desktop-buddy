using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Environment;

public sealed class EnvironmentPersistenceTests
{
    private static readonly DecorationDefinition Lamp = new(new("decoration.lamp.classic"), "environment.lamp.classic",
        DecorationCategory.Lamp, 75_000, DecorationAnchorKind.Floor, new(true, 90), DecorationRenderBand.BehindBuddyFloor);

    [Fact]
    public async Task CommitWritesWalletAndLayoutInOneProgressAggregate()
    {
        var progress = FundedProgress(250_000);
        var environment = new EnvironmentProgressState();
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store, environment: environment);
        var session = new EnvironmentEditSession(environment.Layout, progress.BalanceMilliCredits, new DecorationCatalogue([Lamp]), () => Id(1));
        Assert.True(session.Place(Lamp.Id, new CanonicalRoomPosition(.25f, .8f)).Succeeded);

        await saves.CommitEnvironmentAsync(session);

        Assert.Equal(175_000, progress.BalanceMilliCredits);
        Assert.Single(environment.Layout.Decorations);
        Assert.Equal(175_000, store.Progress!.BalanceMilliCredits);
        Assert.Single(store.Progress.Environment.PlacedDecorations);
        Assert.False(saves.IsDirty);
    }

    [Fact]
    public async Task FailedCommitRestoresExactWalletAndEnvironmentSnapshots()
    {
        var progress = FundedProgress(250_000);
        var baseline = new EnvironmentLayout([Placed(Id(10), .2f)]);
        var environment = new EnvironmentProgressState(baseline, 4);
        var store = new InMemoryProgressStore { NextProgressFailure = new IOException("injected") };
        var saves = new SaveCoordinator(progress, store, environment: environment);
        var session = new EnvironmentEditSession(baseline, progress.BalanceMilliCredits, new DecorationCatalogue([Lamp]), () => Id(11));
        Assert.True(session.Place(Lamp.Id, new CanonicalRoomPosition(.6f, .8f)).Succeeded);
        progress.Deposit(1_000);
        ProgressSnapshot progressBefore = progress.Snapshot();
        EnvironmentProgressSnapshot environmentBefore = environment.Snapshot();

        await Assert.ThrowsAsync<IOException>(() => saves.CommitEnvironmentAsync(session));

        ProgressSnapshot progressAfter = progress.Snapshot();
        Assert.Equal(progressBefore.Revision, progressAfter.Revision);
        Assert.Equal(progressBefore.BalanceMilliCredits, progressAfter.BalanceMilliCredits);
        Assert.Equal(progressBefore.SelectedToolId, progressAfter.SelectedToolId);
        Assert.Equal(progressBefore.UnlockedToolIds, progressAfter.UnlockedToolIds);
        Assert.Equal(environmentBefore.Revision, environment.Revision);
        Assert.Equal(environmentBefore.Layout.Decorations, environment.Layout.Decorations);
        Assert.True(saves.IsDirty);
    }

    [Fact]
    public async Task WalletIncreaseDuringEditCommitsAgainstCurrentBalance()
    {
        var progress = FundedProgress(250_000);
        var environment = new EnvironmentProgressState();
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store, environment: environment);
        var session = new EnvironmentEditSession(environment.Layout, progress.BalanceMilliCredits, new DecorationCatalogue([Lamp]), () => Id(12));
        Assert.True(session.Place(Lamp.Id, new CanonicalRoomPosition(.5f, .8f)).Succeeded);
        progress.Deposit(1_000);

        await saves.CommitEnvironmentAsync(session);

        Assert.Single(environment.Layout.Decorations);
        Assert.Equal(176_000, progress.BalanceMilliCredits);
        Assert.Equal(176_000, store.Progress!.BalanceMilliCredits);
    }

    [Fact]
    public async Task WalletDecreaseBelowStagedCostRejectsWithoutMutation()
    {
        var progress = FundedProgress(75_000);
        var environment = new EnvironmentProgressState();
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store, environment: environment);
        var session = new EnvironmentEditSession(environment.Layout, progress.BalanceMilliCredits, new DecorationCatalogue([Lamp]), () => Id(13));
        Assert.True(session.Place(Lamp.Id, new CanonicalRoomPosition(.5f, .8f)).Succeeded);
        progress.Adopt(progress.Snapshot() with { Revision = progress.Revision + 1, BalanceMilliCredits = 74_000 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => saves.CommitEnvironmentAsync(session));

        Assert.Empty(environment.Layout.Decorations);
        Assert.Equal(74_000, progress.BalanceMilliCredits);
        Assert.Null(store.Progress);
    }

    [Fact]
    public void ProgressRoundTripPreservesResolvedAndUnresolvedRecords()
    {
        var unresolved = new PlacedDecoration(Id(20), new DecorationDefinitionId("decoration.retired.unknown"),
            new CanonicalRoomPosition(.7f, .3f), 0, DecorationRenderBand.WallDecoration, 25_000);
        var environment = new EnvironmentProgressState(new EnvironmentLayout([Placed(Id(21), .2f), unresolved]), 9);
        ProgressSave save = ProgressSave.FromSnapshot(FundedProgress(50_000).Snapshot(), environment: environment.Snapshot());

        SaveDecodeResult decoded = ProgressSavePolicy.Decode(ProgressSavePolicy.Serialize(save));
        EnvironmentProgressState restored = decoded.Save!.Environment.CreateState();

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal(9, restored.Revision);
        Assert.Equal(environment.Layout.Decorations, restored.Layout.Decorations);
    }

    [Fact]
    public void ProgressRoundTripPreservesOwnedStorage()
    {
        DecorationDefinitionId lamp = new("decoration.lamp.classic");
        var environment = new EnvironmentProgressState(new EnvironmentLayout(), 3, [lamp, lamp]);
        ProgressSave save = ProgressSave.FromSnapshot(FundedProgress(50_000).Snapshot(), environment: environment.Snapshot());

        SaveDecodeResult decoded = ProgressSavePolicy.Decode(ProgressSavePolicy.Serialize(save));
        EnvironmentProgressState restored = decoded.Save!.Environment.CreateState();

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal([lamp, lamp], restored.OwnedUnplaced);
    }

    [Fact]
    public void SchemaSevenMigratesToDefaultEmptyEnvironment()
    {
        string current = ProgressSavePolicy.Serialize(ProgressSave.FromSnapshot(FundedProgress(0).Snapshot()));
        JsonObject root = JsonNode.Parse(current)!.AsObject();
        root["schemaVersion"] = 7;
        root.Remove("environment");

        SaveDecodeResult decoded = ProgressSavePolicy.Decode(root.ToJsonString());

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal(ProgressSave.CurrentSchemaVersion, decoded.Save!.SchemaVersion);
        Assert.Empty(decoded.Save.Environment.PlacedDecorations);
    }

    [Fact]
    public async Task ResetClearsEnvironmentAndRollsBackOnFailure()
    {
        var progress = FundedProgress(250_000);
        var environment = new EnvironmentProgressState(new EnvironmentLayout([Placed(Id(30), .3f)]), 2);
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store, environment: environment);

        Assert.True(await ProgressReset.ResetAsync(progress, saves));
        Assert.Empty(environment.Layout.Decorations);

        environment.Commit(new EnvironmentLayout([Placed(Id(31), .4f)]));
        await saves.FlushProgressAsync();
        EnvironmentProgressSnapshot before = environment.Snapshot();
        store.NextProgressFailure = new IOException("injected");
        Assert.False(await ProgressReset.ResetAsync(progress, saves));
        Assert.Equal(before.Revision, environment.Revision);
        Assert.Equal(before.Layout.Decorations, environment.Layout.Decorations);
    }

    [Fact]
    public void LayoutSchemasOneAndTwoMigrateByDroppingTheirFlatBackground()
    {
        var environment = new EnvironmentProgressState(new EnvironmentLayout([Placed(Id(41), .4f)]));
        string current = ProgressSavePolicy.Serialize(ProgressSave.FromSnapshot(
            FundedProgress(0).Snapshot(), environment: environment.Snapshot()));

        foreach (int legacySchema in new[] { 1, 2 })
        {
            JsonObject root = JsonNode.Parse(current)!.AsObject();
            JsonObject savedEnvironment = root["environment"]!.AsObject();
            savedEnvironment["layoutSchemaVersion"] = legacySchema;
            savedEnvironment["background"] = new JsonObject { ["wallRed"] = 12 };

            SaveDecodeResult decoded = ProgressSavePolicy.Decode(root.ToJsonString());
            EnvironmentProgressState restored = decoded.Save!.Environment.CreateState();

            Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
            Assert.Equal(EnvironmentLayout.CurrentSchemaVersion, restored.Layout.SchemaVersion);
            Assert.Single(restored.Layout.Decorations);
        }
    }

    private static BuddyProgressState FundedProgress(long balance)
    {
        var progress = new BuddyProgressState(0.018);
        progress.Deposit(balance);
        return progress;
    }

    private static PlacedDecoration Placed(PlacedDecorationId id, float x) =>
        new(id, Lamp.Id, new CanonicalRoomPosition(x, .8f), 0, Lamp.RenderBand, Lamp.PriceMilliCredits);
    private static PlacedDecorationId Id(int value) => new(new Guid(value, 0, 0, new byte[8]));
}
