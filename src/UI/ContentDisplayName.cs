using System;
using System.Globalization;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;

namespace DesktopBuddy.Ui;

/// <summary>
/// Player-facing text for catalogue content and money.
///
/// ponytail: names are derived from the content id ("tool.baseball_bat" → "Baseball Bat"),
/// because the catalogue's authored NameKey points at a string table that does not exist
/// yet. Resolve through that table instead once localisation lands (M7).
/// </summary>
public static class ContentDisplayName
{
    public static string For(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return string.Empty;

        // Pet and Tickle ship as the Brush and the Feather (owner renames 2026-08-19), the
        // Fire Sprayer as the Flamethrower and the Nerf Blaster as the Toy Gun (2026-08-22).
        // Their content ids are persisted in save files and economy fixtures, so only the
        // labels move.
        if (string.Equals(contentId, ContentIds.ToolPet, StringComparison.Ordinal))
            return "Brush";
        if (string.Equals(contentId, ContentIds.ToolTickle, StringComparison.Ordinal))
            return "Feather";
        if (string.Equals(contentId, ContentIds.ToolFireSprayer, StringComparison.Ordinal))
            return "Flamethrower";
        if (string.Equals(contentId, ContentIds.ToolNerfBlaster, StringComparison.Ordinal))
            return "Toy Gun";

        int lastDot = contentId.LastIndexOf('.');
        string slug = lastDot >= 0 && lastDot < contentId.Length - 1
            ? contentId[(lastDot + 1)..]
            : contentId;
        string[] words = slug.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < words.Length; index++)
            words[index] = char.ToUpperInvariant(words[index][0]) + words[index][1..];

        return string.Join(' ', words);
    }

    /// <summary>
    /// One sentence on how the tool is actually driven, for the Inventory and Tools rows.
    /// Kept here beside the names rather than in the catalogue resources for the same reason
    /// the names are: the authored DescriptionKey points at a string table that does not exist
    /// until localisation lands (M7). Move both at once.
    /// </summary>
    public static string Usage(string contentId) => contentId switch
    {
        ContentIds.ToolGrab =>
            "Click and hold Buddy with the left mouse button to drag it. Release while moving " +
            "to fling it.",
        ContentIds.ToolPowerGrab =>
            "Drag Buddy with the left mouse button, just like Grab, but with much more pull and " +
            "a harder throw.",
        ContentIds.ToolPet =>
            "Click and hold the left mouse button, then brush your Buddy slowly. It should have " +
            "a favourite spot...",
        ContentIds.ToolTickle =>
            "Click and hold the left mouse button to wiggle the Feather over Buddy. Keep going " +
            "too long and it will get grumpy.",
        ContentIds.ToolBaseballBat =>
            "Hold the right mouse button to charge it, then release to swing.",
        ContentIds.ToolBoxingGlove =>
            "Swing the glove into your Buddy, or hold the right mouse button to wind it back " +
            "and let go to throw a punch.",
        ContentIds.ToolBaseball =>
            "Right-click to drop a baseball. Grab it with the left mouse button, then hold the " +
            "right mouse button and pull back to throw.",
        ContentIds.ToolSoccerBall =>
            "Right-click to drop the ball. Grab it with the left mouse button, then hold the " +
            "right mouse button and pull back to kick it across the room.",
        ContentIds.ToolMeal =>
            "Right-click to drop a treat, though I'm not sure what it's made of...",
        ContentIds.ToolDrink =>
            "Right-click to drop a drink. It also makes for a good projectile.",
        ContentIds.ToolRepairKit =>
            "Right-click to drop a kit, then grab it with the left mouse button and throw it at " +
            "your Buddy to patch it up.",
        ContentIds.ToolGrenade =>
            "Right-click to drop a grenade. Grab it with the left mouse button, then hold the " +
            "right mouse button and pull back to lob it. Pulling back also pulls the pin.",
        ContentIds.ToolNerfBlaster =>
            "Left-click to have some friendly fire.",
        ContentIds.ToolPistol =>
            "Left-click to have some less friendly fire.",
        ContentIds.ToolShotgun =>
            "Left-click to fire a spread of pellets with some hefty knockback.",
        ContentIds.ToolRopeSuspender =>
            "Grab something using left-click and use the right mouse button to suspend it at that location.",
        ContentIds.ToolFireSprayer =>
            "Hold the left mouse button to spray burning fuel. Anything it touches catches fire.",
        _ => string.Empty,
    };

    /// <summary>
    /// Milli-credits as the player sees them: 7000 → "$7". Whole credits only, floored the same
    /// way <see cref="RewardLedger.BalanceCredits"/> floors — the shell's corner readout and every
    /// panel that quotes a balance must never disagree about how much money you have. Prices are
    /// validated to be whole credits, so nothing is lost rounding them.
    /// </summary>
    public static string Credits(long milliCredits) =>
        "$" + (milliCredits / RewardLedger.MilliCreditsPerCredit)
            .ToString(CultureInfo.InvariantCulture);
}
