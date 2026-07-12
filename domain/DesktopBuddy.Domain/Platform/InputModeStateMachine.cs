namespace DesktopBuddy.Domain.Platform;

/// <summary>Stimuli that may request a Work/Play mode change.</summary>
public enum ShellInputEvent
{
    /// <summary>The player interacted with the buddy in the sandbox.</summary>
    BuddyInteraction,

    /// <summary>The player interacted with an in-window menu/HUD control.</summary>
    MenuInteraction,

    /// <summary>A tool was selected.</summary>
    ToolSelected,

    /// <summary>The global mode hotkey (default Ctrl+Shift+B) fired.</summary>
    GlobalToggle,

    /// <summary>A click landed outside the sandbox box.</summary>
    OutsideClick,

    /// <summary>Escape was pressed.</summary>
    EscapePressed,

    /// <summary>The tray "return to Work Mode" action was chosen.</summary>
    TrayReturnToWork,

    /// <summary>The window lost focus.</summary>
    FocusLost,

    /// <summary>An inactivity/idle tick — must never change the mode.</summary>
    InactivityTick,
}

/// <summary>
/// Pure Work/Play transition rules (`DECISIONS.md` "Overlay and Interface";
/// `ARCHITECTURE.md` §9). Interacting with the buddy, an in-window menu, or
/// selecting a tool requests Play Mode; the global toggle flips the mode; an
/// outside click, Escape, the tray action, or focus loss requests Work Mode.
/// Input mode never changes from inactivity alone, and a transition never
/// synthesizes primary input (a driver concern the machine keeps out of state).
/// The selected tool is deliberately not modelled here, so it cannot change
/// across a transition — that invariant is structural, not enforced per event.
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
        ShellInputEvent.MenuInteraction => InputMode.Play,
        ShellInputEvent.ToolSelected => InputMode.Play,
        ShellInputEvent.GlobalToggle => current == InputMode.Work ? InputMode.Play : InputMode.Work,
        ShellInputEvent.OutsideClick => InputMode.Work,
        ShellInputEvent.EscapePressed => InputMode.Work,
        ShellInputEvent.TrayReturnToWork => InputMode.Work,
        ShellInputEvent.FocusLost => InputMode.Work,
        ShellInputEvent.InactivityTick => current,
        _ => current,
    };
}
