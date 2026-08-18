using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintPenLimbDabTests
{
    [Theory]
    [InlineData(PaintPart.LeftHand)]
    [InlineData(PaintPart.RightHand)]
    [InlineData(PaintPart.LeftFoot)]
    [InlineData(PaintPart.RightFoot)]
    public void SmallPenDabOnLimbStaysLocalizedAndUndoable(PaintPart part)
    {
        PaintWorkspace workspace = new()
        {
            SelectedTool = PaintTool.Pen,
            SelectedColor = new PaintColor(245, 40, 210),
        };
        PaintSurface surface = workspace.Surfaces[part];
        string before = surface.ComputeHash();

        workspace.BeginGesture(null);
        workspace.StampPenDab([
            new PaintHit(part, new PaintPoint(0.25, 0.5), 0.0),
        ]);
        workspace.EndGesture();

        Assert.True(surface.TrySample(new PaintPoint(0.25, 0.5), out PaintColor actual));
        Assert.Equal(workspace.SelectedColor, actual);
        Assert.False(surface.TrySample(new PaintPoint(0.05, 0.1), out _));
        Assert.False(surface.TrySample(new PaintPoint(0.45, 0.9), out _));
        Assert.False(surface.TrySample(new PaintPoint(0.75, 0.5), out _));
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(before, surface.ComputeHash());
    }

    [Fact]
    public void SmallPenDabOnConnectorStaysLocalizedToConnectorLane()
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

        Assert.True(surface.TrySample(new PaintPoint(0.75, 0.5), out PaintColor actual));
        Assert.Equal(workspace.SelectedColor, actual);
        Assert.False(surface.TrySample(new PaintPoint(0.25, 0.5), out _));
        Assert.False(surface.TrySample(new PaintPoint(0.55, 0.1), out _));
        Assert.False(surface.TrySample(new PaintPoint(0.95, 0.9), out _));
    }

    [Fact]
    public void HeadAndNeckUseSameSeparateEndpointConnectorLanesAsOtherLimbs()
    {
        PaintHit normalHead = new(
            PaintPart.Head,
            PaintUvRegion.LimbEnd.MapLocal(new PaintPoint(0.5, 0.5)),
            0.0);
        PaintHit neck = new(
            PaintPart.Head,
            PaintUvRegion.LimbConnector.MapLocal(new PaintPoint(0.5, 0.5)),
            0.0,
            IsConnector: true);

        Assert.Equal(PaintUvRegion.LimbEnd, PaintUvRegion.For(normalHead));
        Assert.Equal(PaintUvRegion.LimbConnector, PaintUvRegion.For(neck));

        PaintWorkspace workspace = new()
        {
            SelectedTool = PaintTool.Pen,
            SelectedColor = new PaintColor(40, 210, 255),
        };
        PaintSurface surface = workspace.Surfaces[PaintPart.Head];

        Assert.True(workspace.BucketFill(neck));
        Assert.True(surface.TrySample(new PaintPoint(0.75, 0.5), out PaintColor connectorColor));
        Assert.Equal(workspace.SelectedColor, connectorColor);
        Assert.False(surface.TrySample(new PaintPoint(0.25, 0.5), out _));
    }
}
