using System;
using DesktopBuddy.Domain.Environment;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Environment;

public sealed class EnvironmentPlacementTests
{
    private static readonly RoomScreenBounds Room = new(100, 50, 800, 600);

    [Fact]
    public void MapsPointerToResolutionIndependentCoordinatesAndBack()
    {
        Assert.True(EnvironmentPlacement.TryMap(300, 500, Room, DecorationAnchorKind.Floor, false,
            EnvironmentGridSize.Medium, out CanonicalRoomPosition position));
        Assert.Equal(.25f, position.X, 4);
        Assert.Equal(.75f, position.Y, 4);
        Assert.Equal((300f, 500f), EnvironmentPlacement.ToScreen(position, Room));
        Assert.Equal((600f, 975f), EnvironmentPlacement.ToScreen(position, new RoomScreenBounds(200, 150, 1600, 1100)));
    }

    [Theory]
    [InlineData(300, 200, DecorationAnchorKind.Wall, true)]
    [InlineData(300, 500, DecorationAnchorKind.Wall, false)]
    [InlineData(300, 500, DecorationAnchorKind.Floor, true)]
    [InlineData(300, 200, DecorationAnchorKind.Floor, false)]
    [InlineData(300, 200, DecorationAnchorKind.RoomSurface, true)]
    public void EnforcesAuthoredAnchorZone(float x, float y, DecorationAnchorKind anchor, bool expected)
    {
        Assert.Equal(expected, EnvironmentPlacement.TryMap(x, y, Room, anchor, false,
            EnvironmentGridSize.Fine, out _));
    }

    [Fact]
    public void SnapSizesQuantizeCanonicalCoordinates()
    {
        Assert.True(EnvironmentPlacement.TryMap(367, 501, Room, DecorationAnchorKind.RoomSurface, true,
            EnvironmentGridSize.Large, out CanonicalRoomPosition large));
        Assert.True(EnvironmentPlacement.TryMap(367, 501, Room, DecorationAnchorKind.RoomSurface, true,
            EnvironmentGridSize.Fine, out CanonicalRoomPosition fine));
        Assert.Equal(.375f, large.X, 4);
        Assert.Equal(.34375f, fine.X, 4);
        Assert.NotEqual(large.X, fine.X);
    }

    [Fact]
    public void RejectsChromeOutsideRoomAndInvalidBounds()
    {
        Assert.False(EnvironmentPlacement.TryMap(99, 500, Room, DecorationAnchorKind.RoomSurface, false,
            EnvironmentGridSize.Medium, out _));
        Assert.False(EnvironmentPlacement.TryMap(300, 49, Room, DecorationAnchorKind.RoomSurface, false,
            EnvironmentGridSize.Medium, out _));
        Assert.False(EnvironmentPlacement.TryMap(float.NaN, 500, Room, DecorationAnchorKind.RoomSurface, false,
            EnvironmentGridSize.Medium, out _));
        Assert.Throws<ArgumentException>(() => EnvironmentPlacement.ToScreen(default, new RoomScreenBounds(0, 0, 0, 10)));
    }
}
