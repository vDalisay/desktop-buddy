using System;
using System.Collections.Generic;

namespace DesktopBuddy.Testing;

/// <summary>Phase A scenario extension kept separate from the legacy milestone registry.</summary>
public static class PhaseACharacterScenarioCatalog
{
    private static readonly Dictionary<string, Func<IScenario>> Factories = new(StringComparer.Ordinal)
    {
        ["editor_mode_lifecycle_accounting"] = () => new EditorModeLifecycleAccountingScenario(),
        ["editor_window_restore"] = () => new EditorWindowRestoreScenario(),
        ["editor_window_monitor_removed"] = () => new EditorWindowMonitorRemovedScenario(),
        ["editor_resize_boundary_isolation"] = () => new EditorResizeBoundaryIsolationScenario(),
    };

    public static IReadOnlyCollection<string> Ids => Factories.Keys;

    public static IScenario? Find(string? id) =>
        id is not null && Factories.TryGetValue(id, out Func<IScenario>? factory)
            ? factory()
            : null;
}
