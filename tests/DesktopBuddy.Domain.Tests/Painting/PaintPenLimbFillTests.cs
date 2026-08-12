using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintPenLimbFillTests
{
    [Fact]
    public void PenDabOnLimbReplacesStripeyLaneWithOneSolidUndoableColor()
    {
        PaintWorkspace workspace = new()
        {
            SelectedTool = PaintTool.Pen,
            SelectedColor = new PaintColor(245, 40, 210),
        };
        PaintSurface surface = workspace.Surfaces[PaintPart.LeftFoot];
        byte[] pixels = new byte[PaintPolicy.SurfaceBytes];

        // Reproduce the old failure shape: alternating painted/blank rows in the limb-end lane.
        for (int y = 0; y < PaintPolicy.SurfaceSize; y++)
        {
            for (int x = PaintUvRegion.LimbEnd.StartPixel;
                 x < PaintUvRegion.LimbEnd.StartPixel + PaintUvRegion.LimbEnd.PixelWidth;
                 x++)
            {
                if ((y / 4) % 2 == 0)
                    WritePixel(pixels, x, y, new PaintColor(20, 160, 240));
            }

            // Connector lane contains unrelated paint and must not be changed by an end-part dab.
            for (int x = PaintUvRegion.LimbConnector.StartPixel;
                 x < PaintUvRegion.LimbConnector.StartPixel + PaintUvRegion.LimbConnector.PixelWidth;
                 x++)
            {
                WritePixel(pixels, x, y, new PaintColor(30, 70, 110));
            }
        }
        surface.Replace(pixels);
        string before = surface.ComputeHash();

        workspace.BeginGesture(null);
        workspace.StampPenDab([
            new PaintHit(PaintPart.LeftFoot, new PaintPoint(0.25, 0.5), 0.0),
        ]);
        workspace.EndGesture();

        AssertSolid(surface, 0.01, workspace.SelectedColor);
        AssertSolid(surface, 0.25, workspace.SelectedColor);
        AssertSolid(surface, 0.49, workspace.SelectedColor);
        AssertSolid(surface, 0.75, new PaintColor(30, 70, 110));
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(before, surface.ComputeHash());
    }

    [Fact]
    public void PenDabOnConnectorSolidFillsOnlyConnectorLane()
    {
        PaintWorkspace workspace = new()
        {
            SelectedTool = PaintTool.Pen,
            SelectedColor = new PaintColor(255, 210, 20),
        };
        PaintSurface surface = workspace.Surfaces[PaintPart.RightHand];

        workspace.BeginGesture(null);
        workspace.StampPenDab([
            new PaintHit(
                PaintPart.RightHand,
                PaintUvRegion.LimbConnector.MapLocal(new PaintPoint(0.5, 0.5)),
                0.0,
                IsConnector: true),
        ]);
        workspace.EndGesture();

        Assert.False(surface.TrySample(new PaintPoint(0.25, 0.5), out _));
        AssertSolid(surface, 0.51, workspace.SelectedColor);
        AssertSolid(surface, 0.75, workspace.SelectedColor);
        AssertSolid(surface, 0.99, workspace.SelectedColor);
    }

    private static void AssertSolid(PaintSurface surface, double u, PaintColor expected)
    {
        foreach (double v in new[] { 0.01, 0.25, 0.5, 0.75, 0.99 })
        {
            Assert.True(surface.TrySample(new PaintPoint(u, v), out PaintColor actual));
            Assert.Equal(expected, actual);
        }
    }

    private static void WritePixel(byte[] pixels, int x, int y, PaintColor color)
    {
        int index = ((y * PaintPolicy.SurfaceSize) + x) * PaintPolicy.BytesPerPixel;
        pixels[index] = color.R;
        pixels[index + 1] = color.G;
        pixels[index + 2] = color.B;
        pixels[index + 3] = byte.MaxValue;
    }
}
