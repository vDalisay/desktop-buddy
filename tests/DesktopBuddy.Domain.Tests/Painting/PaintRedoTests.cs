using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintRedoTests
{
    [Fact]
    public void UndoThenRedo_RestoresEditedPixels()
    {
        var workspace = new PaintWorkspace();
        string before = workspace.Surfaces[PaintPart.Head].ComputeHash();

        workspace.BeginGesture(new PaintHit(PaintPart.Head, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();
        string painted = workspace.Surfaces[PaintPart.Head].ComputeHash();

        Assert.NotEqual(before, painted);
        Assert.True(workspace.Undo());
        Assert.Equal(before, workspace.Surfaces[PaintPart.Head].ComputeHash());
        Assert.True(workspace.CanRedo);

        Assert.True(workspace.Redo());
        Assert.Equal(painted, workspace.Surfaces[PaintPart.Head].ComputeHash());
        Assert.True(workspace.CanUndo);
    }

    [Fact]
    public void NewEditAfterUndo_ClearsRedoBranch()
    {
        var workspace = new PaintWorkspace();

        workspace.BeginGesture(new PaintHit(PaintPart.Head, new PaintPoint(0.25, 0.25), 0));
        workspace.EndGesture();
        Assert.True(workspace.Undo());
        Assert.True(workspace.CanRedo);

        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.75, 0.75), 0));
        workspace.EndGesture();

        Assert.False(workspace.CanRedo);
        Assert.False(workspace.Redo());
    }

    [Fact]
    public void UndoAndRedoShareConfiguredMemoryBudget()
    {
        var workspace = new PaintWorkspace();
        for (int index = 0; index < 2000; index++)
        {
            PaintPart part = (PaintPart)(index % 6);
            double coordinate = (index % 100) / 100.0;
            workspace.BeginGesture(new PaintHit(part, new PaintPoint(coordinate, 1.0 - coordinate), 0));
            workspace.EndGesture();
        }

        for (int index = 0; index < 50 && workspace.CanUndo; index++)
            workspace.Undo();

        Assert.True(workspace.UndoMemoryBytes <= PaintPolicy.UndoBudgetBytes);
    }
}
