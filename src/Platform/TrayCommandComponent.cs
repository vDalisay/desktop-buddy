using System;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// The minimal tray command surface: Show/Hide, Save &amp; Quit (ARCHITECTURE §24), and —
/// since M5 Task 13 — the armed Reset Progress request. It reads the shell actions and
/// raises semantic requests; the composition root owns what they do, so this worker holds
/// no lifecycle or persistence state.
///
/// <para>
/// <b>Reset is two affirmative actions, with Cancel as the default.</b> Arming
/// (<see cref="RequestResetProgress"/>) mutates nothing; only
/// <see cref="ConfirmResetProgress"/> inside <see cref="ResetArmingWindowSeconds"/> raises
/// the event the root turns into a reset. That contract lives here, rather than in a dialog,
/// so it is testable in a build that has no dock to put a dialog in.
/// </para>
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

    /// <summary>
    /// A reset was <b>armed</b> and is waiting for a second affirmative action. The dock
    /// modal (UI_FLOATING_DOCK_PLAN Task 7) binds here; nothing has been mutated yet.
    /// </summary>
    public event Action? ResetProgressRequested;

    /// <summary>The armed reset was confirmed in time: perform it.</summary>
    public event Action? ResetProgressConfirmed;

    /// <summary>
    /// How long an armed reset stays armed. Cancel is the default: the window lapsing, an
    /// explicit cancel, or any other tray command all disarm it and mutate nothing.
    /// </summary>
    public const double ResetArmingWindowSeconds = 30.0;

    private ulong _resetArmedAtMsec;
    private bool _resetArmed;

    public int HideShowRequestCount { get; private set; }
    public int SaveAndQuitRequestCount { get; private set; }
    public int ResetProgressRequestCount { get; private set; }
    public int ResetProgressConfirmCount { get; private set; }

    /// <summary>True while a reset is armed and still inside its window.</summary>
    public bool ResetIsArmed => _resetArmed && !ArmingWindowLapsed;

    private bool ArmingWindowLapsed =>
        (Time.GetTicksMsec() - _resetArmedAtMsec) > (ulong)(ResetArmingWindowSeconds * 1000.0);

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
        // Any other command is an answer of "no": an armed reset never survives one.
        CancelResetProgress();
        HideShowToggled?.Invoke();
    }

    /// <summary>Test/native seam: raise the same command the hotkey raises.</summary>
    public void RequestSaveAndQuit()
    {
        SaveAndQuitRequestCount++;
        CancelResetProgress();
        SaveAndQuitRequested?.Invoke();
    }

    /// <summary>
    /// Arms a progress reset and asks for confirmation. Mutates nothing by itself: only
    /// <see cref="ConfirmResetProgress"/> inside the arming window performs the reset, so the
    /// two-step "Cancel is the default" contract holds with or without a dialog.
    /// </summary>
    public void RequestResetProgress()
    {
        ResetProgressRequestCount++;
        _resetArmed = true;
        _resetArmedAtMsec = Time.GetTicksMsec();
        ResetProgressRequested?.Invoke();
    }

    /// <summary>
    /// The second affirmative action. Returns <c>false</c> — raising nothing — when no reset
    /// is armed or the arming window has lapsed.
    /// </summary>
    public bool ConfirmResetProgress()
    {
        if (!ResetIsArmed)
        {
            _resetArmed = false;
            return false;
        }

        _resetArmed = false;
        ResetProgressConfirmCount++;
        ResetProgressConfirmed?.Invoke();
        return true;
    }

    /// <summary>Disarms an armed reset. Safe to call when nothing is armed.</summary>
    public void CancelResetProgress() => _resetArmed = false;
}
