using System.Linq;
using DesktopBuddy.Domain.Environment;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Environment;

public sealed class EnvironmentPaintRefinementTests
{
    private static readonly EnvironmentColor Ink = new(12, 34, 56);

    [Fact]
    public void SprayUsesBrushDiameterAndClampsAtRoomEdges()
    {
        var small = new EnvironmentCanvas { Tool = EnvironmentPaintTool.Spray, Color = Ink, BrushDiameter = 4 };
        var large = new EnvironmentCanvas { Tool = EnvironmentPaintTool.Spray, Color = Ink, BrushDiameter = 64 };

        small.Begin(.5, .5); small.End(.5, .5);
        large.Begin(.5, .5); large.End(.5, .5);

        Assert.True(ChangedPixels(large) > ChangedPixels(small));

        var edge = new EnvironmentCanvas { Tool = EnvironmentPaintTool.Spray, Color = Ink, BrushDiameter = 96 };
        edge.Begin(.001, .5);
        for (int pulse = 0; pulse < 8; pulse++) edge.Continue(.001, .5);
        edge.End(.001, .5);

        Assert.True(edge.TryPick(.999, .5, out EnvironmentColor opposite));
        Assert.Equal(EnvironmentCanvasPolicy.DefaultColor, opposite);
    }

    [Fact]
    public void StationarySprayPulsesRemainOneUndoStep()
    {
        var canvas = new EnvironmentCanvas { Tool = EnvironmentPaintTool.Spray, Color = Ink, BrushDiameter = 32 };
        byte[] before = canvas.ClonePixels();

        canvas.Begin(.5, .5);
        for (int pulse = 0; pulse < 10; pulse++) canvas.Continue(.5, .5);
        canvas.End(.5, .5);

        Assert.True(canvas.CanUndo);
        Assert.True(canvas.Undo());
        Assert.Equal(before, canvas.ClonePixels());
    }

    [Fact]
    public void CurveBaselineMatchesStraightLineBeforeBending()
    {
        var straight = new EnvironmentCanvas { Tool = EnvironmentPaintTool.Line, Color = Ink, BrushDiameter = 6 };
        var curved = new EnvironmentCanvas { Tool = EnvironmentPaintTool.CurvedLine, Color = Ink, BrushDiameter = 6 };

        straight.Begin(.2, .3); straight.End(.8, .3);
        curved.Begin(.2, .3); curved.End(.8, .3);

        Assert.Equal(EnvironmentCurvePhase.AwaitFirstBend, curved.CurvePhase);
        Assert.Equal(straight.ClonePixels(), curved.ClonePixels());
        Assert.False(curved.CanUndo);
    }

    [Fact]
    public void CurveTwoBendsCommitAsOneUndoAction()
    {
        var canvas = new EnvironmentCanvas { Tool = EnvironmentPaintTool.CurvedLine, Color = Ink, BrushDiameter = 6 };
        byte[] baseline = canvas.ClonePixels();

        canvas.Begin(.2, .5); canvas.End(.8, .5);
        canvas.Begin(.35, .5); canvas.End(.35, .30);
        Assert.Equal(EnvironmentCurvePhase.AwaitSecondBend, canvas.CurvePhase);
        canvas.Begin(.68, .5); canvas.End(.68, .72);

        Assert.Equal(EnvironmentCurvePhase.Idle, canvas.CurvePhase);
        Assert.True(canvas.IsDirty);
        Assert.True(canvas.CanUndo);
        Assert.NotEqual(baseline, canvas.ClonePixels());
        Assert.True(canvas.Undo());
        Assert.Equal(baseline, canvas.ClonePixels());
        Assert.False(canvas.CanUndo);
    }

    [Fact]
    public void CancellingPendingCurveRestoresExactBaselineWithoutUndo()
    {
        var canvas = new EnvironmentCanvas { Tool = EnvironmentPaintTool.CurvedLine, Color = Ink, BrushDiameter = 10 };
        byte[] baseline = canvas.ClonePixels();

        canvas.Begin(.2, .4); canvas.End(.8, .4);
        canvas.Begin(.4, .4); canvas.End(.4, .2);
        Assert.True(canvas.CurvePending);
        Assert.True(canvas.CancelPendingCurve());

        Assert.Equal(baseline, canvas.ClonePixels());
        Assert.False(canvas.CurvePending);
        Assert.False(canvas.CanUndo);
        Assert.False(canvas.IsDirty);
    }

    [Fact]
    public void ToolSwitchCancelsAnIncompleteCurve()
    {
        var canvas = new EnvironmentCanvas { Tool = EnvironmentPaintTool.CurvedLine, Color = Ink };
        byte[] baseline = canvas.ClonePixels();

        canvas.Begin(.2, .2); canvas.End(.8, .2);
        canvas.Tool = EnvironmentPaintTool.Brush;

        Assert.Equal(baseline, canvas.ClonePixels());
        Assert.Equal(EnvironmentCurvePhase.Idle, canvas.CurvePhase);
    }

    [Fact]
    public void PenAndSprayStretchTheirFootprintByPixelAspect()
    {
        var pen = new EnvironmentCanvas { Tool = EnvironmentPaintTool.Pen, Color = Ink, BrushDiameter = 40, PixelAspect = 2.0 };
        pen.Begin(.5, .5); pen.End(.5, .5);

        // A round tool under a 2:1 stretch must reach twice as far down the canvas as across it.
        Assert.True(pen.TryPick(.5 + (19.0 / 511), .5, out EnvironmentColor right));
        Assert.Equal(Ink, right);
        Assert.True(pen.TryPick(.5, .5 + (38.0 / 511), out EnvironmentColor down));
        Assert.Equal(Ink, down);
        Assert.True(pen.TryPick(.5 + (24.0 / 511), .5, out EnvironmentColor outside));
        Assert.Equal(EnvironmentCanvasPolicy.DefaultColor, outside);

        var round = new EnvironmentCanvas { Tool = EnvironmentPaintTool.Spray, Color = Ink, BrushDiameter = 40, PixelAspect = 2.0 };
        var flat = new EnvironmentCanvas { Tool = EnvironmentPaintTool.Spray, Color = Ink, BrushDiameter = 40 };
        round.Begin(.5, .5); round.End(.5, .5);
        flat.Begin(.5, .5); flat.End(.5, .5);
        Assert.True(ChangedPixels(round) > ChangedPixels(flat));
    }

    [Fact]
    public void ColorAndSizeChangesRepaintAPendingCurve()
    {
        var canvas = new EnvironmentCanvas { Tool = EnvironmentPaintTool.CurvedLine, Color = Ink, BrushDiameter = 6 };
        canvas.Begin(.2, .5); canvas.End(.8, .5);
        byte[] baseline = canvas.ClonePixels();

        canvas.Color = new EnvironmentColor(255, 0, 255);
        Assert.NotEqual(baseline, canvas.ClonePixels());

        byte[] recolored = canvas.ClonePixels();
        canvas.BrushDiameter = 24;
        Assert.NotEqual(recolored, canvas.ClonePixels());
        Assert.True(canvas.CancelPendingCurve());
    }

    private static int ChangedPixels(EnvironmentCanvas canvas)
    {
        byte[] pixels = canvas.ClonePixels();
        int changed = 0;
        for (int index = 0; index < pixels.Length; index += EnvironmentCanvasPolicy.BytesPerPixel)
        {
            if (pixels[index] != 192 || pixels[index + 1] != 192 || pixels[index + 2] != 192)
                changed++;
        }
        return changed;
    }
}
