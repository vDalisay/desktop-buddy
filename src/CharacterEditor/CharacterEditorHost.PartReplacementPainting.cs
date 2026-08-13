using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class CharacterEditorHost
{
    public override void _PhysicsProcess(double delta)
    {
        if (!IsInitialized || !GodotObject.IsInstanceValid(_preview) || !GodotObject.IsInstanceValid(_paintCanvas))
            return;

        SyncReplacementPaintTarget(PaintPart.Torso, BuddyPartId.Torso);
        SyncReplacementPaintTarget(PaintPart.LeftFoot, BuddyPartId.LeftFoot);
        SyncReplacementPaintTarget(PaintPart.RightFoot, BuddyPartId.RightFoot);
    }

    private void SyncReplacementPaintTarget(PaintPart paintPart, BuddyPartId buddyPart)
    {
        bool shouldBeVisible = !_preview.IsPartVisualReplaced(buddyPart);
        if (_paintCanvas.IsPartVisible(paintPart) == shouldBeVisible) return;
        _paintCanvas.SetPartVisible(paintPart, shouldBeVisible);
    }
}
