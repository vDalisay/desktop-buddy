using System;
using System.Threading.Tasks;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// The experimental single-threaded WASM runtime can leave an async Task incomplete even after the
/// browser-specific synchronous body of CharacterEditorSession has already run to completion. The
/// public browser wrappers therefore have already committed their state by the time they hand the
/// runtime bridge the stranded Task.
///
/// Recovery is invoked by BrowserUnsavedPrompt.cs from its proven-live _PhysicsProcess callback.
/// Do not use _Notification for this runtime: Chromium smoke showed the process callbacks continue
/// while the expected physics notification never reaches this C# script override. Do not use
/// UnsafeAccessor either; NativeAOT/Web previously stalled inside that accessor before completion.
/// </summary>
internal sealed partial class BrowserCharacterEditorRuntimeBridge
{
    private bool _asyncCompletionRecoveryReported;

    private void RecoverStrandedBrowserActionTask()
    {
        if (!OperatingSystem.IsBrowser() ||
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
        // A real failure is already reflected in LastError / dirty state by this point, so never
        // manufacture success over an authored persistence or validation error.
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
