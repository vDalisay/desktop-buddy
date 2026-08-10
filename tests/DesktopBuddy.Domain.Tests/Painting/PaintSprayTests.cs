using System;
using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintSprayTests
{
    [Fact]
    public void LargerBrushSizeProducesLargerDenserSprayPulse()
    {
        PaintSurface small = new();
        PaintSurface large = new();
        PaintPoint center = new(0.5, 0.5);
        PaintColor color = new(20, 40, 60);

        small.Spray(center, PaintPolicy.MinBrushDiameter, color, 42);
        large.Spray(center, PaintPolicy.MaxBrushDiameter, color, 42);

        Assert.True(CountOpaque(large) > CountOpaque(small));
    }

    [Fact]
    public void SprayWrapsAcrossUSeamButNeverAcrossVerticalEdge()
    {
        PaintSurface seam = new();
        PaintSurface top = new();
        PaintColor color = new(1, 2, 3);

        seam.Spray(new PaintPoint(0.001, 0.5), PaintPolicy.MaxBrushDiameter, color, 7);
        top.Spray(new PaintPoint(0.5, 0.001), PaintPolicy.MaxBrushDiameter, color, 7);

        Assert.True(HasOpaqueInRegion(seam, PaintPolicy.SurfaceSize - 64, 0, 64, PaintPolicy.SurfaceSize));
        Assert.False(HasOpaqueInRegion(top, 0, PaintPolicy.SurfaceSize - 64, PaintPolicy.SurfaceSize, 64));
    }

    [Fact]
    public void RepeatedStationaryPulsesRemainOneExactUndoCommand()
    {
        PaintWorkspace workspace = new() { SelectedTool = PaintTool.Spray };
        PaintHit hit = new(PaintPart.Head, new PaintPoint(0.5, 0.5), 0);
        string before = workspace.Surfaces[PaintPart.Head].ComputeHash();

        workspace.BeginGesture(hit);
        for (int pulse = 0; pulse < 8; pulse++)
            workspace.ContinueGesture(hit);
        workspace.EndGesture();

        Assert.NotEqual(before, workspace.Surfaces[PaintPart.Head].ComputeHash());
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(before, workspace.Surfaces[PaintPart.Head].ComputeHash());
    }

    [Fact]
    public void SprayPartTransitionDoesNotBridgeThroughUnpaintedSpace()
    {
        PaintWorkspace workspace = new() { SelectedTool = PaintTool.Spray };
        PaintHit left = new(PaintPart.Head, new PaintPoint(0.1, 0.1), 0);
        PaintHit right = new(PaintPart.Head, new PaintPoint(0.9, 0.9), 0);

        workspace.BeginGesture(left);
        workspace.ContinueGesture(null);
        workspace.ContinueGesture(right);
        workspace.EndGesture();

        Assert.False(workspace.Surfaces[PaintPart.Head].TrySample(new PaintPoint(0.5, 0.5), out _));
    }

    private static int CountOpaque(PaintSurface surface)
    {
        int count = 0;
        ReadOnlySpan<byte> pixels = surface.Pixels.Span;
        for (int index = 3; index < pixels.Length; index += PaintPolicy.BytesPerPixel)
            if (pixels[index] != 0) count++;
        return count;
    }

    private static bool HasOpaqueInRegion(PaintSurface surface, int x, int y, int width, int height)
    {
        ReadOnlySpan<byte> pixels = surface.Pixels.Span;
        for (int row = y; row < y + height; row++)
        for (int column = x; column < x + width; column++)
        {
            int alpha = ((row * PaintPolicy.SurfaceSize) + column) * PaintPolicy.BytesPerPixel + 3;
            if (pixels[alpha] != 0) return true;
        }
        return false;
    }
}
