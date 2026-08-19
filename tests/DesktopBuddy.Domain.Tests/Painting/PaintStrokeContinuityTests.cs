using System;
using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

/// <summary>
/// <see cref="PaintSurface.StampSpacingFactor"/> is the one constant deciding how much a big
/// brush costs, so it is tuned for speed. These lock the visual floor it must not cross: a
/// dragged stroke has to read as one solid band, never a row of scalloped dots.
/// </summary>
public sealed class PaintStrokeContinuityTests
{
    private const int Size = PaintPolicy.SurfaceSize;

    [Theory]
    [InlineData(PaintPolicy.MinBrushDiameter)]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(PaintPolicy.MaxBrushDiameter)]
    public void DraggedStroke_IsSolidAcrossItsWholeSpan(int diameter)
    {
        int[] coverage = PaintHorizontalStroke(diameter);

        // Sample well inside the stroke so the round caps at either end are not measured.
        int from = Size / 4;
        int to = (Size * 3) / 4;
        int peak = 0;
        for (int x = from; x <= to; x++)
            peak = Math.Max(peak, coverage[x]);

        Assert.True(peak > 0, "the stroke painted nothing at all");
        for (int x = from; x <= to; x++)
        {
            Assert.True(
                coverage[x] > 0,
                $"brush {diameter} left a gap at column {x}: the stamp spacing is wider than the brush");
        }

        // The evenness floor only applies once the stroke is tall enough for a missing row to
        // read as a scallop. A 4px brush is 2 rows tall after the 0.5 vertical scale, where a
        // single row of ellipse rasterization is a third of the band; that chunkiness is the
        // rasterizer's and is identical at every spacing.
        if (peak < 16)
            return;
        for (int x = from; x <= to; x++)
        {
            Assert.True(
                coverage[x] >= peak * 0.85,
                $"brush {diameter} scalloped at column {x}: {coverage[x]} of {peak} rows covered");
        }
    }

    /// <summary>Paints left-to-right at mid-height and returns painted rows per column.</summary>
    private static int[] PaintHorizontalStroke(int diameter)
    {
        var workspace = new PaintWorkspace();
        workspace.SetBrushDiameter(diameter);

        // Step the way PaintCanvasControl does: spacing derived from the same shared factor.
        double spacing = Math.Max(0.5, diameter * PaintSurface.StampSpacingFactor);
        int steps = (int)Math.Ceiling(Size / spacing);

        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.0, 0.5), 0));
        for (int step = 1; step <= steps; step++)
        {
            workspace.ContinueGesture(new PaintHit(
                PaintPart.Torso,
                new PaintPoint(step / (double)steps, 0.5),
                0));
        }
        workspace.EndGesture();

        byte[] pixels = workspace.Surfaces[PaintPart.Torso]
            .Capture(new PaintRect(0, 0, Size, Size));
        var coverage = new int[Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (pixels[(((y * Size) + x) * PaintPolicy.BytesPerPixel) + 3] > 0)
                    coverage[x]++;
            }
        }
        return coverage;
    }
}
