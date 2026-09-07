using System;

namespace DesktopBuddy.Environment;

/// <summary>
/// Narrow semantic event surface for features that react to committed room customization without
/// depending on the environment editor UI or polling its persistence files.
/// </summary>
public interface IEnvironmentCustomizationEvents
{
    event Action? BackgroundCommitted;
}
