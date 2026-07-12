using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Platform;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Platform;

public sealed class WindowPlacementPolicyTests
{
    // A typical 1920x1080 monitor whose usable area excludes a 40 px bottom taskbar.
    private static readonly PixelRect Usable = new(0, 0, 1920, 1040);

    [Fact]
    public void FirstLaunch_AnchorsSixteenPixelsInsideLowerRight()
    {
        PixelRect rect = WindowPlacementPolicy.FirstLaunch(Usable);

        Assert.Equal(WindowPlacementPolicy.DefaultWidth, rect.Width);
        Assert.Equal(WindowPlacementPolicy.DefaultHeight, rect.Height);
        Assert.Equal(Usable.Right - rect.Width - WindowPlacementPolicy.FirstLaunchMargin, rect.X);
        Assert.Equal(Usable.Bottom - rect.Height - WindowPlacementPolicy.FirstLaunchMargin, rect.Y);
        Assert.True(Usable.Contains(rect));
    }

    [Fact]
    public void FirstLaunch_RespectsUsableOriginOffset()
    {
        // Taskbar on the left edge: usable area starts at x=60.
        var offset = new PixelRect(60, 0, 1860, 1080);
        PixelRect rect = WindowPlacementPolicy.FirstLaunch(offset);

        Assert.Equal(offset.Right - rect.Width - WindowPlacementPolicy.FirstLaunchMargin, rect.X);
        Assert.True(offset.Contains(rect));
    }

    [Fact]
    public void FirstLaunch_ClampsSizeToTinyMonitorAndKeepsOnScreen()
    {
        // Monitor smaller than the default window: size clamps to the minimum,
        // origin never leaves the usable area even though the margin cannot fit.
        var tiny = new PixelRect(0, 0, WindowPlacementPolicy.MinimumWidth, WindowPlacementPolicy.MinimumHeight);
        PixelRect rect = WindowPlacementPolicy.FirstLaunch(tiny);

        Assert.Equal(WindowPlacementPolicy.MinimumWidth, rect.Width);
        Assert.Equal(WindowPlacementPolicy.MinimumHeight, rect.Height);
        Assert.Equal(tiny.X, rect.X);
        Assert.Equal(tiny.Y, rect.Y);
    }

    [Fact]
    public void Recover_KeepsValidStoredRectOnItsMonitor()
    {
        var stored = new PixelRect(200, 150, 640, 480);
        WindowPlacement placement = WindowPlacementPolicy.Recover(stored, new[] { Usable });

        Assert.Equal(0, placement.MonitorIndex);
        Assert.Equal(stored, placement.Rect);
    }

    [Fact]
    public void Recover_ClampsPartlyOffScreenBackIntoMonitor()
    {
        // Right/bottom edge hangs off the usable area; overlap is still greatest here.
        var stored = new PixelRect(1700, 900, 480, 360);
        WindowPlacement placement = WindowPlacementPolicy.Recover(stored, new[] { Usable });

        Assert.True(Usable.Contains(placement.Rect));
        Assert.Equal(Usable.Right - 480, placement.Rect.X);
        Assert.Equal(Usable.Bottom - 360, placement.Rect.Y);
        Assert.Equal(480, placement.Rect.Width);
    }

    [Fact]
    public void Recover_FullyOffScreenReanchorsOnPrimary()
    {
        // Secondary monitor removed; the stored rect sat entirely on it.
        var stored = new PixelRect(-2000, 100, 480, 360);
        WindowPlacement placement = WindowPlacementPolicy.Recover(stored, new[] { Usable });

        Assert.Equal(0, placement.MonitorIndex);
        Assert.True(Usable.Contains(placement.Rect));
        // Re-anchored lower-right, preserving the stored size.
        Assert.Equal(Usable.Right - 480 - WindowPlacementPolicy.FirstLaunchMargin, placement.Rect.X);
    }

    [Fact]
    public void Recover_PicksMonitorWithGreatestOverlap()
    {
        var primary = new PixelRect(0, 0, 1920, 1040);
        var secondary = new PixelRect(1920, 0, 1920, 1080);
        // Straddles the seam but sits mostly on the secondary monitor.
        var stored = new PixelRect(1800, 200, 640, 480);
        WindowPlacement placement = WindowPlacementPolicy.Recover(stored, new[] { primary, secondary });

        Assert.Equal(1, placement.MonitorIndex);
        Assert.True(secondary.Contains(placement.Rect));
    }

    [Fact]
    public void Recover_ClampsWindowLargerThanMonitor()
    {
        var stored = new PixelRect(-50, -50, 5000, 5000);
        WindowPlacement placement = WindowPlacementPolicy.Recover(stored, new[] { Usable });

        Assert.Equal(Usable.Width, placement.Rect.Width);
        Assert.Equal(Usable.Height, placement.Rect.Height);
        Assert.True(Usable.Contains(placement.Rect));
    }

    [Fact]
    public void Recover_RejectsEmptyMonitorList()
    {
        Assert.Throws<ArgumentException>(() =>
            WindowPlacementPolicy.Recover(new PixelRect(0, 0, 480, 360), Array.Empty<PixelRect>()));
    }

    [Theory]
    [InlineData(0, 0, 100, 100, 50, 50, 100, 100, 2500)] // quarter overlap
    [InlineData(0, 0, 100, 100, 100, 0, 100, 100, 0)]     // edge-adjacent, disjoint
    [InlineData(0, 0, 100, 100, 0, 0, 100, 100, 10000)]   // identical
    public void IntersectionArea_IsCorrect(
        int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh, long expected)
    {
        var a = new PixelRect(ax, ay, aw, ah);
        var b = new PixelRect(bx, by, bw, bh);
        Assert.Equal(expected, a.IntersectionArea(b));
        Assert.Equal(expected, b.IntersectionArea(a));
    }
}
