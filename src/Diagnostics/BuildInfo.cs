using Godot;

namespace DesktopBuddy.Diagnostics;

/// <summary>
/// Build-time capability guards. Development-only surfaces (the automation
/// driver, laboratory controls, debug telemetry, tuning panels) must be gated
/// behind these so release exports contain no such code paths
/// (AGENTS.md "Implementation Discipline", AGENT_VERIFICATION_AND_E2E.md Section 2).
/// </summary>
public static class BuildInfo
{
    /// <summary>True in editor/debug builds; false in release/exported templates.</summary>
    public static bool IsDebugBuild => OS.IsDebugBuild();

    /// <summary>
    /// Whether development automation (AutomationDriver, scenario/journey
    /// runners) may be composed at all. Release exports return false regardless
    /// of command-line flags, so no automation code can run in a shipped build.
    /// </summary>
    public static bool AutomationAllowed => IsDebugBuild;
}
