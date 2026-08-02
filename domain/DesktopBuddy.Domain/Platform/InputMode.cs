namespace DesktopBuddy.Domain.Platform;

/// <summary>
/// Who owns gameplay pointer input. Work Mode leaves gameplay controls inactive;
/// Play Mode routes pointer actions to the buddy and selected tools. Native desktop
/// passthrough is a separate window-layout policy and is not implied by this enum.
/// </summary>
public enum InputMode
{
    Work,
    Play,
}
