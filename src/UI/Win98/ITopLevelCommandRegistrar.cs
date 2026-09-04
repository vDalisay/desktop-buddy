using System;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Feature-facing registration seam for commands that belong in the Win98 top-level strip.
/// Callers do not need to know the command bar's composition or scene-tree location.
/// </summary>
public interface ITopLevelCommandRegistrar
{
    IDisposable RegisterTopLevelCommand(
        TopLevelCommandDefinition definition,
        Action invoke,
        Func<bool>? isVisible = null,
        Func<bool>? isEnabled = null);
}
