namespace DesktopBuddy.Laboratory;

/// <summary>
/// The single-key developer controls — time scale on the number row, tool selection and
/// object spawning on letters — are off in the shipped game. They were never meant to reach a
/// player, and they did: pressing 5 or 6 spawned objects mid-session (owner report 2026-08-21).
///
/// <para>The scenario and journey harnesses drive tools and spawns through these very keys, so
/// the runner switches them on for its own process. Real input paths — the buddy_reload action,
/// mouse buttons, the wheel — are unaffected; only the debug keyboard shortcuts are gated.</para>
/// </summary>
public static class LabDevKeys
{
    public static bool Enabled { get; set; }
}
