using System;

namespace DesktopBuddy.Environment;

/// <summary>
/// Narrow semantic event surface for features that react to successful environment customization
/// commits without depending on editor UI state, save-file polling, or a global event bus.
/// </summary>
public interface IEnvironmentCustomizationEvents
{
    /// <summary>
    /// Raised only after Paint Background successfully persists a genuinely changed canvas.
    /// Opening/saving an unchanged canvas, Reset Progress, and Workshop room application do not
    /// count as an authored Paint Background customization event.
    /// </summary>
    event Action? BackgroundCommitted;
}
