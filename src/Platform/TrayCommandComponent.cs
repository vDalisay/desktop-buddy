using System;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// The minimal Milestone 4 tray command surface: Show/Hide and Save &amp; Quit
/// (ARCHITECTURE §24). It reads only those two shell actions and raises semantic
/// requests; the composition root owns what they do, so this worker holds no
/// lifecycle or persistence state.
///
/// <para>
/// <b>Scope boundary.</b> Godot delivers no input to an invisible, unfocused window,
/// so this path can hide but cannot restore. Restoring from hidden needs the native
/// tray icon or OS-global hotkey, which the M4 plan scopes to Milestone 6 ("Full tray
/// menu (M6). M4 ships Show/Hide + Save &amp; Quit only."). Until then the shell owns
/// the commands and the hidden-mode state machine, and the native adapter will bind
/// the restore stimulus to the same seam.
/// </para>
///
/// <para>
/// <see cref="Node.ProcessMode"/> is <c>Always</c> so the commands survive the paused
/// gameplay tree that hidden mode installs.
/// </para>
/// </summary>
public partial class TrayCommandComponent : Node
{
    /// <summary>Requests the hidden-to-tray state be toggled.</summary>
    public event Action? HideShowToggled;

    /// <summary>Requests an immediate progress flush followed by a clean exit.</summary>
    public event Action? SaveAndQuitRequested;

    public int HideShowRequestCount { get; private set; }
    public int SaveAndQuitRequestCount { get; private set; }

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.ToggleHideToTray))
        {
            HideShowRequestCount++;
            GetViewport().SetInputAsHandled();
            HideShowToggled?.Invoke();
            return;
        }

        if (@event.IsActionPressed(InputActions.SaveAndQuit))
        {
            SaveAndQuitRequestCount++;
            GetViewport().SetInputAsHandled();
            SaveAndQuitRequested?.Invoke();
        }
    }

    /// <summary>Test/native seam: raise the same command the hotkey raises.</summary>
    public void RequestHideShow()
    {
        HideShowRequestCount++;
        HideShowToggled?.Invoke();
    }

    /// <summary>Test/native seam: raise the same command the hotkey raises.</summary>
    public void RequestSaveAndQuit()
    {
        SaveAndQuitRequestCount++;
        SaveAndQuitRequested?.Invoke();
    }
}
