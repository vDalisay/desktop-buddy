using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

/// <summary>Regression coverage for the cross-editor paint review pass.</summary>
public sealed class PaintReviewRegressionTests
{
    private static readonly EnvironmentColor RoomInk = new(12, 34, 56);

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
