using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintSymmetryTests
{
    private static readonly PaintColor Ink = new(24, 48, 72);

    [Fact]
    public void MirrorPaintsReflectedLongitudeAndUndoesAtomically()
    {
        PaintWorkspace workspace = new()
        {
            MirrorEnabled = true,
            SelectedColor = Ink,
            SelectedTool = PaintTool.Brush,
        };
        PaintSurface surface = workspace.Surfaces[PaintPart.Head];
        string before = surface.ComputeHash();
        PaintHit hit = new(PaintPart.Head, new PaintPoint(0.2, 0.5), 0);

        workspace.BeginGesture(hit);
        workspace.EndGesture();

        Assert.True(surface.TrySample(new PaintPoint(0.2, 0.5), out PaintColor original));
        Assert.True(surface.TrySample(new PaintPoint(0.8, 0.5), out PaintColor mirrored));
        Assert.Equal(Ink, original);
        Assert.Equal(Ink, mirrored);
        Assert.True(workspace.Undo());
        Assert.Equal(before, surface.ComputeHash());
    }

    [Fact]
    public void BacksidePaintsHalfCircumferenceAway()
    {
        PaintWorkspace workspace = new()
        {
            PaintBacksideEnabled = true,
            SelectedColor = Ink,
            SelectedTool = PaintTool.Brush,
        };
        PaintSurface surface = workspace.Surfaces[PaintPart.Head];
        PaintHit hit = new(PaintPart.Head, new PaintPoint(0.2, 0.5), 0);

        workspace.BeginGesture(hit);
        workspace.EndGesture();

        Assert.True(surface.TrySample(new PaintPoint(0.2, 0.5), out _));
        Assert.True(surface.TrySample(new PaintPoint(0.7, 0.5), out _));
    }

    [Fact]
    public void MirrorAndBacksideProduceFourCounterpartsInOneCommand()
    {
        PaintWorkspace workspace = new()
        {
            MirrorEnabled = true,
            PaintBacksideEnabled = true,
            SelectedColor = Ink,
            SelectedTool = PaintTool.Brush,
        };
        PaintSurface surface = workspace.Surfaces[PaintPart.Head];
        string before = surface.ComputeHash();
        PaintHit hit = new(PaintPart.Head, new PaintPoint(0.2, 0.5), 0);

        workspace.BeginGesture(hit);
        workspace.EndGesture();

        Assert.True(surface.TrySample(new PaintPoint(0.2, 0.5), out _));
        Assert.True(surface.TrySample(new PaintPoint(0.8, 0.5), out _));
        Assert.True(surface.TrySample(new PaintPoint(0.7, 0.5), out _));
        Assert.True(surface.TrySample(new PaintPoint(0.3, 0.5), out _));
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(before, surface.ComputeHash());
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void CurvePreviewReplicatesPerLaneWithoutBridgingLanes()
    {
        PaintWorkspace workspace = new()
        {
            MirrorEnabled = true,
            PaintBacksideEnabled = true,
            SelectedColor = Ink,
        };
        PaintSurface surface = workspace.Surfaces[PaintPart.Head];
        string before = surface.ComputeHash();
        PaintHit?[] samples =
        [
            new(PaintPart.Head, new PaintPoint(0.18, 0.45), 0),
            new(PaintPart.Head, new PaintPoint(0.22, 0.55), 0),
        ];

        workspace.BeginPreviewTransaction();
        workspace.RenderPreviewPath(samples);
        Assert.True(workspace.FinalizePreviewTransaction());

        Assert.True(surface.TrySample(new PaintPoint(0.2, 0.5), out _));
        Assert.True(surface.TrySample(new PaintPoint(0.8, 0.5), out _));
        Assert.True(surface.TrySample(new PaintPoint(0.7, 0.5), out _));
        Assert.True(surface.TrySample(new PaintPoint(0.3, 0.5), out _));
        Assert.True(workspace.Undo());
        Assert.Equal(before, surface.ComputeHash());
    }

    [Fact]
    public void ChangingModifierEndsActiveGestureBeforeChangingPolicy()
    {
        PaintWorkspace workspace = new() { SelectedTool = PaintTool.Brush };
        PaintHit first = new(PaintPart.Head, new PaintPoint(0.2, 0.5), 0);
        PaintHit second = new(PaintPart.Head, new PaintPoint(0.3, 0.5), 0);

        workspace.BeginGesture(first);
        workspace.MirrorEnabled = true;
        workspace.ContinueGesture(second);
        workspace.EndGesture();

        Assert.False(workspace.GestureActive);
        Assert.True(workspace.Surfaces[PaintPart.Head].TrySample(first.Uv, out _));
        Assert.False(workspace.Surfaces[PaintPart.Head].TrySample(new PaintPoint(0.7, 0.5), out _));
    }
}
