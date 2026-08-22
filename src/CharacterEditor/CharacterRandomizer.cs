using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Deterministic Studio randomization across every category the catalogue carries. It reads the
/// categories and their styles from the catalogue itself, so a style added to the catalogue is
/// rollable the moment it ships without this class being touched. Eligibility comes only from
/// trusted free definitions and the caller's existing permanent cosmetic ownership set, so a
/// roll can never equip something the player has not bought.
/// </summary>
public static class CharacterRandomizer
{
    private static readonly Rgba32[] SafeColors =
    [
        Rgba32.Parse("#183042"),
        Rgba32.Parse("#386A8C"),
        Rgba32.Parse("#6A4937"),
        Rgba32.Parse("#C95B63"),
        Rgba32.Parse("#E3A33A"),
        Rgba32.Parse("#74B9E8"),
        Rgba32.Parse("#8A6BC4"),
        Rgba32.Parse("#5A6575"),
    ];

    public static CharacterDocument Randomize(CharacterDocument document, ulong seed) =>
        Randomize(
            document,
            CharacterFeatureCatalog.Shipped,
            new HashSet<string>(StringComparer.Ordinal),
            seed);

    public static CharacterDocument Randomize(
        CharacterDocument document,
        CharacterFeatureCatalog catalogue,
        IReadOnlySet<string> ownedCosmeticIds,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(ownedCosmeticIds);
        var random = new XorShift64(seed == 0 ? 0x9E3779B97F4A7C15UL : seed);
        CharacterDocument result = document;

        foreach (CharacterPartSlot part in Enum.GetValues<CharacterPartSlot>())
            result = CharacterDocumentEditor.SetPartColor(result, part, NextColor(ref random));

        foreach (CharacterFeatureSlot slot in Enum.GetValues<CharacterFeatureSlot>().Distinct())
        {
            CosmeticDefinition[] eligible = catalogue.GetDefinitions(slot)
                .Where(definition => definition.IsFreeDefault ||
                    (definition.OwnershipContentId is string contentId &&
                     ownedCosmeticIds.Contains(contentId)))
                .ToArray();
            if (eligible.Length == 0)
                throw new InvalidOperationException($"No owned or free cosmetic definition exists for {slot}.");

            // Style only. Position and size stay where the style was authored to sit: a rolled
            // offset and scale made half the results look broken rather than different, and the
            // player can move and resize whatever the roll gave them (owner instruction
            // 2026-08-22).
            CosmeticDefinition selected = eligible[random.NextInt(eligible.Length)];
            NormalizedFeatureTransform transform = selected.DefaultTransform;
            var colors = selected.ColorChannels.ToDictionary(
                channel => channel.Id,
                _ => NextColor(ref random),
                StringComparer.Ordinal);
            Rgba32 legacyColor = colors.TryGetValue(CosmeticDefinition.PrimaryColorChannel, out Rgba32 primary)
                ? primary
                : CharacterDocumentEditor.ReadFeatureColor(result, slot);
            result = CharacterDocumentEditor.SetFeatureDocument(
                result,
                slot,
                new CharacterFeatureDocument
                {
                    FeatureId = selected.Id,
                    OffsetX = transform.OffsetX,
                    OffsetY = transform.OffsetY,
                    Scale = transform.Scale,
                    Color = legacyColor,
                    Colors = colors,
                });
        }

        CharacterNormalizationResult normalized = CharacterDocumentNormalizer.Normalize(result);
        CharacterValidationResult validation = CharacterDocumentValidator.Validate(
            normalized.Document,
            catalogue);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                $"Randomized character was invalid: {string.Join("; ", validation.Errors)}");
        return normalized.Document;
    }

    private static Rgba32 NextColor(ref XorShift64 random) =>
        SafeColors[random.NextInt(SafeColors.Length)];

    private struct XorShift64
    {
        private ulong _state;

        public XorShift64(ulong seed) => _state = seed;

        public ulong NextUInt64()
        {
            ulong value = _state;
            value ^= value << 13;
            value ^= value >> 7;
            value ^= value << 17;
            _state = value;
            return value;
        }

        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            return (int)(NextUInt64() % (uint)exclusiveMaximum);
        }
    }
}
