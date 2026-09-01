using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Browser-only hard stop around the experimental WASM async-state-machine failure. The Web
/// runtime has repeatedly stranded CharacterEditorSession's Task continuations even when every
/// persistence primitive completed inline and both custom SynchronizationContexts were being
/// drained. Run the already-authored synchronous browser session paths from the fixed-tick phase
/// before the idle-process bridge can start an async state machine at all.
///
/// The smoke driver emits its button signals from _PhysicsProcess at the default priority. This
/// bridge intentionally runs later in the same fixed tick so the queued action is consumed before
/// BrowserCharacterEditorRuntimeBridge._Process reaches StartQueuedAction. A fallback also replaces
/// an already-stranded task on the following fixed tick, which covers real GUI input that arrives
/// after the fixed-tick phase.
/// </summary>
internal sealed partial class BrowserCharacterEditorRuntimeBridge
{
    // Godot Node.NOTIFICATION_PHYSICS_PROCESS. Use the stable engine notification value here
    // because this partial already owns _PhysicsProcess in BrowserUnsavedPrompt.cs and C# cannot
    // declare a second override for the same partial type.
    private const int PhysicsProcessNotification = 16;
    private bool _syncRecoveryReported;

    public override void _EnterTree()
    {
        // Lower priorities run first. The itch smoke driver uses the default priority (0), so run
        // after it has emitted a Save/Use/unsaved-prompt signal but before the idle process phase.
        ProcessPhysicsPriority = 1000;
    }

    public override void _Notification(int what)
    {
        if (what != PhysicsProcessNotification ||
            !OperatingSystem.IsBrowser() ||
            !GodotObject.IsInstanceValid(_host))
        {
            return;
        }

        if (_actionTask is null && _queuedAction != BrowserPaintAction.None)
        {
            BrowserPaintAction kind = _queuedAction;
            _queuedAction = BrowserPaintAction.None;
            _queuedMarker = null;
            ExecuteSynchronously(kind, rescuedAsyncTask: false);
            return;
        }

        if (_actionTask is { IsCompleted: false } && _actionKind != BrowserPaintAction.None)
        {
            // If GUI input happened after this frame's fixed tick, the existing idle bridge may
            // already have started the known-bad async state machine. Replace it on the next fixed
            // tick with the same synchronous browser operation. Save is transactional/idempotent,
            // and Use simply re-queues the latest activation sequence.
            BrowserPaintAction kind = _actionKind;
            ExecuteSynchronously(kind, rescuedAsyncTask: true);
        }
    }

    private void ExecuteSynchronously(BrowserPaintAction kind, bool rescuedAsyncTask)
    {
        try
        {
            _actionKind = kind;
            CharacterEditorActionResult result = kind switch
            {
                BrowserPaintAction.Save =>
                    CharacterEditorSessionBrowserSynchronousAccess.Save(_host.Session, default),
                BrowserPaintAction.Use =>
                    CharacterEditorSessionBrowserSynchronousAccess.Use(_host.Session, default),
                BrowserPaintAction.UnsavedSave =>
                    CharacterEditorSessionBrowserSynchronousAccess.ResolveUnsaved(
                        _host.Session, UnsavedDecision.Save, default),
                BrowserPaintAction.UnsavedDiscard =>
                    CharacterEditorSessionBrowserSynchronousAccess.ResolveUnsaved(
                        _host.Session, UnsavedDecision.Discard, default),
                BrowserPaintAction.UnsavedCancel =>
                    CharacterEditorSessionBrowserSynchronousAccess.ResolveUnsaved(
                        _host.Session, UnsavedDecision.Cancel, default),
                BrowserPaintAction.BootstrapNewPromptDiscard =>
                    CharacterEditorSessionBrowserSynchronousAccess.ResolveUnsaved(
                        _host.Session, UnsavedDecision.Discard, default),
                _ => throw new InvalidOperationException(
                    $"Unsupported synchronous browser Paint Buddy action: {kind}."),
            };

            _actionTask = Task.FromResult(result);
            GD.Print($"DESKTOP_BUDDY_WEB_PAINT_ACTION_SYNC_STARTED:{ActionName(kind)}");
            if (rescuedAsyncTask && !_syncRecoveryReported)
            {
                _syncRecoveryReported = true;
                GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION_ASYNC_STALL_RECOVERED");
            }
        }
        catch (Exception exception)
        {
            _actionTask = null;
            _actionKind = BrowserPaintAction.None;
            ReportFailure(ActionName(kind), exception.ToString());
        }
    }
}

/// <summary>
/// Compile-time access to CharacterEditorSession's browser-only synchronous implementations. These
/// methods are already directly referenced by the session's public API, so their code is rooted in
/// NativeAOT; UnsafeAccessor avoids reflection metadata and, critically, avoids entering any async
/// state machine in the experimental Web runtime.
/// </summary>
internal static class CharacterEditorSessionBrowserSynchronousAccess
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "SaveBrowserSynchronously")]
    internal static extern CharacterEditorActionResult Save(
        CharacterEditorSession session,
        CancellationToken token);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "UseCharacterBrowserSynchronously")]
    internal static extern CharacterEditorActionResult Use(
        CharacterEditorSession session,
        CancellationToken token);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ResolveUnsavedBrowserSynchronously")]
    internal static extern CharacterEditorActionResult ResolveUnsaved(
        CharacterEditorSession session,
        UnsavedDecision decision,
        CancellationToken token);
}
