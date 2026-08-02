namespace DesktopBuddy.Domain.Platform;

/// <summary>Stimuli that may request a Work/Play interaction-mode change.</summary>
public enum ShellInputEvent
{
    /// <summary>A primary click landed on the compact gameplay canvas.</summary>
    BuddyInteraction,

    /// <summary>The player interacted with an in-window menu/HUD control.</summary>
    MenuInteraction,

    /// <summary>A tool was selected. Selection alone never enables gameplay input.</summary>
    ToolSelected,

    /// <summary>The toolbar/global mode toggle fired.</summary>
    GlobalToggle,

    /// <summary>A click landed outside the gameplay canvas.</summary>
    OutsideClick,

    /// <summary>Escape requested the safe Work-mode recovery state.</summary>
    EscapePressed,

    /// <summary>The tray "return to Work Mode" action was chosen.</summary>
    TrayReturnToWork,

    /// <summary>The window lost focus. Focus alone does not change interaction mode.</summary>
    FocusLost,

    /// <summary>An inactivity/idle tick — must never change the mode.</summary>
    InactivityTick,
}

/// <summary>
/// Pure interaction-mode transition rules. A gameplay-canvas click may enter Play,
/// while toolbar/global toggle explicitly alternates modes. Menu use, tool selection,
/// outside clicks, focus changes, and inactivity preserve the current mode. Escape and
/// tray recovery always return to Work. Window footprint and native passthrough are owned
/// by a separate layout policy.
/// </summary>
public sealed class InputModeStateMachine
{
    public InputMode Current { get; private set; }

    public InputModeStateMachine(InputMode initial = InputMode.Work)
    {
        Current = initial;
    }

    /// <summary>Apply an event; returns true when the mode actually changed.</summary>
    public bool Apply(ShellInputEvent input)
    {
        InputMode next = Next(Current, input);
        bool changed = next != Current;
        Current = next;
        return changed;
    }

    /// <summary>Stateless transition function for exhaustive testing.</summary>
    public static InputMode Next(InputMode current, ShellInputEvent input) => input switch
    {
        ShellInputEvent.BuddyInteraction => InputMode.Play,
        ShellInputEvent.GlobalToggle => current == InputMode.Work ? InputMode.Play : InputMode.Work,
        ShellInputEvent.EscapePressed => InputMode.Work,
        ShellInputEvent.TrayReturnToWork => InputMode.Work,
        ShellInputEvent.MenuInteraction => current,
        ShellInputEvent.ToolSelected => current,
        ShellInputEvent.OutsideClick => current,
        ShellInputEvent.FocusLost => current,
        ShellInputEvent.InactivityTick => current,
        _ => current,
    };
}
