using System;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class RoomLayoutPolicyTests
{
    [Theory]
    [InlineData(360, 270, 2.0, 1.0, 360.0, 270.0)]
    [InlineData(480, 360, 2.0, 1.25, 384.0, 288.0)]
    [InlineData(640, 360, 2.0, 1.25, 512.0, 288.0)]
    [InlineData(960, 720, 2.0, 2.0, 480.0, 360.0)]
    [InlineData(480, 360, 0.75, 0.75, 640.0, 480.0)]
    public void Resolve_ClampsEffectiveZoomWithoutDiscardingStoredPreference(
        int width,
        int height,
        double stored,
        double effective,
        double roomWidth,
        double roomHeight)
    {
        RoomLayout layout = RoomLayoutPolicy.Resolve(width, height, stored);

        Assert.Equal(stored, layout.StoredZoom);
        Assert.Equal(effective, layout.EffectiveZoom);
        Assert.Equal(roomWidth, layout.RoomWidth, 6);
        Assert.Equal(roomHeight, layout.RoomHeight, 6);
        Assert.True(layout.RoomWidth >= RoomLayoutPolicy.MinimumRoomWidth);
        Assert.True(layout.RoomHeight >= RoomLayoutPolicy.MinimumRoomHeight);
    }

    [Fact]
    public void IsZoomAvailable_UsesBothRoomDimensions()
    {
        Assert.True(RoomLayoutPolicy.IsZoomAvailable(640, 360, 1.25));
        Assert.False(RoomLayoutPolicy.IsZoomAvailable(640, 360, 1.5));
        Assert.True(RoomLayoutPolicy.IsZoomAvailable(720, 540, 2.0));
    }

    [Theory]
    [InlineData(359, 270)]
    [InlineData(360, 269)]
    public void Resolve_RejectsClientBelowConfirmedMinimum(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayoutPolicy.Resolve(width, height, 1.0));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.1)]
    [InlineData(2.25)]
    public void Resolve_RejectsUnsupportedZoom(double zoom)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomLayoutPolicy.Resolve(480, 360, zoom));
    }
}
