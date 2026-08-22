using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintSessionPayoutTests
{
    [Fact]
    public void AnUntouchedSessionPaysNothing()
    {
        byte[] before = new byte[64 * 4];
        byte[] after = (byte[])before.Clone();

        Assert.Equal(0, PaintSessionPayout.ChangedPixels(before, after));
        Assert.Equal(0, PaintSessionPayout.MilliCredits(0));
        Assert.Equal(0, PaintSessionPayout.MilliCredits(-5));
    }

    [Fact]
    public void OnlyPixelsThatEndedDifferentCount()
    {
        byte[] before = new byte[4 * 4];
        byte[] after = (byte[])before.Clone();
        after[0] = 255;          // first pixel: red channel
        after[11] = 128;         // third pixel: alpha
        // Second pixel is painted and painted back to where it started, so it is not changed.

        Assert.Equal(2, PaintSessionPayout.ChangedPixels(before, after));
    }

    [Fact]
    public void PayoutScalesWithTheAreaAndStopsAtTheCeiling()
    {
        Assert.Equal(1_000, PaintSessionPayout.MilliCredits(PaintSessionPayout.PixelsPerCredit));
        Assert.Equal(10_000, PaintSessionPayout.MilliCredits(PaintSessionPayout.PixelsPerCredit * 10));
        Assert.Equal(
            PaintSessionPayout.MaximumMilliCredits,
            PaintSessionPayout.MilliCredits(PaintSessionPayout.PixelsPerCredit * 100));
        Assert.Equal(
            PaintSessionPayout.MaximumMilliCredits,
            PaintSessionPayout.MilliCredits(long.MaxValue / 2_000));
    }

    [Fact]
    public void AResizedSurfaceComparesOnlyTheOverlap()
    {
        byte[] before = new byte[8 * 4];
        byte[] after = new byte[4 * 4];
        for (int index = 0; index < after.Length; index++)
            after[index] = 255;

        Assert.Equal(4, PaintSessionPayout.ChangedPixels(before, after));
    }
}
