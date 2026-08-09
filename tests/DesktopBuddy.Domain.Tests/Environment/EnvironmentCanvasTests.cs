using System;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Environment;

public sealed class EnvironmentCanvasTests
{
    private static readonly EnvironmentColor Ink = new(10, 20, 30);

    [Fact]
    public void BrushPaintsAlongTheDragAndUndoRestoresTheImageExactly()
    {
        var canvas = new EnvironmentCanvas { Color = Ink };
        byte[] blank = canvas.ClonePixels();

        canvas.Begin(.25, .25);
        canvas.Continue(.75, .25);
        canvas.End(.75, .25);

        Assert.True(canvas.IsDirty);
        Assert.True(canvas.CanUndo);
        Assert.Equal(Ink, Sample(canvas, .5, .25));
        Assert.True(canvas.Undo());
        Assert.Equal(blank, canvas.ClonePixels());
    }

    [Fact]
    public void EraserRepaintsTheBlankDefaultAndGesturesOffCanvasChangeNothing()
    {
        var canvas = new EnvironmentCanvas { Color = Ink, BrushDiameter = 32 };
        canvas.Begin(.5, .5);
        canvas.End(.5, .5);

        canvas.Tool = EnvironmentPaintTool.Eraser;
        canvas.Begin(.5, .5);
        canvas.End(.5, .5);
        Assert.Equal(EnvironmentCanvasPolicy.DefaultColor, Sample(canvas, .5, .5));

        byte[] before = canvas.ClonePixels();
        canvas.Begin(-1, .5);
        canvas.Continue(2, .5);
        canvas.End(2, .5);
        Assert.Equal(before, canvas.ClonePixels());
    }

    [Fact]
    public void FillFloodsTheBlankRoomAndPickReadsBackTheColorUnderThePointer()
    {
        var canvas = new EnvironmentCanvas { Color = Ink, Tool = EnvironmentPaintTool.Fill };
        canvas.Begin(.5, .5);
        canvas.End(.5, .5);

        Assert.Equal(Ink, Sample(canvas, .01, .99));
        canvas.Color = new EnvironmentColor(0, 0, 0);
        Assert.True(canvas.TryPick(.5, .5, out EnvironmentColor picked));
        Assert.Equal(Ink, picked);
    }

    [Fact]
    public void ShapesDrawFromTheDragOriginAndTheLivePreviewLeavesOnlyTheFinalShape()
    {
        var canvas = new EnvironmentCanvas { Color = Ink, BrushDiameter = 2, Tool = EnvironmentPaintTool.Line };
        canvas.Begin(.2, .2);
        canvas.Continue(.8, .8);
        canvas.End(.8, .2);

        // The preview is redrawn from the pre-drag image, so the discarded diagonal is gone.
        Assert.Equal(Ink, Sample(canvas, .5, .2));
        Assert.Equal(EnvironmentCanvasPolicy.DefaultColor, Sample(canvas, .5, .5));

        foreach (EnvironmentPaintTool shape in new[] { EnvironmentPaintTool.Square, EnvironmentPaintTool.Circle })
        {
            var shaped = new EnvironmentCanvas { Color = Ink, BrushDiameter = 2, Tool = shape };
            shaped.Begin(.2, .2);
            shaped.End(.8, .8);
            Assert.True(shaped.IsDirty);
            Assert.Equal(EnvironmentCanvasPolicy.DefaultColor, Sample(shaped, .5, .5));
        }
    }

    [Fact]
    public void ResetBlanksTheRoomAndUnchangedGesturesLeaveNoUndoStep()
    {
        var canvas = new EnvironmentCanvas { Color = Ink };
        canvas.Begin(.5, .5);
        canvas.End(.5, .5);
        canvas.Reset();

        Assert.Equal(EnvironmentCanvasPolicy.DefaultColor, Sample(canvas, .5, .5));
        canvas.MarkSaved();
        canvas.Tool = EnvironmentPaintTool.Eraser;
        canvas.Begin(.5, .5);
        canvas.End(.5, .5);
        Assert.False(canvas.CanUndo);
        Assert.False(canvas.IsDirty);
    }

    [Fact]
    public async Task StoreRoundTripsThePaintingAndFailsSafeWhenItIsMissingOrCorrupt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"desktop-buddy-canvas-{Guid.NewGuid():N}");
        var store = new EnvironmentPaintStore(new CharacterFileSystem(), root);
        try
        {
            Assert.Null(store.Load());

            var canvas = new EnvironmentCanvas { Color = Ink, Tool = EnvironmentPaintTool.Fill };
            canvas.Begin(.5, .5);
            canvas.End(.5, .5);
            await store.SaveAsync(canvas.Pixels);

            byte[]? reloaded = store.Load();
            Assert.NotNull(reloaded);
            Assert.Equal(canvas.ClonePixels(), reloaded);

            var restored = new EnvironmentCanvas();
            restored.Replace(reloaded!);
            Assert.False(restored.IsDirty);
            Assert.False(restored.CanUndo);
            Assert.Equal(Ink, Sample(restored, .1, .9));

            File.WriteAllBytes(store.PaintPath, [1, 2, 3, 4]);
            Assert.Null(store.Load());

            store.Delete();
            Assert.Null(store.Load());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static EnvironmentColor Sample(EnvironmentCanvas canvas, double x, double y)
    {
        Assert.True(canvas.TryPick(x, y, out EnvironmentColor color));
        return color;
    }
}
