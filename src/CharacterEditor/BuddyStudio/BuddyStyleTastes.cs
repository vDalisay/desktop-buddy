using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>
/// What the buddy happens to be fond of this visit. A fresh handful of styles is drawn every
/// time the Studio opens, so dressing him is a small guessing game rather than a fixed answer
/// to look up once (owner instruction 2026-08-22): he shows what he likes by reacting to it,
/// and pays a bonus for each liked style still on him when the Studio closes.
///
/// <para>Nothing here is persisted. A taste lasts exactly one visit, and none of it touches
/// pain, mood, physics or ownership — a liked style is still bought like any other.</para>
/// </summary>
public sealed class BuddyStyleTastes
{
    /// <summary>The bonus one liked style is worth on the way out.</summary>
    public const long CreditsPerLikedStyle = 25_000;

    private readonly HashSet<string> _liked;

    private BuddyStyleTastes(HashSet<string> liked) => _liked = liked;

    /// <summary>An empty set, for a Studio that has not been opened yet.</summary>
    public static BuddyStyleTastes None { get; } = new([]);

    public IReadOnlyCollection<string> LikedIds => _liked;

    public bool Likes(string cosmeticId) =>
        !string.IsNullOrEmpty(cosmeticId) && _liked.Contains(cosmeticId);

    /// <summary>
    /// Draws one liked style from each of <paramref name="categories"/> distinct categories.
    /// Free defaults are excluded: liking the thing he is already wearing by default would pay
    /// a bonus for changing nothing.
    /// </summary>
    public static BuddyStyleTastes Roll(
        CharacterFeatureCatalog catalogue,
        ulong seed,
        int categories = 3)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var random = new XorShift(seed == 0 ? 0x5DEECE66DUL : seed);
        CharacterFeatureSlot[] slots = Enum.GetValues<CharacterFeatureSlot>()
            .Distinct()
            .Where(slot => catalogue.GetDefinitions(slot).Any(definition => !definition.IsFreeDefault))
            .ToArray();

        var liked = new HashSet<string>(StringComparer.Ordinal);
        int wanted = Math.Clamp(categories, 0, slots.Length);
        for (int picked = 0; picked < wanted && slots.Length > 0; picked++)
        {
            // Shuffle-free selection: swap the drawn slot to the end of the live range so the
            // same category cannot be drawn twice.
            int liveCount = slots.Length - picked;
            int index = random.Next(liveCount);
            (slots[index], slots[liveCount - 1]) = (slots[liveCount - 1], slots[index]);
            CosmeticDefinition[] choices = catalogue.GetDefinitions(slots[liveCount - 1])
                .Where(definition => !definition.IsFreeDefault)
                .ToArray();
            if (choices.Length > 0)
                liked.Add(choices[random.Next(choices.Length)].Id);
        }

        return new BuddyStyleTastes(liked);
    }

    /// <summary>Liked styles the document is actually wearing right now.</summary>
    public int WornCount(CharacterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int worn = 0;
        foreach (CharacterFeatureSlot slot in Enum.GetValues<CharacterFeatureSlot>().Distinct())
            if (Likes(CharacterDocumentEditor.ReadFeatureId(document, slot)))
                worn++;
        return worn;
    }

    private struct XorShift(ulong seed)
    {
        private ulong _state = seed;

        public int Next(int exclusiveMaximum)
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return exclusiveMaximum <= 0 ? 0 : (int)(_state % (uint)exclusiveMaximum);
        }
    }
}
