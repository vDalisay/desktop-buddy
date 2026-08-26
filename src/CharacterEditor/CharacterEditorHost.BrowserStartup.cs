using System;
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

            BuildPreview();
            _mode = new CharacterEditorModeCoordinator(
                _sandbox.Window,
                _sandbox.Shell,
                _sandbox.Lifecycle);
            var library = new CharacterLibraryIndex(
                new CharacterFileSystem(),
                _context.Characters.Paths.Root);
            _session = new CharacterEditorSession(
                _context.Characters,
                library,
                _selectionRuntime.Coordinator,
                _preview,
                economy: _context.Economy);
            _session.Changed += RefreshAll;
            _session.LibraryChanged += RefreshLibrary;
            _session.CloseResolved += closed =>
            {
                if (closed)
                    CloseEditorImmediately();
            };

            BuildUi();
            IsInitialized = true;
            RefreshAll();
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
