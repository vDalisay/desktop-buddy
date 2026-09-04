using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class CharacterEditorHost
{
    /// <summary>
    /// Editors share the same preview rig. Treat rotation as transient editor state so opening
    /// Paint Buddy or Buddy Studio always starts from the canonical frontal view instead of
    /// inheriting the previous editor session's quarter-turn.
    /// </summary>
    internal void ResetPreviewRotationToFront()
    {
        _paintRotationQuarterTurns = 0;
        if (!GodotObject.IsInstanceValid(_preview))
            return;
        _preview.RotationDegrees = Vector3.Zero;
        if (_preview.IsInitialized)
            ApplyStaticPreviewPose();
    }
}
