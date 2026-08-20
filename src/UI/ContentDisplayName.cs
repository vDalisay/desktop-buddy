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

        // Pet and Tickle ship as the Brush and the Feather (owner renames 2026-08-19). Their
        // content ids are persisted in save files and economy fixtures, so only the labels move.
        if (string.Equals(contentId, ContentIds.ToolPet, StringComparison.Ordinal))
            return "Brush";
        if (string.Equals(contentId, ContentIds.ToolTickle, StringComparison.Ordinal))
            return "Feather";

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
            "Hold left mouse on Buddy to drag him, and let go while moving to fling him.",
        ContentIds.ToolPowerGrab =>
            "The same left-mouse drag as Grab, with a far stronger pull and a much harder throw.",
        ContentIds.ToolPet =>
            "Hold left mouse and stroke slowly over Buddy — he has a favourite spot.",
        ContentIds.ToolTickle =>
            "Hold left mouse and wiggle over Buddy; keep it up too long and he turns grumpy.",
        ContentIds.ToolBaseballBat =>
            "Hold right mouse to wind up through three charge stages, then let go to swing.",
        ContentIds.ToolBoxingGlove =>
            "Swing the cursor into Buddy — the faster the glove moves, the harder it lands.",
        ContentIds.ToolBaseball =>
            "Right mouse drops a ball at the cursor; grab it with left mouse, then hold right " +
            "mouse and pull back to hurl it.",
        ContentIds.ToolSoccerBall =>
            "Right mouse drops the ball; grab it with left mouse, then hold right mouse and " +
            "pull back to boot it across the room.",
        ContentIds.ToolMeal =>
            "Right mouse drops a meal at the cursor; Buddy eats it where it lands, or grab it " +
            "and pull back with right mouse to throw it at him.",
        ContentIds.ToolDrink =>
            "Right mouse drops a drink at the cursor; Buddy takes it from there, or grab it " +
            "and pull back with right mouse to throw it.",
        ContentIds.ToolRepairKit =>
            "Right mouse drops a kit; grab it with left mouse and throw it into Buddy to patch " +
            "him back up.",
        ContentIds.ToolGrenade =>
            "Right mouse drops a grenade; grab it with left mouse, then hold right mouse and " +
            "pull back to lob it in an arc — pulling back also pulls the pin.",
        ContentIds.ToolNerfBlaster =>
            "Left mouse fires darts wherever the cursor points; press R to reload.",
        ContentIds.ToolPistol =>
            "Left mouse fires at the cursor; press R to reload when the magazine runs dry.",
        ContentIds.ToolShotgun =>
            "Left mouse fires a spread of pellets — brutal up close; press R to chamber the " +
            "next shell.",
        ContentIds.ToolFireSprayer =>
            "Hold left mouse to spray burning fuel; whatever it touches catches alight.",
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
