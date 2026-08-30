using System;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Browser-only startup bridge for the experimental itch.io Web build. The custom single-threaded
/// runtime advances normal Node processing reliably during startup, while early Timer/deferred
/// continuations have both been observed to stall. A tiny processing pump therefore waits for the
/// character coordinator and enters the existing UI composition path synchronously. Native builds
/// never enter this path.
/// </summary>
public partial class CharacterEditorHost
{
    private BrowserCharacterUiStartupPump? _browserInitializationPump;
    private BrowserCharacterEditorRuntimeBridge? _browserRuntimeBridge;
    private bool _browserInitializationFailed;

    public void EnsureBrowserInitialized()
    {
        if (!OperatingSystem.IsBrowser() || IsInitialized || _browserInitializationFailed)
            return;

        // _Ready may already have armed the browser Timer before the character coordinator was
        // published. Disable that competing path. The smoke runner proves Node._Process continues
        // to advance even when that Timer/deferred startup work does not.
        StopBrowserInitializationTimer();

        if (_context.Characters is null || _selectionRuntime.Coordinator is null)
        {
            EnsureBrowserInitializationPump();
            return;
        }

        try
        {
            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_INITIALIZING");

            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_STAGE:preview-begin");
            BuildPreview();
            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_STAGE:preview-ready");

            // The native coordinator captures/restores OS-window geometry and transparency.
            // Browser play has one embedded canvas, so it only needs the shared lifecycle pause
            // while Paint Buddy owns the screen. Supplying this lightweight adapter keeps the
            // existing OpenEditorAsync/CloseEditorImmediately contract intact without touching
            // the native desktop APIs that previously stopped the static-WASM startup callback.
            _mode = new BrowserCharacterEditorModeCoordinator(_sandbox.Lifecycle);
            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_STAGE:mode-browser-ready");

            // The runtime CharacterStore is backed by GodotBrowserCharacterFileSystem. The library
            // must use the same browser-safe boundary: constructing CharacterFileSystem here would
            // quietly reintroduce System.IO as soon as a saved character is enumerated.
            var library = new CharacterLibraryIndex(
                new GodotBrowserCharacterFileSystem(),
                _context.Characters.Paths.Root);
            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_STAGE:library-ready");

            _session = new CharacterEditorSession(
                _context.Characters,
                library,
                _selectionRuntime.Coordinator,
                _preview,
                economy: _context.Economy);
            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_STAGE:session-ready");
            _session.Changed += RefreshAll;
            _session.LibraryChanged += RefreshLibrary;
            _session.CloseResolved += closed =>
            {
                if (closed)
                    CloseEditorImmediately();
            };

            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_STAGE:build-ui-begin");
            BuildUi();
            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_STAGE:build-ui-ready");
            IsInitialized = true;
            RefreshAll();
            EnsureBrowserRuntimeBridge();
            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_STAGE:refresh-ready");
            CallDeferred(MethodName.RefreshDockHitRegions);
            StopBrowserInitializationPump();
            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_READY");
        }
        catch (Exception exception)
        {
            _browserInitializationFailed = true;
            StopBrowserInitializationPump();
            GD.PushError($"Character editor browser initialization failed: {exception}");
        }
    }

    private void EnsureBrowserInitializationPump()
    {
        if (GodotObject.IsInstanceValid(_browserInitializationPump))
            return;

        _browserInitializationPump = new BrowserCharacterUiStartupPump
        {
            Name = "BrowserCharacterUiStartupPump",
            ProcessMode = ProcessModeEnum.Always,
        };
        _browserInitializationPump.Configure(this);
        AddChild(_browserInitializationPump);
    }

    private void EnsureBrowserRuntimeBridge()
    {
        if (GodotObject.IsInstanceValid(_browserRuntimeBridge))
            return;

        _browserRuntimeBridge = new BrowserCharacterEditorRuntimeBridge
        {
            Name = "BrowserCharacterEditorRuntimeBridge",
            ProcessMode = ProcessModeEnum.Always,
        };
        _browserRuntimeBridge.Configure(this);
        AddChild(_browserRuntimeBridge);
    }

    private void StopBrowserInitializationPump()
    {
        if (!GodotObject.IsInstanceValid(_browserInitializationPump))
            return;

        _browserInitializationPump!.SetProcess(false);
        _browserInitializationPump.QueueFree();
        _browserInitializationPump = null;
    }
}

/// <summary>
/// Canvas-only counterpart of CharacterEditorModeCoordinator. Web has no native desktop-window
/// state to transition; the only semantic requirement is to freeze gameplay/lifecycle mutation
/// while the editor overlay is open and resume it when the editor closes.
/// </summary>
internal sealed class BrowserCharacterEditorModeCoordinator : CharacterEditorModeCoordinator
{
    private readonly LifecycleCoordinator _lifecycle;
    private bool _active;

    public BrowserCharacterEditorModeCoordinator(LifecycleCoordinator lifecycle)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    public override bool Enter()
    {
        if (_active)
            return false;
        _lifecycle.SetEditorMode(true);
        _active = true;
        GD.Print("DESKTOP_BUDDY_WEB_PAINT_MODE_ENTERED");
        return true;
    }

    public override bool Exit()
    {
        if (!_active)
            return false;
        _active = false;
        _lifecycle.SetEditorMode(false);
        GD.Print("DESKTOP_BUDDY_WEB_PAINT_MODE_EXITED");
        return true;
    }
}

/// <summary>
/// Browser-only interaction corrections that should not leak into the native editor. The static
/// single-threaded runtime has already shown that an async continuation from a Godot signal can be
/// stranded even though Node._Process keeps advancing. Paint Buddy's persistence actions therefore
/// start their existing Tasks from ordinary button callbacks and observe completion from _Process.
/// Native builds keep the original event path untouched.
/// </summary>
internal sealed partial class BrowserCharacterEditorRuntimeBridge : Node
{
    private enum BrowserPaintAction
    {
        None,
        Save,
        Use,
        UnsavedSave,
        UnsavedDiscard,
        UnsavedCancel,
        BootstrapNewPromptDiscard,
    }

    private CharacterEditorHost _host = null!;
    private bool _wasEditorOpen;
    private bool _footerActionsInstalled;
    private bool _unsavedActionsInstalled;
    private Button? _saveProxy;
    private Button? _useProxy;
    private Button? _exitProxy;
    private Task<CharacterEditorActionResult>? _actionTask;
    private BrowserPaintAction _actionKind;
    private Label? _status;

    public void Configure(CharacterEditorHost host)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Browser runtime bridge must be configured before entering the tree.");
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_host))
            return;

        if (!_host.IsEditorOpen)
        {
            _wasEditorOpen = false;
            return;
        }

        // Reset at the actual browser editor-open boundary instead of relying on the later
        // presentation bootstrap to notice paint visibility. That timing race is what allowed a
        // previous quarter-turn to survive into Paint Buddy in the itch build.
        if (!_wasEditorOpen)
        {
            _wasEditorOpen = true;
            _host.ResetPreviewRotationToFront();
            GD.Print("DESKTOP_BUDDY_WEB_PAINT_FRONT_RESET");
        }

        EnsureFooterActionProxies();
        EnsureUnsavedActionProxies();
        MirrorFooterActionState();
        PumpAction();

        if (_actionTask is null &&
            _host.Session.PendingAction == CharacterEditorPendingAction.NewPrompt &&
            IsUntouchedBuiltIn())
        {
            BeginAction(
                BrowserPaintAction.BootstrapNewPromptDiscard,
                _host.Session.ResolveUnsavedAsync(UnsavedDecision.Discard),
                "DESKTOP_BUDDY_WEB_NEW_CHARACTER_BOOTSTRAP_DISCARD");
        }
    }

    private void EnsureFooterActionProxies()
    {
        if (_footerActionsInstalled ||
            !GodotObject.IsInstanceValid(_host.SaveButton) ||
            !GodotObject.IsInstanceValid(_host.UseButton) ||
            !GodotObject.IsInstanceValid(_host.CloseButton) ||
            _host.SaveButton.GetParent() is not HBoxContainer actions ||
            !string.Equals(actions.Name.ToString(), "PaintPrimaryActions", StringComparison.Ordinal))
        {
            return;
        }

        _saveProxy = ReplaceWithBrowserProxy(
            _host.SaveButton,
            () => BeginAction(
                BrowserPaintAction.Save,
                _host.Session.SaveAsync(),
                "DESKTOP_BUDDY_WEB_PAINT_ACTION:save:begin"));
        _useProxy = ReplaceWithBrowserProxy(
            _host.UseButton,
            () => BeginAction(
                BrowserPaintAction.Use,
                _host.Session.UseCharacterAsync(),
                "DESKTOP_BUDDY_WEB_PAINT_ACTION:use:begin"));
        _exitProxy = ReplaceWithBrowserProxy(_host.CloseButton, RequestBrowserExit);
        _footerActionsInstalled = true;
        GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION_PROXIES_READY");
    }

    private void EnsureUnsavedActionProxies()
    {
        if (_unsavedActionsInstalled ||
            _host.FindChild("UnsavedChangesPrompt", true, false) is not PanelContainer prompt)
        {
            return;
        }

        Button? save = null;
        Button? discard = null;
        Button? cancel = null;
        foreach (Node node in prompt.FindChildren("*", nameof(Button), true, false))
        {
            if (node is not Button button || !GodotObject.IsInstanceValid(button))
                continue;
            switch (button.Text)
            {
                case "Save": save ??= button; break;
                case "Discard": discard ??= button; break;
                case "Cancel": cancel ??= button; break;
            }
        }
        if (!GodotObject.IsInstanceValid(save) ||
            !GodotObject.IsInstanceValid(discard) ||
            !GodotObject.IsInstanceValid(cancel))
        {
            return;
        }

        ReplaceWithBrowserProxy(
            save!,
            () => BeginAction(
                BrowserPaintAction.UnsavedSave,
                _host.Session.ResolveUnsavedAsync(UnsavedDecision.Save),
                "DESKTOP_BUDDY_WEB_PAINT_ACTION:unsaved-save:begin"));
        ReplaceWithBrowserProxy(
            discard!,
            () => BeginAction(
                BrowserPaintAction.UnsavedDiscard,
                _host.Session.ResolveUnsavedAsync(UnsavedDecision.Discard),
                "DESKTOP_BUDDY_WEB_PAINT_ACTION:unsaved-discard:begin"));
        ReplaceWithBrowserProxy(
            cancel!,
            () => BeginAction(
                BrowserPaintAction.UnsavedCancel,
                _host.Session.ResolveUnsavedAsync(UnsavedDecision.Cancel),
                "DESKTOP_BUDDY_WEB_PAINT_ACTION:unsaved-cancel:begin"));
        _unsavedActionsInstalled = true;
    }

    private Button ReplaceWithBrowserProxy(Button source, Action pressed)
    {
        if (source.GetParent() is not Control parent)
            throw new InvalidOperationException($"Browser action source '{source.Name}' has no Control parent.");

        int sourceIndex = source.GetIndex();
        string sourceName = source.Name.ToString();
        var proxy = new Button
        {
            Name = sourceName,
            Text = source.Text,
            TooltipText = source.TooltipText,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = source.CustomMinimumSize,
            SizeFlagsHorizontal = source.SizeFlagsHorizontal,
            SizeFlagsVertical = source.SizeFlagsVertical,
        };
        proxy.Pressed += pressed;

        source.Name = $"{sourceName}NativeBrowserHandler";
        source.Visible = false;
        source.FocusMode = Control.FocusModeEnum.None;
        source.MouseFilter = Control.MouseFilterEnum.Ignore;
        parent.AddChild(proxy);
        parent.MoveChild(proxy, sourceIndex);
        return proxy;
    }

    private void MirrorFooterActionState()
    {
        if (!_footerActionsInstalled)
            return;

        bool busy = _actionTask is not null;
        if (GodotObject.IsInstanceValid(_saveProxy))
            _saveProxy!.Disabled = busy || _host.SaveButton.Disabled;
        if (GodotObject.IsInstanceValid(_useProxy))
            _useProxy!.Disabled = busy || _host.UseButton.Disabled;
        if (GodotObject.IsInstanceValid(_exitProxy))
            _exitProxy!.Disabled = busy || _host.CloseButton.Disabled;
    }

    private void RequestBrowserExit()
    {
        if (_actionTask is not null)
            return;

        try
        {
            GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION:exit:begin");
            CharacterEditorActionResult result = _host.Session.RequestClose();
            if (result.Completed)
            {
                if (_host.IsEditorOpen)
                    _host.CloseEditorImmediately();
                GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION:exit:complete");
            }
            else if (result.NeedsUnsavedDecision)
            {
                GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION:exit:awaiting-unsaved-decision");
            }
            else
            {
                ReportFailure("exit", result.Detail ?? "Paint Buddy could not close.");
            }
        }
        catch (Exception exception)
        {
            ReportFailure("exit", exception.ToString());
        }
    }

    private void BeginAction(
        BrowserPaintAction kind,
        Task<CharacterEditorActionResult> task,
        string marker)
    {
        if (_actionTask is not null)
            return;
        _actionKind = kind;
        _actionTask = task;
        GD.Print(marker);
        PumpAction();
    }

    private void PumpAction()
    {
        if (_actionTask is null || !_actionTask.IsCompleted)
            return;

        Task<CharacterEditorActionResult> completed = _actionTask;
        BrowserPaintAction kind = _actionKind;
        _actionTask = null;
        _actionKind = BrowserPaintAction.None;

        CharacterEditorActionResult result;
        try
        {
            result = completed.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            ReportFailure(ActionName(kind), exception.ToString());
            return;
        }

        if (kind == BrowserPaintAction.UnsavedCancel)
        {
            GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION:unsaved-cancel:complete");
            return;
        }

        if (!result.Completed)
        {
            ReportFailure(ActionName(kind), result.Detail ?? $"{ActionName(kind)} did not complete.");
            return;
        }

        switch (kind)
        {
            case BrowserPaintAction.Save:
                GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION:save:complete");
                break;
            case BrowserPaintAction.Use:
                GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION:use:complete");
                if (_host.IsEditorOpen)
                    _host.CloseEditorImmediately();
                break;
            case BrowserPaintAction.UnsavedSave:
                GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION:unsaved-save:complete");
                break;
            case BrowserPaintAction.UnsavedDiscard:
                GD.Print("DESKTOP_BUDDY_WEB_PAINT_ACTION:unsaved-discard:complete");
                break;
            case BrowserPaintAction.BootstrapNewPromptDiscard:
                GD.Print("DESKTOP_BUDDY_WEB_NEW_CHARACTER_BOOTSTRAP_DISCARD_COMPLETE");
                break;
        }
    }

    private void ReportFailure(string action, string detail)
    {
        string message = $"Browser Paint Buddy {action} failed: {detail}";
        GD.PushError(message);
        _status ??= _host.FindChild("CharacterEditorStatus", true, false) as Label;
        if (GodotObject.IsInstanceValid(_status))
            _status!.Text = message;
        GD.Print($"DESKTOP_BUDDY_WEB_PAINT_ACTION:{action}:failed");
    }

    private static string ActionName(BrowserPaintAction kind) => kind switch
    {
        BrowserPaintAction.Save => "save",
        BrowserPaintAction.Use => "use",
        BrowserPaintAction.UnsavedSave => "unsaved-save",
        BrowserPaintAction.UnsavedDiscard => "unsaved-discard",
        BrowserPaintAction.UnsavedCancel => "unsaved-cancel",
        BrowserPaintAction.BootstrapNewPromptDiscard => "new-character-bootstrap-discard",
        _ => "action",
    };

    private bool IsUntouchedBuiltIn()
    {
        CharacterDocument? working = _host.Session.WorkingDocument;
        if (working is null || !string.Equals(working.DisplayName, "Built-in Buddy", StringComparison.Ordinal))
            return false;

        CharacterDocument baseline = CharacterDocument.CreateDefault(working.Id, "Built-in Buddy");
        if (!string.Equals(
                CharacterDocumentEditor.Canonical(working),
                CharacterDocumentEditor.Canonical(baseline),
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!_host.IsPaintMode)
            return true;

        foreach (var surface in _host.PaintWorkspace.Surfaces.Values)
        {
            if (surface.Pixels.Span.IndexOfAnyExcept((byte)0) >= 0)
                return false;
        }
        return true;
    }
}

/// <summary>
/// Uses the same render-frame callback that already drives CharacterEditorHost diagnostics in the
/// browser. It exists only until the host has either composed or reported a concrete failure.
/// </summary>
internal sealed partial class BrowserCharacterUiStartupPump : Node
{
    private CharacterEditorHost _host = null!;

    public void Configure(CharacterEditorHost host)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Browser startup pump must be configured before entering the tree.");
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_host))
        {
            SetProcess(false);
            QueueFree();
            return;
        }

        _host.EnsureBrowserInitialized();
        if (_host.IsInitialized)
        {
            SetProcess(false);
            QueueFree();
        }
    }
}
