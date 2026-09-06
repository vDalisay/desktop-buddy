using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Achievements;

/// <summary>
/// Stable achievement metadata. Local IDs are persisted in progress.json and Steam API names are
/// the identifiers to create verbatim in Steamworks. Neither may be silently repurposed after ship.
/// </summary>
public readonly record struct AchievementDefinition(
    string Id,
    string SteamApiName,
    string DisplayName,
    string Description,
    bool Hidden = false);

public static class AchievementIds
{
    public const string FirstImpression = "achievement.first_impression";
    public const string LightsOut = "achievement.lights_out";
    public const string RetailTherapy = "achievement.retail_therapy";
    public const string FullToybox = "achievement.full_toybox";
    public const string BestFriends = "achievement.best_friends";
    public const string Forgiven = "achievement.forgiven";
    public const string NiceCatch = "achievement.nice_catch";
    public const string VarietyHour = "achievement.variety_hour";
    public const string FireDrill = "achievement.fire_drill";
    public const string DesktopShift = "achievement.desktop_shift";
    public const string AirBud = "achievement.air_bud";
    public const string BankShot = "achievement.bank_shot";
    public const string CharacterArc = "achievement.character_arc";
    public const string EmployeeDay = "achievement.employee_day";
    public const string EmployeeWeek = "achievement.employee_week";
    public const string EmployeeMonth = "achievement.employee_month";
    public const string EmployeeYear = "achievement.employee_year";
    public const string EmployeeForLife = "achievement.employee_for_life";
    public const string MakeItYours = "achievement.make_it_yours";
    public const string PunchingBag = "achievement.punching_bag";
    public const string FullyDressed = "achievement.fully_dressed";
    public const string HomeSweetHome = "achievement.home_sweet_home";
    public const string TryEverythingOnce = "achievement.try_everything_once";
    public const string RubeGoldberg = "achievement.rube_goldberg";
}

public static class AchievementCatalog
{
    public const int BaselineCount = 24;

    public static IReadOnlyList<AchievementDefinition> Baseline { get; } =
    [
        new(AchievementIds.FirstImpression, "ACH_FIRST_IMPRESSION", "First Impression", "Earn money from hurting Buddy for the first time."),
        new(AchievementIds.LightsOut, "ACH_LIGHTS_OUT", "Lights Out", "Knock Buddy unconscious for the first time."),
        new(AchievementIds.RetailTherapy, "ACH_RETAIL_THERAPY", "Retail Therapy", "Buy your first tool or care item."),
        new(AchievementIds.FullToybox, "ACH_FULL_TOYBOX", "Full Toybox", "Own every launch tool and care item."),
        new(AchievementIds.BestFriends, "ACH_BEST_FRIENDS", "Best Friends", "Reach +100 mood."),
        new(AchievementIds.Forgiven, "ACH_FORGIVEN", "Forgiven", "Regain enough trust for Buddy to clear harmful history."),
        new(AchievementIds.NiceCatch, "ACH_NICE_CATCH", "Nice Catch", "Successfully catch Buddy 25 times."),
        new(AchievementIds.VarietyHour, "ACH_VARIETY_HOUR", "Variety Hour", "Use every launch interaction at least once."),
        new(AchievementIds.FireDrill, "ACH_FIRE_DRILL", "Fire Drill", "Put out a burning Buddy with the Repair Kit.", Hidden: true),
        new(AchievementIds.DesktopShift, "ACH_DESKTOP_SHIFT", "Desktop Shift", "Accumulate 2 hours of running time."),
        new(AchievementIds.AirBud, "ACH_AIR_BUD", "Air Bud", "Keep Buddy airborne for 30 seconds without actively grabbing them.", Hidden: true),
        new(AchievementIds.BankShot, "ACH_BANK_SHOT", "Bank Shot", "Hit Buddy with a baseball after it ricochets off the room.", Hidden: true),
        new(AchievementIds.CharacterArc, "ACH_CHARACTER_ARC", "Character Arc", "Reach -100 mood and later +100 with the same Buddy.", Hidden: true),
        new(AchievementIds.EmployeeDay, "ACH_EMPLOYEE_DAY", "Employee of the Day", "Reach 100 lifetime Work Mode actions."),
        new(AchievementIds.EmployeeWeek, "ACH_EMPLOYEE_WEEK", "Employee of the Week", "Reach 1,000 lifetime Work Mode actions."),
        new(AchievementIds.EmployeeMonth, "ACH_EMPLOYEE_MONTH", "Employee of the Month", "Reach 10,000 lifetime Work Mode actions."),
        new(AchievementIds.EmployeeYear, "ACH_EMPLOYEE_YEAR", "Employee of the Year", "Reach 100,000 lifetime Work Mode actions."),
        new(AchievementIds.EmployeeForLife, "ACH_EMPLOYEE_FOR_LIFE", "Employee for Life", "Reach 1,000,000 lifetime Work Mode actions."),
        new(AchievementIds.MakeItYours, "ACH_MAKE_IT_YOURS", "Make It Yours", "Customize the same Buddy in Buddy Studio, Paint Buddy, Paint Background, and the Environment Decorator."),
        new(AchievementIds.PunchingBag, "ACH_PUNCHING_BAG", "Punching Bag", "Land 100 successful Boxing Glove hits."),
        new(AchievementIds.FullyDressed, "ACH_FULLY_DRESSED", "Fully Dressed", "Equip headwear, a top, shoes, and glasses at the same time."),
        new(AchievementIds.HomeSweetHome, "ACH_HOME_SWEET_HOME", "Home Sweet Home", "Place and save at least one decoration from every Environment Decorator category."),
        new(AchievementIds.TryEverythingOnce, "ACH_TRY_EVERYTHING_ONCE", "Try Everything Once", "Cause damage with every damaging launch tool at least once."),
        new(AchievementIds.RubeGoldberg, "ACH_RUBE_GOLDBERG", "Rube Goldberg Would Be Proud", "Damage Buddy with three different sources within five seconds.", Hidden: true),
    ];

    /// <summary>
    /// Launch tools that can directly print positive pain. Care, grab-only, suspension and Repair
    /// Kit interactions are intentionally excluded from the damage-completion achievement.
    /// </summary>
    public static IReadOnlyList<string> DamagingLaunchContentIds { get; } =
    [
        ContentIds.ToolBoxingGlove,
        ContentIds.ToolBaseball,
        ContentIds.ToolBaseballBat,
        ContentIds.ToolNerfBlaster,
        ContentIds.ToolPistol,
        ContentIds.ToolSoccerBall,
        ContentIds.ToolGrenade,
        ContentIds.ToolShotgun,
        ContentIds.ToolFireSprayer,
        ContentIds.ToolSword,
    ];

    public static AchievementDefinition Get(string id)
    {
        foreach (AchievementDefinition definition in Baseline)
            if (string.Equals(definition.Id, id, StringComparison.Ordinal))
                return definition;
        throw new KeyNotFoundException($"Unknown achievement '{id}'.");
    }

    public static IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Baseline.Count != BaselineCount)
            errors.Add($"Baseline must contain exactly {BaselineCount} achievements; found {Baseline.Count}.");
        if (Baseline.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.SteamApiName)))
            errors.Add("Achievement IDs and Steam API names cannot be blank.");
        if (Baseline.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != Baseline.Count)
            errors.Add("Achievement local IDs must be unique.");
        if (Baseline.Select(item => item.SteamApiName).Distinct(StringComparer.Ordinal).Count() != Baseline.Count)
            errors.Add("Achievement Steam API names must be unique.");
        return errors;
    }
}
