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

    /// <summary>Milli-credits as the player sees them: 7000 → "$7".</summary>
    public static string Credits(long milliCredits) =>
        "$" + (milliCredits / (double)RewardLedger.MilliCreditsPerCredit)
            .ToString("0.#", CultureInfo.InvariantCulture);
}
