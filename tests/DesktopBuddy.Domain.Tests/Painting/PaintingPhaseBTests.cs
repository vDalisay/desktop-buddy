using System;
using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintingPhaseBTests
{
    [Fact]
    public void DefaultMapper_MapsAllSixSeparatedFrontalParts()
    {
        FrontalPaintMapper mapper = FrontalPaintMapper.CreateDefault();

        AssertHit(mapper, new PaintPoint(0, -1.42), PaintPart.Head);
        AssertHit(mapper, new PaintPoint(0, -0.15), PaintPart.Torso);
        AssertHit(mapper, new PaintPoint(-1.02, -0.12), PaintPart.LeftHand);
        AssertHit(mapper, new PaintPoint(1.02, -0.12), PaintPart.RightHand);
        AssertHit(mapper, new PaintPoint(-0.43, 1.08), PaintPart.LeftFoot);
        AssertHit(mapper, new PaintPoint(0.43, 1.08), PaintPart.RightFoot);
        Assert.False(mapper.TryMap(new PaintPoint(3, 3), out _));
    }

    [Fact]
    public void MirroredLimbs_ReverseUWithoutChangingV()
    {
        FrontalPaintMapper mapper = FrontalPaintMapper.CreateDefault();

        Assert.True(mapper.TryMap(new PaintPoint(-0.92, -0.12), out PaintHit left));
        Assert.True(mapper.TryMap(new PaintPoint(0.92, -0.12), out PaintHit right));

        Assert.Equal(1.0 - right.Uv.X, left.Uv.X, 8);
        Assert.Equal(right.Uv.Y, left.Uv.Y, 8);
    }

    [Fact]
    public void EmptyCanvasBreaksInterpolationButGestureRemainsOneUndoCommand()
    {
        PaintWorkspace workspace = new();
        PaintHit first = new(PaintPart.Head, new PaintPoint(0.25, 0.5), 0);
        PaintHit second = new(PaintPart.Torso, new PaintPoint(0.75, 0.5), 0);
        string headBefore = workspace.Surfaces[PaintPart.Head].ComputeHash();
        string torsoBefore = workspace.Surfaces[PaintPart.Torso].ComputeHash();

        workspace.BeginGesture(first);
        workspace.ContinueGesture(null);
        workspace.ContinueGesture(second);
        workspace.EndGesture();

        Assert.True(workspace.CanUndo);
        Assert.NotEqual(headBefore, workspace.Surfaces[PaintPart.Head].ComputeHash());
        Assert.NotEqual(torsoBefore, workspace.Surfaces[PaintPart.Torso].ComputeHash());
        Assert.True(workspace.Undo());
        Assert.Equal(headBefore, workspace.Surfaces[PaintPart.Head].ComputeHash());
        Assert.Equal(torsoBefore, workspace.Surfaces[PaintPart.Torso].ComputeHash());
    }

    [Fact]
    public void EraserAndEraseAll_AreByteExactUndoableCommands()
    {
        PaintWorkspace workspace = new();
        PaintHit hit = new(PaintPart.Head, new PaintPoint(0.5, 0.5), 0);
        string blank = workspace.Surfaces[PaintPart.Head].ComputeHash();

        workspace.SelectedColor = new PaintColor(1, 2, 3);
        workspace.BeginGesture(hit);
        workspace.EndGesture();
        string painted = workspace.Surfaces[PaintPart.Head].ComputeHash();

        workspace.SelectedTool = PaintTool.Eraser;
        workspace.BeginGesture(hit);
        workspace.EndGesture();
        Assert.Equal(blank, workspace.Surfaces[PaintPart.Head].ComputeHash());
        Assert.True(workspace.Undo());
        Assert.Equal(painted, workspace.Surfaces[PaintPart.Head].ComputeHash());

        workspace.EraseAll();
        Assert.Equal(blank, workspace.Surfaces[PaintPart.Head].ComputeHash());
        Assert.True(workspace.Undo());
        Assert.Equal(painted, workspace.Surfaces[PaintPart.Head].ComputeHash());
    }

    [Fact]
    public void BrushAndViewState_ClampToLockedBudgets()
    {
        PaintWorkspace workspace = new();
        workspace.SetBrushDiameter(int.MaxValue);
        Assert.Equal(PaintPolicy.MaxBrushDiameter, workspace.BrushDiameter);
        workspace.AdjustBrush(int.MinValue);
        Assert.Equal(PaintPolicy.MinBrushDiameter, workspace.BrushDiameter);

        PaintViewState view = new();
        view.SetZoom(double.MaxValue, default);
        view.PanBy(new PaintPoint(double.MaxValue, double.MaxValue));
        Assert.Equal(PaintViewState.MaximumZoom, view.Zoom);
        Assert.InRange(view.Pan.X, -PaintViewState.MaximumZoom, PaintViewState.MaximumZoom);
        Assert.InRange(view.Pan.Y, -PaintViewState.MaximumZoom, PaintViewState.MaximumZoom);
    }

    [Fact]
    public void Policy_MatchesLockedPhaseBBudgets()
    {
        Assert.Equal(512, PaintPolicy.SurfaceSize);
        Assert.Equal(6L * 1024 * 1024, PaintPolicy.WorkingSurfaceBudgetBytes);
        Assert.Equal(48L * 1024 * 1024, PaintPolicy.UndoBudgetBytes);
        Assert.Equal(64L * 1024 * 1024, PaintPolicy.EditingBudgetBytes);
        Assert.Equal(6, PaintPolicy.WhitelistedPaths.Count);
    }

    private static void AssertHit(FrontalPaintMapper mapper, PaintPoint point, PaintPart expected)
    {
        Assert.True(mapper.TryMap(point, out PaintHit hit));
        Assert.Equal(expected, hit.Part);
        Assert.True(hit.IsValid);
    }
}
