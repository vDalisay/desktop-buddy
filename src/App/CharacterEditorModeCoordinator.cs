using System;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Platform;
using Godot;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.App;

public readonly record struct CharacterEditorModeSnapshot(
    WindowSettings CompactWindowSettings,
    WindowLayoutMode LayoutMode,
    int FullscreenMonitor,
    DomainInputMode InputMode);

/// <summary>
/// Owns the temporary single-window editor transition. It captures and restores every
/// mutated window flag, layout mode and interaction policy, freezes gameplay/lifecycle
/// accounting through the single pause owner, and prevents editor resizes from reaching
/// gameplay boundaries.
/// </summary>
public sealed class CharacterEditorModeCoordinator
{
    public static readonly Vector2I EditorClientSize = new(960, 720);

    private readonly DesktopWindowController _window;
    private readonly DesktopShellController _shell;
    private readonly LifecycleCoordinator _lifecycle;
    private CharacterEditorModeSnapshot? _snapshot;

    public CharacterEditorModeCoordinator(
        DesktopWindowController window,
        DesktopShellController shell,
        LifecycleCoordinator lifecycle)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    public event Action<bool>? ModeChanged;

    public bool IsActive => _snapshot.HasValue;
    public int EnterCount { get; private set; }
    public int ExitCount { get; private set; }
    public CharacterEditorModeSnapshot? CapturedSnapshot => _snapshot;

    public bool Enter()
    {
        if (IsActive)
            return false;

        WindowSettings compact = _window.CompactWindowSettings;
        _snapshot = new CharacterEditorModeSnapshot(
            compact,
            _window.LayoutMode,
            _window.FullscreenMonitor,
            _window.InputMode);
        _shell.BeginEditorBoundaryIsolation();
        _lifecycle.SetEditorMode(true);

        if (_window.LayoutMode == WindowLayoutMode.FullscreenOverlay)
            _window.TrySetLayoutMode(WindowLayoutMode.Compact, _window.FullscreenMonitor);

        Rect2I candidate = new(compact.Rect.Position, EditorClientSize);
        Rect2I recovered = _window.ResolvePlacement(candidate);
        _window.ApplyWindowSettings(compact with
        {
            Rect = recovered,
            Transparent = false,
            AlwaysOnTop = false,
            // The Win98 frame draws its own title bar; a native one would stack a second bar on top.
            Borderless = true,
            Resizable = true,
        });
        _window.SetInputMode(DomainInputMode.Play, Array.Empty<Rect2I>());
        EnterCount++;
        ModeChanged?.Invoke(true);
        return true;
    }

    public bool Exit()
    {
        if (_snapshot is not CharacterEditorModeSnapshot captured)
            return false;

        WindowSettings restoredCompact = _window.RecoverWindowSettings(
            captured.CompactWindowSettings);
        if (_window.LayoutMode != WindowLayoutMode.Compact)
            _window.TrySetLayoutMode(WindowLayoutMode.Compact, captured.FullscreenMonitor);
        _window.ApplyWindowSettings(restoredCompact);

        if (captured.LayoutMode == WindowLayoutMode.FullscreenOverlay)
        {
            _window.TrySetLayoutMode(
                WindowLayoutMode.FullscreenOverlay,
                captured.FullscreenMonitor);
        }

        _window.SetInputMode(captured.InputMode, _shell.LastWorkModeHitRegions);
        _shell.EndEditorBoundaryIsolation(_window.CurrentSettings.Rect.Size);
        _snapshot = null;
        _lifecycle.SetEditorMode(false);
        ExitCount++;
        ModeChanged?.Invoke(false);
        return true;
    }
}
