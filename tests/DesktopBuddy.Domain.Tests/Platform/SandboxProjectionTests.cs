using System;
using DesktopBuddy.Domain.Platform;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Platform;

public sealed class SandboxProjectionTests
{
    [Fact]
    public void IdentityAtUnitZoom()
    {
        PixelRect client = SandboxProjection.SandboxRectToClient(16, 16, 448, 328, 1.0);
        Assert.Equal(new PixelRect(16, 16, 448, 328), client);
    }

    [Theory]
    [InlineData(2.0, 32, 32, 896, 656)]
    [InlineData(0.75, 12, 12, 336, 246)]
    [InlineData(1.25, 20, 20, 560, 410)]
    public void ScalesByZoom(double zoom, int x, int y, int w, int h)
    {
        PixelRect client = SandboxProjection.SandboxRectToClient(16, 16, 448, 328, zoom);
        Assert.Equal(new PixelRect(x, y, w, h), client);
    }

    [Fact]
    public void RoundsToNearestPixel()
    {
        // 16 * 1.1 = 17.6 -> 18; 100 * 1.1 = 110.
        PixelRect client = SandboxProjection.SandboxRectToClient(16, 16, 100, 100, 1.1);
        Assert.Equal(new PixelRect(18, 18, 110, 110), client);
    }

    [Fact]
    public void ClampsNegativeSizeToZero()
    {
        PixelRect client = SandboxProjection.SandboxRectToClient(0, 0, -5, -5, 1.0);
        Assert.Equal(0, client.Width);
        Assert.Equal(0, client.Height);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsNonPositiveOrNonFiniteZoom(double zoom)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SandboxProjection.SandboxRectToClient(0, 0, 10, 10, zoom));
    }
}
