namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Declares the narrow feature-facing command registration capability separately from the legacy
/// command-bar implementation. The existing public RegisterTopLevelCommand method satisfies it.
/// </summary>
public partial class Win98CommandBarBootstrap : ITopLevelCommandRegistrar
{
}
