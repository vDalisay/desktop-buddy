using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

/// <summary>Regression coverage for the cross-editor paint review pass.</summary>
public sealed class PaintReviewRegressionTests
{
    private static readonly EnvironmentColor RoomInk = new(12, 34, 56);

    [Fact]
    public void RowWriterMatchesScalarRasterAtSeamsPolesAndAcrossRepeatedErasing()
    {
        var surface = new PaintSurface();
        byte[] expected = new byte[PaintPolicy.SurfaceBytes];
        var random = new System.Random(734);
        for (int stamp = 0; stamp < 90; stamp++)
        {
            PaintUvRegion region = stamp % 3 == 0 ? PaintUvRegion.Full :
                stamp % 3 == 1 ? PaintUvRegion.LimbEnd : new PaintUvRegion(.5, .125);
            var uv = new PaintPoint(region.AtlasU(stamp % 2 == 0 ? .001 : .999),
                stamp % 3 == 0 ? 0 : stamp % 3 == 1 ? 1 : random.NextDouble());
            int diameter = stamp % 2 == 0 ? 128 : 19;
            double scale = stamp % 2 == 0 ? .5 : 1;
            bool erase = stamp % 4 == 0;
            double cx = region.PixelX(uv.X), cy = uv.Y * 511;
            double rx = diameter / 2.0, ry = rx * scale;
            for (int y = System.Math.Max(0, (int)System.Math.Floor(cy - ry));
                y <= System.Math.Min(511, (int)System.Math.Ceiling(cy + ry)); y++)
            for (int x = (int)System.Math.Floor(cx - rx); x <= System.Math.Ceiling(cx + rx); x++)
            {
                double dx = (x + .5 - cx) * (1.0 / rx), dy = (y + .5 - cy) * (1.0 / ry);
                if (erase ? System.Math.Abs(dx) > 1 || System.Math.Abs(dy) > 1 : dx * dx + dy * dy > 1) continue;
                int pixel = (y * 512 + region.WrapPixelX(x)) * 4;
                expected[pixel] = erase ? (byte)0 : (byte)stamp;
                expected[pixel + 1] = erase ? (byte)0 : (byte)34;
                expected[pixel + 2] = erase ? (byte)0 : (byte)56;
                expected[pixel + 3] = erase ? (byte)0 : (byte)255;
            }
            surface.Stamp(uv, diameter, erase ? PaintTool.Eraser : PaintTool.Brush,
                new PaintColor((byte)stamp, 34, 56), scale, region);
            Assert.True(System.MemoryExtensions.SequenceEqual<byte>(expected, surface.Pixels.Span), $"stamp={stamp}");
        }
    }

    [Fact]
    public void PackedRgbaLargeBrushMatchesPreOptimizationPixelsAndSkipsEqualStamps()
    {
        var surface = new PaintSurface();
        for (int i = 0; i < 2000; i++)
            surface.Stamp(new PaintPoint((i % 100) / 100.0, .5), 128,
                PaintTool.Brush, new PaintColor((byte)i, 34, 56), .5);

        // Captured from the original four-byte writer, including wrapped seam strokes.
        Assert.Equal("B8CD92A76A3D66CEAF8F0509CB38206C330D01A16CF5381399053418135D2BF6", surface.ComputeHash());
        long revision = surface.Revision;
        Assert.True(surface.Stamp(new PaintPoint(.99, .5), 128,
            PaintTool.Brush, new PaintColor(unchecked((byte)1999), 34, 56), .5).IsEmpty);
        Assert.Equal(revision, surface.Revision);
    }

    [Fact]
    public void EnvironmentPickColorIsReadOnlyAndDoesNotCreateUndoHistory()
    {
        var canvas = new EnvironmentCanvas { Tool = EnvironmentPaintTool.Fill, Color = RoomInk };
        canvas.Begin(.5, .5);
        canvas.End(.5, .5);
        canvas.MarkSaved();

        canvas.Color = new EnvironmentColor(200, 100, 50);
        canvas.Tool = EnvironmentPaintTool.PickColor;
        canvas.Begin(.5, .5);
        canvas.End(.5, .5);

        Assert.Equal(RoomInk, canvas.Color);
        Assert.False(canvas.CanUndo);
        Assert.False(canvas.IsDirty);
    }

    [Fact]
    public void EnvironmentLargePenStrokeRemainsContinuousAfterHotPathChanges()
    {
        var canvas = new EnvironmentCanvas
        {
            Tool = EnvironmentPaintTool.Pen,
            Color = RoomInk,
            BrushDiameter = EnvironmentCanvasPolicy.MaxBrushDiameter,
        };

        canvas.Begin(.05, .5);
        canvas.Continue(.95, .5);
        canvas.End(.95, .5);

        // Inner-loop optimizations must not turn a Win98-style drag into a dotted line.
        for (int x = 32; x < EnvironmentCanvasPolicy.Size - 32; x++)
        {
            Assert.True(canvas.TryPick(x / (double)(EnvironmentCanvasPolicy.Size - 1), .5, out EnvironmentColor color));
            Assert.Equal(RoomInk, color);
        }
    }

    [Fact]
    public void EnvironmentScanlineFillRespectsBarriersAndStillUndoesAsOneGesture()
    {
        var barrier = new EnvironmentColor(220, 10, 10);
        var canvas = new EnvironmentCanvas
        {
            Tool = EnvironmentPaintTool.Line,
            Color = barrier,
            BrushDiameter = EnvironmentCanvasPolicy.MinBrushDiameter,
        };
        canvas.Begin(.5, 0);
        canvas.End(.5, 1);
        canvas.MarkSaved();
        byte[] baseline = canvas.ClonePixels();

        canvas.Tool = EnvironmentPaintTool.Fill;
        canvas.Color = RoomInk;
        canvas.Begin(.25, .5);
        canvas.End(.25, .5);

        Assert.True(canvas.TryPick(.25, .5, out EnvironmentColor left));
        Assert.Equal(RoomInk, left);
        Assert.True(canvas.TryPick(.75, .5, out EnvironmentColor right));
        Assert.Equal(EnvironmentCanvasPolicy.Blank, right);
        Assert.True(canvas.TryPick(.5, .5, out EnvironmentColor divider));
        Assert.Equal(barrier, divider);
        Assert.True(canvas.Undo());
        Assert.Equal(baseline, canvas.ClonePixels());
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(0.002, 0.5)]
    [InlineData(0.498, 0.5)]
    public void PaintSurfaceStampReturnsThePrecomputedDirtyBounds(double u, double v)
    {
        var surface = new PaintSurface();
        var uv = new PaintPoint(u, v);
        PaintUvRegion region = PaintUvRegion.LimbEnd;
        PaintRect expected = PaintSurface.StampBounds(uv, 64, region: region);

        PaintRect actual = surface.Stamp(
            uv,
            64,
            PaintTool.Pen,
            new PaintColor(1, 2, 3),
            region: region);

        Assert.Equal(expected, actual);
    }
}
