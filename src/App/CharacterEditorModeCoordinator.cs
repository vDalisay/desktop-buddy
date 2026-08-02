using System;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Platform;
using Godot;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.App;

public readonly record struct CharacterEditorModeSnapshot(
    WindowSettings WindowSettings,
    DomainInputMode InputMode);

/// <summary>
/// Owns the temporary single-window editor transition. It captures and restores every
/// mutated window flag, freezes gameplay/lifecycle accounting through the single pause
/// owner, and prevents editor resizes from reaching gameplay boundaries.
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

        WindowSettings captured = _window.CaptureWindowSettings();
        _snapshot = new CharacterEditorModeSnapshot(captured, _window.InputMode);
        _shell.BeginEditorBoundaryIsolation();
        _lifecycle.SetEditorMode(true);

        Rect2I candidate = new(captured.Rect.Position, EditorClientSize);
        Rect2I recovered = _window.ResolvePlacement(candidate);
        _window.ApplyWindowSettings(captured with
        {
            Rect = recovered,
            Transparent = false,
            AlwaysOnTop = false,
            Borderless = false,
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

        WindowSettings restored = _window.RecoverWindowSettings(captured.WindowSettings);
        _window.ApplyWindowSettings(restored);
        _window.SetInputMode(captured.InputMode, _shell.LastWorkModeHitRegions);
        _shell.EndEditorBoundaryIsolation(restored.Rect.Size);
        _snapshot = null;
        _lifecycle.SetEditorMode(false);
        ExitCount++;
        ModeChanged?.Invoke(false);
        return true;
    }
}
