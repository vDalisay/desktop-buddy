using System;
using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

/// <summary>
/// The eraser lays down a square block rather than the brush's round footprint (owner
/// instruction 2026-08-19). Corner samples are what tell the two apart — the axes alone read
/// the same for a square and an inscribed ellipse.
/// </summary>
public sealed class PaintEraserFootprintTests
{
    private const int Size = PaintPolicy.SurfaceSize;

    [Fact]
    public void Eraser_ClearsASquareBlockIncludingItsCorners()
    {
        // Small enough to sit well inside the covered patch in both axes, so an "outside"
        // sample is unpainted because the eraser stopped, not because paint never reached.
        const int diameter = 32;
        var workspace = new PaintWorkspace();

        // Cover the middle of the surface first, so the erased hole is what is being measured.
        workspace.SetBrushDiameter(PaintPolicy.MaxBrushDiameter);
        workspace.SelectedColor = new PaintColor(200, 40, 90);
        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();

        workspace.SelectedTool = PaintTool.Eraser;
        workspace.SetBrushDiameter(diameter);
        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();

        byte[] pixels = workspace.Surfaces[PaintPart.Torso].Capture(new PaintRect(0, 0, Size, Size));
        int centre = (Size - 1) / 2;
        const int inside = (diameter / 2) - 3;

        Assert.False(IsPainted(pixels, centre, centre), "the eraser cleared nothing at all");
        Assert.False(IsPainted(pixels, centre + inside, centre), "not cleared across");
        // The square's own height: an ellipse footprint would be half as tall.
        Assert.False(IsPainted(pixels, centre, centre + inside), "not cleared down");
        // The corner, which only a square reaches.
        Assert.False(IsPainted(pixels, centre + inside, centre + inside), "the corner survived, so the eraser is still round");

        // And it stops where the square stops rather than running on.
        int outside = (diameter / 2) + 3;
        Assert.True(IsPainted(pixels, centre + outside, centre), "the eraser reached past its width");
        Assert.True(IsPainted(pixels, centre, centre + outside), "the eraser reached past its height");
    }

    [Fact]
    public void Brush_KeepsItsRoundFootprintWithNoCorners()
    {
        const int diameter = 64;
        var workspace = new PaintWorkspace();
        workspace.SetBrushDiameter(diameter);
        workspace.SelectedColor = new PaintColor(30, 160, 220);
        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();

        byte[] pixels = workspace.Surfaces[PaintPart.Torso].Capture(new PaintRect(0, 0, Size, Size));
        int centre = (Size - 1) / 2;
        const int inside = (diameter / 2) - 3;

        Assert.True(IsPainted(pixels, centre, centre));
        Assert.True(IsPainted(pixels, centre + inside, centre));
        Assert.False(
            IsPainted(pixels, centre + inside, centre + inside),
            "the brush painted its bounding-box corner, so it is no longer round");
    }

    /// <summary>
    /// The screen-dab path stamps one small mark per supplied hit and nothing in between. The
    /// caller picks those hits in screen space, which is what lets the eraser and the spray land
    /// as the shape their outline draws however the surface is stretched or rotated underneath
    /// (owner report 2026-08-19).
    /// </summary>
    [Fact]
    public void ScreenDab_MarksEverySuppliedHitAndNothingBetweenThem()
    {
        var workspace = new PaintWorkspace { SelectedTool = PaintTool.Eraser };
        var covered = new byte[PaintPolicy.SurfaceBytes];
        Array.Fill(covered, byte.MaxValue);
        workspace.Load(PaintPart.Torso, covered);

        // Two dabs far apart: an old-style single footprint would have joined them.
        var hits = new[]
        {
            new PaintHit(PaintPart.Torso, new PaintPoint(0.25, 0.5), 0),
            new PaintHit(PaintPart.Torso, new PaintPoint(0.75, 0.5), 0),
        };
        workspace.BeginGesture(null);
        workspace.StampScreenDab(hits, PaintPolicy.MinBrushDiameter, PaintTool.Eraser);
        workspace.EndGesture();

        byte[] pixels = workspace.Surfaces[PaintPart.Torso].Capture(new PaintRect(0, 0, Size, Size));
        int row = (int)(0.5 * (Size - 1));
        Assert.False(IsPainted(pixels, (int)(0.25 * (Size - 1)), row), "the first dab did not land");
        Assert.False(IsPainted(pixels, (int)(0.75 * (Size - 1)), row), "the second dab did not land");
        Assert.True(IsPainted(pixels, (int)(0.5 * (Size - 1)), row), "the gap between the dabs was erased");
    }

    /// <summary>Bounding box of the hole an eraser left in a fully covered surface.</summary>
    private static (int Width, int Height) ClearedExtent(PaintWorkspace workspace, PaintPart part)
    {
        byte[] pixels = workspace.Surfaces[part].Capture(new PaintRect(0, 0, Size, Size));
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            if (IsPainted(pixels, x, y))
                continue;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        return minX > maxX ? (0, 0) : (maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Bounding box of everything the gesture painted.</summary>
    private static (int Width, int Height) TouchedExtent(PaintWorkspace workspace, PaintPart part)
    {
        byte[] pixels = workspace.Surfaces[part].Capture(new PaintRect(0, 0, Size, Size));
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            if (!IsPainted(pixels, x, y))
                continue;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        return minX > maxX ? (0, 0) : (maxX - minX + 1, maxY - minY + 1);
    }

    private static bool IsPainted(byte[] pixels, int x, int y) =>
        pixels[(((y * Size) + x) * PaintPolicy.BytesPerPixel) + 3] > 0;
}
