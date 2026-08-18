using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintBucketFillTests
{
    [Fact]
    public void FillToolActsOnceAndIsExactlyUndoable()
    {
        PaintWorkspace workspace = new()
        {
            SelectedTool = PaintTool.Fill,
            SelectedColor = new PaintColor(220, 40, 80),
        };
        // Torso remains the canonical full-width surface. Head now deliberately uses the same
        // endpoint/connector half-atlas as hands and feet, so generic flood-fill behavior should
        // not accidentally assert that Head owns both lanes.
        PaintHit hit = new(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0);
        PaintSurface surface = workspace.Surfaces[PaintPart.Torso];
        string before = surface.ComputeHash();

        workspace.BeginGesture(hit);

        Assert.False(workspace.GestureActive);
        Assert.True(surface.TrySample(new PaintPoint(0.1, 0.1), out PaintColor filled));
        Assert.Equal(workspace.SelectedColor, filled);
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(before, surface.ComputeHash());
        Assert.True(workspace.Redo());
        Assert.True(surface.TrySample(new PaintPoint(0.9, 0.9), out filled));
        Assert.Equal(workspace.SelectedColor, filled);
    }

    [Fact]
    public void BucketFillPreservesDifferentColoredIslands()
    {
        PaintWorkspace workspace = new() { SelectedColor = new PaintColor(200, 30, 40) };
        PaintSurface surface = workspace.Surfaces[PaintPart.Torso];
        PaintColor island = new(12, 34, 56);
        surface.Stamp(new PaintPoint(0.5, 0.5), 64, PaintTool.Brush, island);

        Assert.True(workspace.BucketFill(new PaintHit(PaintPart.Torso, new PaintPoint(0.1, 0.1), 0)));

        Assert.True(surface.TrySample(new PaintPoint(0.1, 0.1), out PaintColor filled));
        Assert.Equal(workspace.SelectedColor, filled);
        Assert.True(surface.TrySample(new PaintPoint(0.5, 0.5), out PaintColor preserved));
        Assert.Equal(island, preserved);
    }

    [Fact]
    public void BucketFillTreatsHorizontalTextureSeamAsConnected()
    {
        PaintWorkspace workspace = new() { SelectedColor = new PaintColor(70, 140, 210) };
        PaintSurface surface = workspace.Surfaces[PaintPart.Torso];
        byte[] pixels = new byte[PaintPolicy.SurfaceBytes];
        for (int index = 0; index < pixels.Length; index += PaintPolicy.BytesPerPixel)
        {
            pixels[index] = 9;
            pixels[index + 1] = 8;
            pixels[index + 2] = 7;
            pixels[index + 3] = byte.MaxValue;
        }

        const int row = 200;
        ClearPixel(pixels, 0, row);
        ClearPixel(pixels, PaintPolicy.SurfaceSize - 1, row);
        surface.Replace(pixels);

        double v = row / (double)(PaintPolicy.SurfaceSize - 1);
        Assert.True(workspace.BucketFill(new PaintHit(PaintPart.Torso, new PaintPoint(0.0, v), 0)));

        Assert.True(surface.TrySample(new PaintPoint(0.0, v), out PaintColor left));
        Assert.True(surface.TrySample(new PaintPoint(1.0, v), out PaintColor right));
        Assert.Equal(workspace.SelectedColor, left);
        Assert.Equal(workspace.SelectedColor, right);
    }

    private static void ClearPixel(byte[] pixels, int x, int y)
    {
        int index = ((y * PaintPolicy.SurfaceSize) + x) * PaintPolicy.BytesPerPixel;
        pixels[index] = 0;
        pixels[index + 1] = 0;
        pixels[index + 2] = 0;
        pixels[index + 3] = 0;
    }
}
