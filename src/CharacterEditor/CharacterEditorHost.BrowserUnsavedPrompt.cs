using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Keeps browser Paint Buddy state derived from the editor session and its authored static pose.
/// The experimental single-threaded Web runtime can strand async signal continuations even while
/// process callbacks keep advancing. Re-deriving the modal and preview pose here prevents stale
/// save/exit UI and keeps artificial depth lanes from turning into screen-space offsets at 90°.
/// Native builds never compose BrowserCharacterEditorRuntimeBridge.
/// </summary>
internal sealed partial class BrowserCharacterEditorRuntimeBridge
{
    private bool? _lastUnsavedPromptState;

    public override void _Ready()
    {
        SetPhysicsProcess(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!GodotObject.IsInstanceValid(_host) || !_host.IsEditorOpen)
        {
            _lastUnsavedPromptState = null;
            return;
        }

        // Rotate the authored 2D pose first, then add its camera-depth lanes. The normal paint
        // controls still own the quarter-turn value and root yaw used by hit mapping; this pass
        // only corrects socket placement so a side view cannot explode the buddy into separated
        // head/torso/limb pieces in browser play.
        _host.ReapplyBrowserPaintPreviewPose();

        if (_host.FindChild("UnsavedChangesPrompt", true, false) is not Control prompt)
            return;

        bool shouldShow =
            _host.Session.PendingAction != CharacterEditorPendingAction.None;

        if (prompt.Visible != shouldShow)
            prompt.Visible = shouldShow;

        if (_lastUnsavedPromptState == shouldShow)
            return;

        _lastUnsavedPromptState = shouldShow;
        GD.Print(shouldShow
            ? $"DESKTOP_BUDDY_WEB_UNSAVED_PROMPT:show:{_host.Session.PendingAction}"
            : "DESKTOP_BUDDY_WEB_UNSAVED_PROMPT:hide");
    }
}
