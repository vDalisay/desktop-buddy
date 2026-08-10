using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Tools;

namespace DesktopBuddy.Domain.Content;

/// <summary>
/// The canonical stable content IDs that cross every domain and save seam
/// (ARCHITECTURE §5). These strings are <b>persisted</b>: once shipped, an ID may be
/// migrated but never silently repurposed, and the values must never be derived from
/// scene node names, enum ordinals, or hash codes.
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
    public const string ToolNerfBlaster = "tool.nerf_blaster";
    public const string ToolPistol = "tool.pistol";
    public const string ToolGrenade = "tool.grenade";
    public const string ToolFireSprayer = "tool.fire_sprayer";
    public const string ToolSoccerBall = "tool.soccer_ball";
    public const string ToolDrink = "tool.drink";
    public const string ToolShotgun = "tool.shotgun";
    public const string ToolRepairKit = "tool.repair_kit";
    public const string ToolPowerGrab = "tool.power_grab";

    public const string UpgradeStrength = "upgrade.strength";

    /// <summary>
    /// First-entry Work Mode reward. This is a permanent cosmetic ownership ID and is
    /// deliberately separate from the character feature ID used to equip/render it.
    /// </summary>
    public const string CosmeticWorkGlasses = "cosmetic.glasses.work_classic";
    public const string CosmeticHairShortSweep = "cosmetic.hair.short_sweep";
    public const string CosmeticNoseButton = "cosmetic.nose.button";
    public const string CosmeticEarsRoundTabs = "cosmetic.ears.round_tabs";
    public const string CosmeticHeadwearSoftCap = "cosmetic.headwear.soft_cap";
    public const string CosmeticTopUtilityBib = "cosmetic.tops.utility_bib";
    public const string CosmeticShoesSoftSteps = "cosmetic.shoes.soft_steps";

    public const string LooseObject = "object.loose";
    public const string RoomBoundary = "boundary.room";
    public const string CareLabFood = "care.lab_food";
    public const string FunCatch = "fun.catch";
    public const string FunPet = "fun.pet";
    public const string FunTickle = "fun.tickle";
    public const string FunTreat = "fun.treat";

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
        ToolId.PowerGrab => ToolPowerGrab,
        _ => throw new ArgumentOutOfRangeException(
            nameof(tool),
            tool,
            "Unknown tool: extend ContentIds.ForTool when adding a ToolId."),
    };

    public static bool TryParseTool(string? contentId, out ToolId tool)
    {
        switch (contentId)
        {
            case ToolGrab: tool = ToolId.Grab; return true;
            case ToolPet: tool = ToolId.Pet; return true;
            case ToolTickle: tool = ToolId.Tickle; return true;
            case ToolBoxingGlove: tool = ToolId.BoxingGlove; return true;
            case ToolBaseball: tool = ToolId.Baseball; return true;
            case ToolMeal: tool = ToolId.Meal; return true;
            case ToolBaseballBat: tool = ToolId.BaseballBat; return true;
            case ToolNerfBlaster: tool = ToolId.NerfBlaster; return true;
            case ToolPistol: tool = ToolId.Pistol; return true;
            case ToolGrenade: tool = ToolId.Grenade; return true;
            case ToolFireSprayer: tool = ToolId.FireSprayer; return true;
            case ToolSoccerBall: tool = ToolId.SoccerBall; return true;
            case ToolDrink: tool = ToolId.Drink; return true;
            case ToolShotgun: tool = ToolId.Shotgun; return true;
            case ToolRepairKit: tool = ToolId.RepairKit; return true;
            case ToolPowerGrab: tool = ToolId.PowerGrab; return true;
            default:
                tool = ToolSelection.DefaultTool;
                return false;
        }
    }

    public static bool IsTool(string? contentId) => TryParseTool(contentId, out _);

    public static bool IsCosmetic(string? contentId) =>
        contentId is not null && contentId.StartsWith("cosmetic.", StringComparison.Ordinal);

    public static bool IsKnown(string? contentId) =>
        IsTool(contentId) || IsCosmetic(contentId) ||
        contentId is UpgradeStrength or LooseObject or RoomBoundary or CareLabFood or
            FunCatch or FunPet or FunTickle or FunTreat;

    public static bool IsCatalogueEntry(string? contentId) =>
        IsTool(contentId) || IsCosmetic(contentId) || contentId == UpgradeStrength;

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

    public static bool TryParseFun(string? contentId, out FunActivityId activity)
    {
        switch (contentId)
        {
            case FunCatch: activity = FunActivityId.Catch; return true;
            case FunPet: activity = FunActivityId.Pet; return true;
            case FunTickle: activity = FunActivityId.Tickle; return true;
            case FunTreat: activity = FunActivityId.Treat; return true;
            default:
                activity = FunActivityId.Catch;
                return false;
        }
    }
}
