using System;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Platform;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Binds the shell's semantic Work/Play mode to the one real pointer input owner. Work mode
/// releases every pointer-owned action and disables gameplay input processing; Play mode
/// re-enables it but waits for a fresh mouse event before tools may respawn.
/// </summary>
public partial class GameplayInputModeBridge : Node
{
    private SandboxRoot _sandbox = null!;
    private bool _configured;

    public bool GameplayInputEnabled { get; private set; }
    public int AppliedCount { get; private set; }

    public void Configure(SandboxRoot sandbox)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("GameplayInputModeBridge must be configured before _Ready.");
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _configured = true;
    }

    public override void _Ready()
    {
        if (!_configured ||
            !GodotObject.IsInstanceValid(_sandbox.Shell) ||
            !GodotObject.IsInstanceValid(_sandbox.Pointer))
        {
            throw new InvalidOperationException(
                "GameplayInputModeBridge requires a configured sandbox shell and pointer.");
        }

        _sandbox.Shell.InputModeChanged += Apply;
        Apply(_sandbox.Shell.Mode);
    }

    private void Apply(InputMode mode)
    {
        bool enabled = mode == InputMode.Play;
        if (!enabled)
        {
            // Clear before disabling input callbacks. This releases grab/tool buttons,
            // removes cursor-owned actors, and invalidates the stale cursor anchor.
            _sandbox.Pointer.NotifyPointerExitedPlayArea();
        }

        _sandbox.Pointer.SetProcessInput(enabled);
        _sandbox.Pointer.SetProcessUnhandledInput(enabled);
        GameplayInputEnabled = enabled;
        AppliedCount++;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_sandbox) &&
            GodotObject.IsInstanceValid(_sandbox.Shell))
        {
            _sandbox.Shell.InputModeChanged -= Apply;
        }
    }
}
