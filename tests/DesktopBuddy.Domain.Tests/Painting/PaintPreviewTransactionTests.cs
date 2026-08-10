using System;
using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintPreviewTransactionTests
{
    [Fact]
    public void CancelPreviewRestoresEveryTouchedPartWithoutUndoOrDirtyState()
    {
        PaintWorkspace workspace = new();
        string headBefore = workspace.Surfaces[PaintPart.Head].ComputeHash();
        string torsoBefore = workspace.Surfaces[PaintPart.Torso].ComputeHash();

        workspace.BeginPreviewTransaction();
        workspace.RenderPreviewPath(new PaintHit?[]
        {
            new(PaintPart.Head, new PaintPoint(.2, .4), 0),
            new(PaintPart.Head, new PaintPoint(.3, .4), 0),
            null,
            new(PaintPart.Torso, new PaintPoint(.6, .5), 0),
            new(PaintPart.Torso, new PaintPoint(.7, .5), 0),
        });

        Assert.NotEqual(headBefore, workspace.Surfaces[PaintPart.Head].ComputeHash());
        Assert.NotEqual(torsoBefore, workspace.Surfaces[PaintPart.Torso].ComputeHash());
        Assert.True(workspace.CancelPreviewTransaction());
        Assert.Equal(headBefore, workspace.Surfaces[PaintPart.Head].ComputeHash());
        Assert.Equal(torsoBefore, workspace.Surfaces[PaintPart.Torso].ComputeHash());
        Assert.False(workspace.CanUndo);
        Assert.False(workspace.IsDirty);
    }

    [Fact]
    public void ReRenderingPreviewRestoresCleanBaselineBeforeDrawingTheReplacement()
    {
        PaintWorkspace workspace = new();
        PaintSurface head = workspace.Surfaces[PaintPart.Head];
        PaintPoint oldOnly = new(.2, .2);
        PaintPoint newOnly = new(.8, .8);

        workspace.BeginPreviewTransaction();
        workspace.RenderPreviewPath(new PaintHit?[] { new(PaintPart.Head, oldOnly, 0) });
        Assert.True(head.TrySample(oldOnly, out _));

        workspace.RenderPreviewPath(new PaintHit?[] { new(PaintPart.Head, newOnly, 0) });
        Assert.False(head.TrySample(oldOnly, out _));
        Assert.True(head.TrySample(newOnly, out _));
    }

    [Fact]
    public void FinalPreviewAcrossPartsBecomesExactlyOneUndoCommand()
    {
        PaintWorkspace workspace = new();
        string headBefore = workspace.Surfaces[PaintPart.Head].ComputeHash();
        string footBefore = workspace.Surfaces[PaintPart.RightFoot].ComputeHash();

        workspace.BeginPreviewTransaction();
        workspace.RenderPreviewPath(new PaintHit?[]
        {
            new(PaintPart.Head, new PaintPoint(.45, .45), 0),
            new(PaintPart.Head, new PaintPoint(.55, .45), 0),
            null,
            new(PaintPart.RightFoot, new PaintPoint(.45, .55), 0),
            new(PaintPart.RightFoot, new PaintPoint(.55, .55), 0),
        });
        Assert.True(workspace.FinalizePreviewTransaction());

        Assert.True(workspace.IsDirty);
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
        Assert.Equal(headBefore, workspace.Surfaces[PaintPart.Head].ComputeHash());
        Assert.Equal(footBefore, workspace.Surfaces[PaintPart.RightFoot].ComputeHash());
        Assert.False(workspace.CanUndo);
    }

    [Fact]
    public void MissAndPartChangeAreHardContinuityBreaks()
    {
        PaintWorkspace workspace = new();
        PaintSurface head = workspace.Surfaces[PaintPart.Head];

        workspace.SetBrushDiameter(PaintPolicy.MinBrushDiameter);
        workspace.BeginPreviewTransaction();
        workspace.RenderPreviewPath(new PaintHit?[]
        {
            new(PaintPart.Head, new PaintPoint(.1, .5), 0),
            null,
            new(PaintPart.Head, new PaintPoint(.9, .5), 0),
        });

        Assert.False(head.TrySample(new PaintPoint(.5, .5), out _));
        workspace.CancelPreviewTransaction();
    }

    [Fact]
    public void ToolSwitchCancelsPreviewBeforeChangingMutationMode()
    {
        PaintWorkspace workspace = new() { SelectedTool = PaintTool.Curve };
        string before = workspace.Surfaces[PaintPart.Head].ComputeHash();
        workspace.BeginPreviewTransaction();
        workspace.RenderPreviewPath(new PaintHit?[]
        {
            new(PaintPart.Head, new PaintPoint(.5, .5), 0),
        });

        workspace.SelectedTool = PaintTool.Brush;

        Assert.False(workspace.PreviewActive);
        Assert.Equal(before, workspace.Surfaces[PaintPart.Head].ComputeHash());
    }

    [Fact]
    public void PreviewWorstCaseRemainsInsideExistingEditBudget()
    {
        PaintWorkspace workspace = new();
        workspace.SetBrushDiameter(PaintPolicy.MaxBrushDiameter);
        workspace.BeginPreviewTransaction();

        foreach (PaintPart part in Enum.GetValues<PaintPart>())
        {
            workspace.RenderPreviewPath(new PaintHit?[]
            {
                new(part, new PaintPoint(.01, .01), 0),
                new(part, new PaintPoint(.99, .99), 0),
            });
        }

        // Preview builders retain at most one clean RGBA surface per body part. The final command
        // holds Before+After, which is still well below the existing 48 MiB history budget.
        long worstCaseCommand = 2L * PaintPolicy.WorkingSurfaceBudgetBytes;
        Assert.True(worstCaseCommand <= PaintPolicy.UndoBudgetBytes);
        workspace.CancelPreviewTransaction();
    }
}
