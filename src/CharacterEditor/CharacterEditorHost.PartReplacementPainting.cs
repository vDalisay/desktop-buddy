using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class CharacterEditorHost
{
    private readonly Dictionary<PaintPart, bool> _replacementPreviousVisibility = [];

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
        if (_preview.IsPartVisualReplaced(buddyPart))
        {
            if (!_replacementPreviousVisibility.ContainsKey(paintPart))
                _replacementPreviousVisibility[paintPart] = _paintCanvas.IsPartVisible(paintPart);
            if (_paintCanvas.IsPartVisible(paintPart))
                _paintCanvas.SetPartVisible(paintPart, false);
            return;
        }

        if (!_replacementPreviousVisibility.TryGetValue(paintPart, out bool previous))
            return;
        _replacementPreviousVisibility.Remove(paintPart);
        if (_paintCanvas.IsPartVisible(paintPart) != previous)
            _paintCanvas.SetPartVisible(paintPart, previous);
    }
}
