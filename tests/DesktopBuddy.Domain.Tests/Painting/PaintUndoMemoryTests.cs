using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintUndoMemoryTests
{
    [Fact]
    public void OrdinaryStroke_RetainsOnlyItsDirtyRectangle()
    {
        var workspace = new PaintWorkspace();

        workspace.BeginGesture(new PaintHit(PaintPart.Head, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();

        Assert.True(workspace.CanUndo);
        Assert.InRange(workspace.UndoMemoryBytes, 1, PaintPolicy.SurfaceBytes - 1);
    }

    [Fact]
    public void ManySmallStrokes_StayWithinBudgetAndNewestRemainsUndoable()
    {
        var workspace = new PaintWorkspace();
        for (int index = 0; index < 5000; index++)
        {
            PaintPart part = (PaintPart)(index % 6);
            double u = ((index * 17) % 100) / 100.0;
            double v = ((index * 29) % 100) / 100.0;
            workspace.BeginGesture(new PaintHit(part, new PaintPoint(u, v), 0));
            workspace.EndGesture();
        }

        Assert.True(workspace.UndoMemoryBytes <= PaintPolicy.UndoBudgetBytes);
        Assert.True(workspace.CanUndo);
        Assert.True(workspace.Undo());
    }

    [Fact]
    public void ExpandedStrokePatch_RestoresOriginalBytesExactly()
    {
        var workspace = new PaintWorkspace();
        workspace.SetBrushDiameter(PaintPolicy.MaxBrushDiameter);
        string before = workspace.Surfaces[PaintPart.Torso].ComputeHash();

        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.2, 0.4), 0));
        long allocationStart = System.GC.GetAllocatedBytesForCurrentThread();
        for (int step = 1; step <= 120; step++)
            workspace.ContinueGesture(new PaintHit(
                PaintPart.Torso,
                new PaintPoint(0.2 + 0.6 * step / 120.0, 0.4 + 0.2 * step / 120.0),
                0));
        workspace.EndGesture();
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - allocationStart;

        Assert.NotEqual(before, workspace.Surfaces[PaintPart.Torso].ComputeHash());
        Assert.InRange(allocated, 1, 8 * 1024 * 1024);
        Assert.True(workspace.Undo());
        Assert.Equal(before, workspace.Surfaces[PaintPart.Torso].ComputeHash());
    }

    [Fact]
    public void EraseAll_UsesCompleteBeforeStateWithinTheSameCap()
    {
        var workspace = new PaintWorkspace();
        foreach (PaintPart part in System.Enum.GetValues<PaintPart>())
        {
            workspace.SetBrushDiameter(PaintPolicy.MaxBrushDiameter);
            workspace.BeginGesture(new PaintHit(part, new PaintPoint(0.5, 0.5), 0));
            workspace.EndGesture();
        }
        string head = workspace.Surfaces[PaintPart.Head].ComputeHash();

        workspace.EraseAll();

        Assert.True(workspace.UndoMemoryBytes <= PaintPolicy.UndoBudgetBytes);
        Assert.True(workspace.Undo());
        Assert.Equal(head, workspace.Surfaces[PaintPart.Head].ComputeHash());
    }
}
