using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Keeps the browser Paint Buddy unsaved-changes overlay derived from session state.
/// The experimental single-threaded Web runtime can strand the async signal callback that
/// normally hides/shows this panel, while the session itself has already moved on. Re-deriving
/// visibility from PendingAction on the physics callback prevents Exit, Save/Discard and Cancel
/// from leaving the browser editor behind a stale or missing modal. Native builds never compose
/// BrowserCharacterEditorRuntimeBridge, so their normal event path remains unchanged.
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
