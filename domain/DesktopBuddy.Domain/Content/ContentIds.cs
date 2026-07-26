using System;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Content;

/// <summary>
/// The canonical stable content IDs that cross every domain and save seam
/// (ARCHITECTURE §5). These strings are <b>persisted</b>: once shipped, an ID may be
/// migrated but never silently repurposed, and the values must never be derived from
/// scene node names, enum ordinals, or hash codes.
///
/// Attribution sources (tools, loose objects, room boundaries) and care consumables
/// share one namespace so harmful-history memory, statistics, and save payloads can
/// key on a single vocabulary. <see cref="ForTool"/> is a <b>total</b> mapping over
/// <see cref="ToolId"/>: adding an enum member without extending it is a compile-time
/// safe runtime throw, not a silent fallback.
/// </summary>
public static class ContentIds
{
    public const string ToolGrab = "tool.grab";
    public const string ToolPet = "tool.pet";
    public const string ToolTickle = "tool.tickle";
    public const string ToolBoxingGlove = "tool.boxing_glove";

    /// <summary>
    /// Generic untagged physical body (scenario props, expired thrown objects). Covers
    /// post-expiry impacts until originating-throw attribution lands with the full
    /// catalogue (RAGDOLL §7.1).
    /// </summary>
    public const string LooseObject = "object.loose";

    /// <summary>Room walls, floor, and ceiling.</summary>
    public const string RoomBoundary = "boundary.room";

    /// <summary>
    /// The M4 laboratory food item that the consume/cooldown machinery ships against
    /// (owner decision 4, 2026-07-24). M5 replaces it with the catalogue Meal.
    /// </summary>
    public const string CareLabFood = "care.lab_food";

    /// <summary>The total <see cref="ToolId"/> → stable ID mapping.</summary>
    public static string ForTool(ToolId tool) => tool switch
    {
        ToolId.Grab => ToolGrab,
        ToolId.Pet => ToolPet,
        ToolId.Tickle => ToolTickle,
        ToolId.BoxingGlove => ToolBoxingGlove,
        _ => throw new ArgumentOutOfRangeException(
            nameof(tool),
            tool,
            "Unknown tool: extend ContentIds.ForTool when adding a ToolId."),
    };

    /// <summary>
    /// The inverse mapping. Returns <c>false</c> for non-tool content and for IDs from a
    /// newer build — a save carrying an unknown selected tool falls back to the default
    /// without discarding the unknown value (FR-015.1 extension rule).
    /// </summary>
    public static bool TryParseTool(string? contentId, out ToolId tool)
    {
        switch (contentId)
        {
            case ToolGrab:
                tool = ToolId.Grab;
                return true;
            case ToolPet:
                tool = ToolId.Pet;
                return true;
            case ToolTickle:
                tool = ToolId.Tickle;
                return true;
            case ToolBoxingGlove:
                tool = ToolId.BoxingGlove;
                return true;
            default:
                tool = ToolSelection.DefaultTool;
                return false;
        }
    }

    /// <summary>True when the ID names a tool known to this build.</summary>
    public static bool IsTool(string? contentId) => TryParseTool(contentId, out _);
}
