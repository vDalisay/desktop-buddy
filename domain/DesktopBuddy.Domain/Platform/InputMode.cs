namespace DesktopBuddy.Domain.Platform;

/// <summary>
/// Overlay interaction mode (`DECISIONS.md` "Input modes"). Work Mode passes
/// transparent-area pointer input to the desktop behind the window; Play Mode
/// captures the bordered sandbox so tools may target empty space.
/// </summary>
public enum InputMode
{
    Work,
    Play,
}
