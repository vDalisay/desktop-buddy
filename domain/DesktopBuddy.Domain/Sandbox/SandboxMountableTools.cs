using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Sandbox;

/// <summary>
/// The tools a Tool Mount can hold (owner 2026-09-12: one mount, any tool, rather than a part per
/// gun). A mount uses its tool the way the tool works: a gun fires, a swung tool swings. Tools that
/// only mean something in a player's hand — carrying, feeding, painting — are not offered, because a
/// mount holding one would sit there doing nothing when a pulse came in.
/// </summary>
public static class SandboxMountableTools
{
    /// <summary>The guns a mount fires, in palette order.</summary>
    public static IReadOnlyList<ToolId> Guns { get; } = [ToolId.NerfBlaster, ToolId.Pistol, ToolId.Shotgun];

    /// <summary>The tools a mount swings on its arm, in palette order.</summary>
    public static IReadOnlyList<ToolId> Swung { get; } = [ToolId.BoxingGlove, ToolId.BaseballBat, ToolId.Sword];

    /// <summary>Everything a mount can hold, guns first.</summary>
    public static IReadOnlyList<ToolId> All { get; } = [.. Guns, .. Swung];

    /// <summary>The content IDs of <see cref="All"/>, which is what a placement saves.</summary>
    public static IReadOnlyList<string> ContentIdsOf { get; } = [.. All.Select(ContentIds.ForTool)];

    public static bool Contains(string contentId) => ContentIdsOf.Contains(contentId);

    /// <summary>
    /// The tool a mount holds. A mount that has never been told holds the first of the list, so a
    /// freshly placed one does something the first time a pulse reaches it; only a tool this build
    /// cannot mount comes back as null.
    /// </summary>
    public static ToolId? ToolOf(string? contentId)
    {
        if (contentId is null)
            return All[0];
        return ContentIds.TryParseTool(contentId, out ToolId tool) && All.Contains(tool) ? tool : null;
    }

    public static bool IsGun(ToolId tool) => Guns.Contains(tool);
}
