using System;
using System.Threading.Tasks;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// The experimental single-threaded WASM runtime can leave an async Task incomplete even after the
/// browser-specific synchronous body of CharacterEditorSession has already run to completion. That
/// is exactly what the Chromium smoke observed: SaveAsync returned to the caller (so the synchronous
/// save and its state mutations had finished), but the Task never transitioned to IsCompleted.
///
/// Do not invoke private session methods through UnsafeAccessor here. NativeAOT/Web proved that
/// accessor itself can stall before the bridge gets a completion marker. Instead, let the public
/// browser wrapper execute its synchronous body normally, then on the next fixed tick replace only
/// the stranded Task with a result derived from the session state that body already committed.
/// </summary>
internal sealed partial class BrowserCharacterEditorRuntimeBridge
{
    // Godot Node.NOTIFICATION_PHYSICS_PROCESS. This partial cannot declare a second
    // _PhysicsProcess override because BrowserUnsavedPrompt.cs already owns it.
    private const int PhysicsProcessNotification = 16;
    private bool _asyncCompletionRecoveryReported;

    public override void _Notification(int what)
    {
        if (what != PhysicsProcessNotification ||
            !OperatingSystem.IsBrowser() ||
            !GodotObject.IsInstanceValid(_host) ||
            _actionTask is not { IsCompleted: false } ||
            _actionKind == BrowserPaintAction.None)
        {
            return;
        }

        BrowserPaintAction kind = _actionKind;
        CharacterEditorActionResult recovered = RecoverCompletedBrowserAction(kind);
        _actionTask = Task.FromResult(recovered);

        GD.Print($"DESKTOP_BUDDY_WEB_PAINT_ACTION_TASK_RECOVERED:{ActionName(kind)}");
        if (!_asyncCompletionRecoveryReported)
        {
            _asyncCompletionRecoveryReported = true;
            GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION_ASYNC_COMPLETION_STALL_RECOVERED");
        }
    }

    private CharacterEditorActionResult RecoverCompletedBrowserAction(BrowserPaintAction kind)
    {
        CharacterEditorSession session = _host.Session;

        // Browser implementations run synchronously before their public async wrappers return.
        // A real failure is therefore already reflected in LastError / dirty state by the time the
        // wrapper hands us the stranded Task. Do not manufacture success over an authored error.
        if (!string.IsNullOrWhiteSpace(session.LastError))
            return new CharacterEditorActionResult(false, Detail: session.LastError);

        return kind switch
        {
            BrowserPaintAction.Save =>
                session.WorkingDocument is not null && !session.IsDirty
                    ? new CharacterEditorActionResult(true)
                    : new CharacterEditorActionResult(false, Detail: "Browser Save did not commit its working copy."),

            BrowserPaintAction.Use =>
                session.WorkingDocument is not null && !session.IsDirty
                    ? new CharacterEditorActionResult(true)
                    : new CharacterEditorActionResult(false, Detail: "Browser Use Character did not finish saving the selected character."),

            BrowserPaintAction.UnsavedSave =>
                session.PendingAction == CharacterEditorPendingAction.None && !session.IsDirty
                    ? new CharacterEditorActionResult(true)
                    : new CharacterEditorActionResult(false, Detail: "Browser unsaved Save did not resolve the pending action."),

            BrowserPaintAction.UnsavedDiscard =>
                session.PendingAction == CharacterEditorPendingAction.None
                    ? new CharacterEditorActionResult(true)
                    : new CharacterEditorActionResult(false, Detail: "Browser Discard did not resolve the pending action."),

            BrowserPaintAction.UnsavedCancel =>
                new CharacterEditorActionResult(false),

            BrowserPaintAction.BootstrapNewPromptDiscard =>
                session.PendingAction == CharacterEditorPendingAction.None
                    ? new CharacterEditorActionResult(true)
                    : new CharacterEditorActionResult(false, Detail: "Browser new-character bootstrap discard did not resolve."),

            _ => new CharacterEditorActionResult(false, Detail: $"Unsupported browser Paint Buddy recovery action: {kind}."),
        };
    }
}
