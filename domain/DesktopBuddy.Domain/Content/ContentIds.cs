using System;
using DesktopBuddy.Domain.Autonomy;
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
    public const string ToolBaseball = "tool.baseball";
    public const string ToolMeal = "tool.meal";
    public const string ToolBaseballBat = "tool.baseball_bat";

    /// <summary>
    /// The starter toy gun. <see cref="ToolPistol"/> keeps its plain meaning — the real
    /// gun — because a shipped content ID is migrated, never repurposed.
    /// </summary>
    public const string ToolNerfBlaster = "tool.nerf_blaster";
    public const string ToolPistol = "tool.pistol";
    public const string ToolGrenade = "tool.grenade";
    public const string ToolFireSprayer = "tool.fire_sprayer";
    public const string ToolSoccerBall = "tool.soccer_ball";
    public const string ToolDrink = "tool.drink";
    public const string ToolShotgun = "tool.shotgun";
    public const string ToolRepairKit = "tool.repair_kit";

    /// <summary>
    /// The FR-019 passive upgrade. Deliberately <b>not</b> a <see cref="ToolId"/>: it is
    /// owned and priced like catalogue content but must never enter tool selection, so the
    /// type system — not a runtime filter alone — keeps it out of the tool vocabulary.
    /// </summary>
    public const string UpgradeStrength = "upgrade.strength";

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

    // The things a buddy can find fun. Each one carries its own interest meter and its own
    // per-buddy taste (owner instruction 2026-07-27), so these IDs are persisted per save
    // and fall under the same never-repurpose rule as every other content ID.

    /// <summary>Catching a ball the player threw, before it touches the ground.</summary>
    public const string FunCatch = "fun.catch";

    /// <summary>Being petted.</summary>
    public const string FunPet = "fun.pet";

    /// <summary>Being tickled.</summary>
    public const string FunTickle = "fun.tickle";

    /// <summary>Eating a treat.</summary>
    public const string FunTreat = "fun.treat";

    /// <summary>The total <see cref="ToolId"/> → stable ID mapping.</summary>
    public static string ForTool(ToolId tool) => tool switch
    {
        ToolId.Grab => ToolGrab,
        ToolId.Pet => ToolPet,
        ToolId.Tickle => ToolTickle,
        ToolId.BoxingGlove => ToolBoxingGlove,
        ToolId.Baseball => ToolBaseball,
        ToolId.Meal => ToolMeal,
        ToolId.BaseballBat => ToolBaseballBat,
        ToolId.NerfBlaster => ToolNerfBlaster,
        ToolId.Pistol => ToolPistol,
        ToolId.Grenade => ToolGrenade,
        ToolId.FireSprayer => ToolFireSprayer,
        ToolId.SoccerBall => ToolSoccerBall,
        ToolId.Drink => ToolDrink,
        ToolId.Shotgun => ToolShotgun,
        ToolId.RepairKit => ToolRepairKit,
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
            case ToolBaseball:
                tool = ToolId.Baseball;
                return true;
            case ToolMeal:
                tool = ToolId.Meal;
                return true;
            case ToolBaseballBat:
                tool = ToolId.BaseballBat;
                return true;
            case ToolNerfBlaster:
                tool = ToolId.NerfBlaster;
                return true;
            case ToolPistol:
                tool = ToolId.Pistol;
                return true;
            case ToolGrenade:
                tool = ToolId.Grenade;
                return true;
            case ToolFireSprayer:
                tool = ToolId.FireSprayer;
                return true;
            case ToolSoccerBall:
                tool = ToolId.SoccerBall;
                return true;
            case ToolDrink:
                tool = ToolId.Drink;
                return true;
            case ToolShotgun:
                tool = ToolId.Shotgun;
                return true;
            case ToolRepairKit:
                tool = ToolId.RepairKit;
                return true;
            default:
                tool = ToolSelection.DefaultTool;
                return false;
        }
    }

    /// <summary>True when the ID names a tool known to this build.</summary>
    public static bool IsTool(string? contentId) => TryParseTool(contentId, out _);

    /// <summary>True when this build can safely activate the persisted content ID.</summary>
    public static bool IsKnown(string? contentId) =>
        IsTool(contentId) ||
        contentId is UpgradeStrength or LooseObject or RoomBoundary or CareLabFood or
            FunCatch or FunPet or FunTickle or FunTreat;

    /// <summary>
    /// True when the ID names an entry the FR-013.2 launch catalogue can carry — every
    /// tool plus the FR-019 upgrade. Attribution-only IDs (loose objects, boundaries, fun
    /// activities) are deliberately excluded: they are never owned or sold.
    /// </summary>
    public static bool IsCatalogueEntry(string? contentId) =>
        IsTool(contentId) || contentId == UpgradeStrength;

    /// <summary>The total <see cref="FunActivityId"/> → stable ID mapping.</summary>
    public static string ForFun(FunActivityId activity) => activity switch
    {
        FunActivityId.Catch => FunCatch,
        FunActivityId.Pet => FunPet,
        FunActivityId.Tickle => FunTickle,
        FunActivityId.Treat => FunTreat,
        _ => throw new ArgumentOutOfRangeException(
            nameof(activity),
            activity,
            "Unknown fun activity: extend ContentIds.ForFun when adding a FunActivityId."),
    };

    /// <summary>The inverse mapping; <c>false</c> for anything this build cannot activate.</summary>
    public static bool TryParseFun(string? contentId, out FunActivityId activity)
    {
        switch (contentId)
        {
            case FunCatch:
                activity = FunActivityId.Catch;
                return true;
            case FunPet:
                activity = FunActivityId.Pet;
                return true;
            case FunTickle:
                activity = FunActivityId.Tickle;
                return true;
            case FunTreat:
                activity = FunActivityId.Treat;
                return true;
            default:
                activity = FunActivityId.Catch;
                return false;
        }
    }
}
