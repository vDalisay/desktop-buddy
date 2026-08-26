using System;
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
/// Browser-only interaction corrections that should not leak into the native editor. The built-in
/// buddy is synthesized when no character has ever been selected, so its blank paint workspace is
/// technically a new unsaved document. Treating that untouched bootstrap document like user work
/// made the first + New Character click open an irrelevant save/discard prompt. When that exact
/// untouched state requests NewPrompt, discard the synthesized document automatically; the normal
/// Win98 UX then opens its name dialog on the following frame. Any actual edits keep the prompt.
/// </summary>
internal sealed partial class BrowserCharacterEditorRuntimeBridge : Node
{
    private CharacterEditorHost _host = null!;
    private bool _resolvingBootstrapNewPrompt;

    public void Configure(CharacterEditorHost host)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Browser runtime bridge must be configured before entering the tree.");
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public override void _Process(double delta)
    {
        if (_resolvingBootstrapNewPrompt || !GodotObject.IsInstanceValid(_host) ||
            !_host.IsEditorOpen || _host.Session.PendingAction != CharacterEditorPendingAction.NewPrompt ||
            !IsUntouchedBuiltIn())
        {
            return;
        }

        ResolveBootstrapNewPrompt();
    }

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

    private async void ResolveBootstrapNewPrompt()
    {
        _resolvingBootstrapNewPrompt = true;
        try
        {
            GD.Print("DESKTOP_BUDDY_WEB_NEW_CHARACTER_BOOTSTRAP_DISCARD");
            CharacterEditorActionResult result = await _host.Session.ResolveUnsavedAsync(UnsavedDecision.Discard);
            if (!result.Completed)
                GD.PushWarning(result.Detail ?? "Could not clear untouched browser bootstrap character.");
        }
        catch (Exception exception)
        {
            GD.PushError($"Browser new-character bootstrap resolution failed: {exception}");
        }
        finally
        {
            _resolvingBootstrapNewPrompt = false;
        }
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
