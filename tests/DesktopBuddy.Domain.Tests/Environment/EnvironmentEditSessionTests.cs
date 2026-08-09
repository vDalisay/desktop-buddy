using System;
using DesktopBuddy.Domain.Environment;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Environment;

public sealed class EnvironmentEditSessionTests
{
    private static readonly DecorationDefinition Lamp = new(new("decoration.lamp.classic"), "environment.lamp.classic",
        DecorationCategory.Lamp, 75_000, DecorationAnchorKind.Floor, new(true, 90), DecorationRenderBand.BehindBuddyFloor);
    private static readonly DecorationDefinition Wallpaper = new(new("decoration.wallpaper.diagonal"), "environment.wallpaper.diagonal",
        DecorationCategory.Wallpaper, 45_000, DecorationAnchorKind.RoomSurface, DecorationRotationPolicy.Fixed,
        DecorationRenderBand.Wallpaper);
    private static readonly DecorationDefinition Plant = new(new("decoration.plant.potted"), "environment.plant.potted",
        DecorationCategory.Plant, 40_000, DecorationAnchorKind.Floor, new(true, 90), DecorationRenderBand.BehindBuddyFloor);

    [Fact]
    public void RepeatedPlacementsCostPerInstanceAndMovementIsFree()
    {
        var ids = new[] { Id(1), Id(2), Id(3) };
        int nextId = 0;
        var session = new EnvironmentEditSession(new EnvironmentLayout(), 250_000, Catalogue(), () => ids[nextId++]);

        EnvironmentEditResult first = session.Place(Lamp.Id, Position(.2f, .8f));
        EnvironmentEditResult second = session.Place(Lamp.Id, Position(.5f, .8f));
        Assert.True(session.Move(first.InstanceId, Position(.3f, .75f)).Succeeded);
        Assert.True(session.Rotate(second.InstanceId).Succeeded);
        Assert.True(session.Place(Plant.Id, Position(.7f, .8f)).Succeeded);

        EnvironmentCommit commit = session.PrepareCommit();
        Assert.Equal(3, commit.Layout.Decorations.Count);
        Assert.Equal(60_000, commit.BalanceMilliCredits);
        Assert.Equal(-190_000, session.PendingBalanceDeltaMilliCredits);
    }

    [Fact]
    public void CancelRestoresBaselineAndProjectedWallet()
    {
        var baselineItem = new PlacedDecoration(Id(10), Lamp.Id, Position(.1f, .8f), 0, Lamp.RenderBand, Lamp.PriceMilliCredits);
        var session = new EnvironmentEditSession(new EnvironmentLayout([baselineItem]), 250_000, Catalogue(), () => Id(11));
        Assert.True(session.Sell(baselineItem.InstanceId).Succeeded);
        Assert.True(session.Place(Plant.Id, Position(.4f, .8f)).Succeeded);

        session.Cancel();

        Assert.False(session.IsDirty);
        Assert.Equal(250_000, session.ProjectedBalanceMilliCredits);
        Assert.Equal([baselineItem], session.WorkingLayout.Decorations);
    }

    [Fact]
    public void SellingStagedItemReversesItsCostWithoutCreatingMoney()
    {
        var session = new EnvironmentEditSession(new EnvironmentLayout(), 75_000, Catalogue(), () => Id(20));
        EnvironmentEditResult placed = session.Place(Lamp.Id, Position(.2f, .8f));
        Assert.Equal(0, session.ProjectedBalanceMilliCredits);
        Assert.True(session.Sell(placed.InstanceId).Succeeded);
        Assert.Equal(75_000, session.ProjectedBalanceMilliCredits);
        Assert.Equal(0, session.PendingBalanceDeltaMilliCredits);
        Assert.Empty(session.WorkingLayout.Decorations);
    }

    [Fact]
    public void ReservationChargesOneCopyAndCancelRestoresItsCost()
    {
        var session = new EnvironmentEditSession(new EnvironmentLayout(), 75_000, Catalogue(), () => Id(21));

        EnvironmentEditResult reserved = session.Reserve(Lamp.Id, 75_000);
        Assert.True(reserved.Succeeded);
        Assert.True(session.HasReservation);
        Assert.Empty(session.WorkingLayout.Decorations);
        Assert.Equal(0, session.ProjectedBalanceMilliCredits);
        Assert.Equal(EnvironmentEditStatus.AlreadyReserved, session.Reserve(Lamp.Id, 75_000).Status);

        Assert.True(session.CancelReservation().Succeeded);
        Assert.False(session.HasReservation);
        Assert.False(session.IsDirty);
        Assert.Equal(75_000, session.ProjectedBalanceMilliCredits);
    }

    [Fact]
    public void ReservedCopyPlacesWithoutDoubleCharge()
    {
        var session = new EnvironmentEditSession(new EnvironmentLayout(), 150_000, Catalogue(), () => Id(22));
        Assert.True(session.Reserve(Lamp.Id, 150_000).Succeeded);

        EnvironmentEditResult placed = session.PlaceReserved(Position(.3f, .8f));

        Assert.True(placed.Succeeded);
        Assert.False(session.HasReservation);
        Assert.Single(session.WorkingLayout.Decorations);
        Assert.Equal(75_000, session.ProjectedBalanceMilliCredits);
    }

    [Fact]
    public void FocusedMoveCanRotateEitherWayAndRestoreItsBaseline()
    {
        var original = new PlacedDecoration(Id(24), Lamp.Id, Position(.2f, .8f), 0, Lamp.RenderBand, Lamp.PriceMilliCredits);
        var session = new EnvironmentEditSession(new EnvironmentLayout([original]), 150_000, Catalogue());
        EnvironmentLayout moveBaseline = session.WorkingLayout;

        Assert.True(session.Move(original.InstanceId, Position(.7f, .6f)).Succeeded);
        Assert.True(session.Rotate(original.InstanceId, -1).Succeeded);
        Assert.Equal(270, session.WorkingLayout.Decorations[0].RotationDegrees);
        Assert.True(session.Rotate(original.InstanceId, 1).Succeeded);
        Assert.Equal(0, session.WorkingLayout.Decorations[0].RotationDegrees);
        Assert.True(session.RestoreTransforms(moveBaseline));
        Assert.Equal(original, session.WorkingLayout.Decorations[0]);
        Assert.Equal(0, session.PendingBalanceDeltaMilliCredits);
    }

    [Fact]
    public void ReservationUsesCurrentWalletAffordability()
    {
        var session = new EnvironmentEditSession(new EnvironmentLayout(), 75_000, Catalogue(), () => Id(23));

        Assert.Equal(EnvironmentEditStatus.InsufficientFunds, session.Reserve(Lamp.Id, 74_000).Status);
        Assert.False(session.HasReservation);
        Assert.False(session.IsDirty);

        Assert.True(session.Reserve(Lamp.Id, 80_000).Succeeded);
        Assert.True(session.TryProjectBalance(80_000, out long projected));
        Assert.Equal(5_000, projected);
    }

    [Fact]
    public void UnaffordablePlacementLeavesSessionUntouched()
    {
        var session = new EnvironmentEditSession(new EnvironmentLayout(), 74_000, Catalogue());
        Assert.Equal(EnvironmentEditStatus.InsufficientFunds, session.Place(Lamp.Id, Position(.2f, .8f)).Status);
        Assert.False(session.IsDirty);
        Assert.Equal(74_000, session.ProjectedBalanceMilliCredits);
    }

    [Fact]
    public void UnknownSavedDefinitionIsPreservedAndCanBeRefunded()
    {
        var missingId = new DecorationDefinitionId("decoration.retired.mystery");
        var unresolved = new PlacedDecoration(Id(40), missingId, Position(.4f, .4f), 0, DecorationRenderBand.WallDecoration, 25_000);
        var session = new EnvironmentEditSession(new EnvironmentLayout([unresolved]), 100_000, Catalogue());

        Assert.Equal(EnvironmentEditStatus.UnknownDefinition, session.Rotate(unresolved.InstanceId).Status);
        Assert.Single(session.WorkingLayout.Decorations);
        Assert.True(session.Sell(unresolved.InstanceId).Succeeded);
        Assert.Equal(125_000, session.ProjectedBalanceMilliCredits);
    }

    [Fact]
    public void DefinitionAndLayoutValidationRejectUnsafeRecords()
    {
        Assert.False(DecorationDefinitionId.TryCreate("../lamp.tscn", out _));
        Assert.Throws<ArgumentException>(() => new DecorationCatalogue([Lamp, Lamp]));
        Assert.Throws<ArgumentException>(() => new EnvironmentLayout([
            new PlacedDecoration(Id(50), Lamp.Id, Position(.2f, .8f), 0, Lamp.RenderBand, 75_000),
            new PlacedDecoration(Id(50), Plant.Id, Position(.4f, .8f), 0, Plant.RenderBand, 40_000),
        ]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Position(float.NaN, .5f));
    }

    [Fact]
    public void EnvironmentProgressAdoptsAndCommitsValidatedLayouts()
    {
        var state = new EnvironmentProgressState();
        var layout = new EnvironmentLayout([new PlacedDecoration(Id(60), Lamp.Id, Position(.2f, .8f), 0, Lamp.RenderBand, 75_000)]);
        state.Commit(layout);
        Assert.Equal(1, state.Revision);
        Assert.Same(layout, state.Layout);

        state.Adopt(new EnvironmentProgressSnapshot(7, new EnvironmentLayout()));
        Assert.Equal(7, state.Revision);
        Assert.Empty(state.Layout.Decorations);
    }

    private static DecorationCatalogue Catalogue() => new([Lamp, Plant, Wallpaper]);
    private static CanonicalRoomPosition Position(float x, float y) => new(x, y);
    private static PlacedDecorationId Id(int value) => new(new Guid(value, 0, 0, new byte[8]));
}
