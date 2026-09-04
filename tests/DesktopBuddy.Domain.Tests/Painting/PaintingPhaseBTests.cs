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

        AssertHit(mapper, new PaintPoint(0, -50), PaintPart.Head);
        AssertHit(mapper, new PaintPoint(0, 0), PaintPart.Torso);
        AssertHit(mapper, new PaintPoint(-38, -5), PaintPart.LeftHand);
        AssertHit(mapper, new PaintPoint(38, -5), PaintPart.RightHand);
        AssertHit(mapper, new PaintPoint(-22, 55), PaintPart.LeftFoot);
        AssertHit(mapper, new PaintPoint(22, 55), PaintPart.RightFoot);
        Assert.False(mapper.TryMap(new PaintPoint(300, 300), out _));
    }

    [Fact]
    public void MirroredLimbs_ReverseUWithoutChangingV()
    {
        FrontalPaintMapper mapper = FrontalPaintMapper.CreateDefault();

        Assert.True(mapper.TryMap(new PaintPoint(-34, -5), out PaintHit left));
        Assert.True(mapper.TryMap(new PaintPoint(34, -5), out PaintHit right));

        Assert.Equal(1.0 - right.Uv.X, left.Uv.X, 8);
        Assert.Equal(right.Uv.Y, left.Uv.Y, 8);
    }

    [Theory]
    // Measured from Godot's generated mesh arrays: a sphere's camera-facing pole is the u seam,
    // its sides are 0.25/0.75; the capsule is offset half a turn, so its front is 0.5.
    [InlineData(0.0, -50.0, 0.0, 0.5)]        // head centre — front pole, equator
    [InlineData(24.0, -50.0, 0.25, 0.5)]      // head right silhouette
    [InlineData(-24.0, -50.0, 0.75, 0.5)]     // head left silhouette
    [InlineData(0.0, -74.0, 0.0, 0.0)]        // head top pole
    // The head's bottom pole is not on this list: the torso's top cap bulges in front of it, so
    // frontally that pixel is torso. Nothing frontal can paint it, because nothing frontal shows
    // it - the same reason the head's back is out of reach until the preview is turned.
    [InlineData(0.0, 0.0, 0.5, 0.5)]          // torso centre — capsule front, mid band
    [InlineData(28.0, 7.0, 0.75, 2.0 / 3.0)]  // torso right silhouette, below the hand
    [InlineData(0.0, -7.0, 0.5, 1.0 / 3.0)]   // torso cylinder/top-cap boundary
    [InlineData(0.0, 7.0, 0.5, 2.0 / 3.0)]    // torso cylinder/bottom-cap boundary
    [InlineData(0.0, 35.0, 0.5, 1.0)]         // torso bottom (its top is behind the head)
    public void FrontalHits_MatchTheGeneratedMeshUvLayout(double x, double y, double u, double v)
    {
        FrontalPaintMapper mapper = FrontalPaintMapper.CreateDefault();

        Assert.True(mapper.TryMap(new PaintPoint(x, y), out PaintHit hit));
        Assert.Equal(u, hit.Uv.X, 2);
        Assert.Equal(v, hit.Uv.Y, 2);
    }

    [Fact]
    public void SeamStroke_TakesTheShortWayAcrossTheWrap()
    {
        PaintSurface surface = new();
        PaintSurface reference = new();

        // 0.98 -> 0.02 is 4% the short way and 96% the long way.
        surface.Stroke(new PaintPoint(0.98, 0.5), new PaintPoint(0.02, 0.5), 8, PaintTool.Brush, PaintColor.White);
        reference.Stamp(new PaintPoint(0.5, 0.5), 8, PaintTool.Brush, PaintColor.White);

        int painted = CountOpaque(surface);
        int oneStamp = CountOpaque(reference);
        Assert.InRange(painted, oneStamp, oneStamp * 4);
    }

    /// <summary>
    /// Overlaps resolve to whichever part's surface is nearest the viewer there - the same test
    /// the renderer runs - so the brush lands on the part the player can see. Resolving by lane
    /// instead handed clicks to a part that was visibly behind another in these bands, and the
    /// paint went on out of sight (owner report 2026-08-24).
    /// </summary>
    [Fact]
    public void OverlappingParts_ResolveToWhicheverSurfaceIsInFront()
    {
        FrontalPaintMapper mapper = FrontalPaintMapper.CreateDefault();

        // A hand's own middle is well in front of the torso beside it.
        Assert.True(mapper.TryMap(new PaintPoint(-38, -5), out PaintHit hand));
        Assert.Equal(PaintPart.LeftHand, hand.Part);

        // Nearer the torso's middle the hand has thinned to its edge and the torso bulges past
        // it, which is exactly what the player sees there.
        Assert.True(mapper.TryMap(new PaintPoint(-25, -5), out PaintHit join));
        Assert.Equal(PaintPart.Torso, join.Part);

        // The head bulges in front of the torso's top.
        Assert.True(mapper.TryMap(new PaintPoint(0, -35), out PaintHit covered));
        Assert.Equal(PaintPart.Head, covered.Part);

        // ... and the strip under the chin is the torso's cap, so that is what takes the paint.
        Assert.True(mapper.TryMap(new PaintPoint(0, -26), out PaintHit neck));
        Assert.Equal(PaintPart.Torso, neck.Part);
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
    public void GestureStartedOffSurfacePaintsOnceTheDragCrossesTheCharacter()
    {
        PaintWorkspace workspace = new();
        PaintHit onBody = new(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0);
        string before = workspace.Surfaces[PaintPart.Torso].ComputeHash();

        workspace.BeginGesture(null);
        workspace.ContinueGesture(onBody);
        workspace.EndGesture();

        Assert.NotEqual(before, workspace.Surfaces[PaintPart.Torso].ComputeHash());
        Assert.True(workspace.Undo());
        Assert.Equal(before, workspace.Surfaces[PaintPart.Torso].ComputeHash());
    }

    [Fact]
    public void PenDabCommitsMappedSamplesAsOneUndoableGesture()
    {
        PaintWorkspace workspace = new() { SelectedTool = PaintTool.Pen };
        PaintHit hit = new(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0);
        string before = workspace.Surfaces[PaintPart.Torso].ComputeHash();

        workspace.BeginGesture(null);
        workspace.StampPenDab([hit]);
        workspace.EndGesture();

        Assert.NotEqual(before, workspace.Surfaces[PaintPart.Torso].ComputeHash());
        Assert.True(workspace.Undo());
        Assert.Equal(before, workspace.Surfaces[PaintPart.Torso].ComputeHash());
    }

    [Fact]
    public void FastDragThatSkipsOverTheSilhouetteStillDrawsAContinuousStroke()
    {
        PaintWorkspace bridged = new();
        PaintWorkspace direct = new();
        PaintHit from = new(PaintPart.Torso, new PaintPoint(0.45, 0.45), 0);
        PaintHit to = new(PaintPart.Torso, new PaintPoint(0.55, 0.55), 0);

        bridged.BeginGesture(from);
        bridged.ContinueGesture(null);
        bridged.ContinueGesture(to);
        bridged.EndGesture();

        direct.BeginGesture(from);
        direct.ContinueGesture(to);
        direct.EndGesture();

        Assert.Equal(
            direct.Surfaces[PaintPart.Torso].ComputeHash(),
            bridged.Surfaces[PaintPart.Torso].ComputeHash());
    }

    [Fact]
    public void ReturningFarAcrossThePartDoesNotSmearAJoiningStroke()
    {
        PaintWorkspace workspace = new();
        PaintPoint far = new(0.9, 0.9);

        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.1, 0.1), 0));
        workspace.ContinueGesture(null);
        workspace.ContinueGesture(new PaintHit(PaintPart.Torso, far, 0));
        workspace.EndGesture();

        PaintSurface surface = workspace.Surfaces[PaintPart.Torso];
        Assert.True(surface.TrySample(new PaintPoint(0.1, 0.1), out _));
        Assert.True(surface.TrySample(far, out _));
        Assert.False(surface.TrySample(new PaintPoint(0.5, 0.5), out _));
    }

    [Fact]
    public void GestureThatNeverTouchesTheCharacterAddsNoUndoStep()
    {
        PaintWorkspace workspace = new();

        workspace.BeginGesture(null);
        workspace.ContinueGesture(null);
        workspace.EndGesture();

        Assert.False(workspace.CanUndo);
        Assert.False(workspace.IsDirty);
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

    private static int CountOpaque(PaintSurface surface)
    {
        int count = 0;
        ReadOnlySpan<byte> pixels = surface.Pixels.Span;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
                count++;
        }
        return count;
    }

    private static void AssertHit(FrontalPaintMapper mapper, PaintPoint point, PaintPart expected)
    {
        Assert.True(mapper.TryMap(point, out PaintHit hit));
        Assert.Equal(expected, hit.Part);
        Assert.True(hit.IsValid);
    }
}
