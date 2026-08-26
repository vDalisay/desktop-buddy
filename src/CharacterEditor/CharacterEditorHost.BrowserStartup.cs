using System;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Browser-only startup bridge for the experimental itch.io Web build. The custom single-threaded
/// runtime has proven reliable for CallDeferred but not for Timer signal delivery during early
/// composition, so browser UI initialization retries through deferred calls instead of depending
/// on the native-oriented timer path in CharacterEditorHost._Ready(). Native builds never enter
/// this path.
/// </summary>
public partial class CharacterEditorHost
{
    private const int BrowserInitializationMaxDeferredAttempts = 120;

    public void EnsureBrowserInitialized()
    {
        if (!OperatingSystem.IsBrowser() || IsInitialized)
            return;

        // _Ready may already have armed the browser Timer before the character coordinator was
        // published. Disable that competing path: deferred calls are known to execute correctly
        // in the Web runtime and keep the entire initialization sequence on the Godot main loop.
        StopBrowserInitializationTimer();

        if (_context.Characters is null || _selectionRuntime.Coordinator is null)
        {
            _browserInitializationAttempts++;
            if (_browserInitializationAttempts >= BrowserInitializationMaxDeferredAttempts)
            {
                GD.PushError(
                    "Character editor could not initialize its character services within " +
                    $"{BrowserInitializationMaxDeferredAttempts} deferred browser frames.");
                return;
            }

            Callable.From(EnsureBrowserInitialized).CallDeferred();
            return;
        }

        try
        {
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
            GD.Print("DESKTOP_BUDDY_WEB_CHARACTER_UI_READY");
        }
        catch (Exception exception)
        {
            GD.PushError($"Character editor browser initialization failed: {exception}");
        }
    }
}
