namespace DesktopBuddy.Platform;

public partial class DesktopWindowController
{
    /// <summary>
    /// The exact compact window state retained while full-screen is active. Temporary modes
    /// such as the character editor use this to restore the compact state before returning to
    /// the prior full-screen layout.
    /// </summary>
    public WindowSettings CompactWindowSettings => _compactSettings;
}
