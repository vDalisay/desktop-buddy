using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

/// <summary>
/// The character editor's Save and Reset buttons are derived from this flag and are refreshed
/// only when something raises an event. A stroke raises no session change — it moves pixels, not
/// the document — so before <see cref="PaintWorkspace.DirtyChanged"/> existed the first stroke
/// left both buttons disabled and the only way out of the editor was Use Character.
/// </summary>
public sealed class PaintDirtyNotificationTests
{
    [Fact]
    public void FirstStroke_RaisesDirtyChanged()
    {
        var workspace = new PaintWorkspace();
        int raised = 0;
        workspace.DirtyChanged += () => raised++;

        Assert.False(workspace.IsDirty);

        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();

        Assert.True(workspace.IsDirty);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void FurtherStrokes_DoNotRepeatTheNotification()
    {
        var workspace = new PaintWorkspace();
        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();

        int raised = 0;
        workspace.DirtyChanged += () => raised++;

        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.25, 0.25), 0));
        workspace.EndGesture();

        // Already dirty: the buttons are already enabled, so there is nothing to tell the UI.
        Assert.Equal(0, raised);
    }

    [Fact]
    public void MarkSaved_RaisesDirtyChangedSoTheButtonsGoBackDown()
    {
        var workspace = new PaintWorkspace();
        workspace.BeginGesture(new PaintHit(PaintPart.Torso, new PaintPoint(0.5, 0.5), 0));
        workspace.EndGesture();

        int raised = 0;
        workspace.DirtyChanged += () => raised++;

        workspace.MarkSaved();

        Assert.False(workspace.IsDirty);
        Assert.Equal(1, raised);
    }
}
