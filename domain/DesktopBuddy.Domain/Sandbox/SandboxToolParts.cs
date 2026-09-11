using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Sandbox;

/// <summary>
/// Tools as things in the room (owner 2026-09-12). A placed tool is an ordinary part — it saves
/// with the room, it can be dragged, roped, hinged and welded to anything else, and in Play it can
/// be picked up and used — so building a machine out of a bat needs no rules of its own.
///
/// <para>Which tool a placement is lives in its overrides rather than in a definition each, because
/// every tool is the same kind of thing to the room: a body lying there. The tool's authored world
/// form still decides its shape, weight and look.</para>
/// </summary>
public static class SandboxToolParts
{
    /// <summary>Every tool that can be put in a room, in palette order.</summary>
    public static IReadOnlyList<ToolId> All { get; } =
    [
        ToolId.BaseballBat,
        ToolId.Sword,
        ToolId.BoxingGlove,
        ToolId.PowerGrab,
        ToolId.Pistol,
        ToolId.Shotgun,
        ToolId.NerfBlaster,
        ToolId.Grenade,
        ToolId.FireSprayer,
        ToolId.Baseball,
        ToolId.SoccerBall,
        ToolId.Meal,
        ToolId.Drink,
        ToolId.RepairKit,
        ToolId.Pet,
        ToolId.Tickle,
        ToolId.RopeSuspender,
    ];

    /// <summary>The content IDs of <see cref="All"/>: what a placement actually saves.</summary>
    public static IReadOnlyList<string> ContentIdsOf { get; } = [.. All.Select(ContentIds.ForTool)];

    public static bool Contains(string? contentId) => contentId is not null && ContentIdsOf.Contains(contentId);

    /// <summary>The tool a placement holds, or null when it names none this build can place.</summary>
    public static ToolId? ToolOf(string? contentId) =>
        contentId is not null && ContentIds.TryParseTool(contentId, out ToolId tool) && All.Contains(tool)
            ? tool
            : null;
}
