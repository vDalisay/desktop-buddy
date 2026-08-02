using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Fully deterministic Phase A randomization. Stable IDs are discovered from the shipped
/// constants, sorted ordinally, and sampled with an owned xorshift stream. Torso accent
/// deliberately excludes accent.none.
/// </summary>
public static class CharacterRandomizer
{
    private static readonly string[] Eyes = Ids("eyes.");
    private static readonly string[] Brows = Ids("brows.");
    private static readonly string[] Mouth = Ids("mouth.");
    private static readonly string[] Accents = Ids("accent.")
        .Where(id => !string.Equals(id, CharacterFeatureIds.AccentNone, StringComparison.Ordinal))
        .ToArray();

    public static CharacterDocument Randomize(CharacterDocument document, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(document);
        var random = new XorShift64(seed == 0 ? 0x9E3779B97F4A7C15UL : seed);
        CharacterDocument result = document;

        foreach (CharacterPartSlot part in Enum.GetValues<CharacterPartSlot>())
            result = CharacterDocumentEditor.SetPartColor(result, part, NextColor(ref random));

        result = RandomizeFeature(result, CharacterFeatureSlot.Eyes, Eyes, ref random);
        result = RandomizeFeature(result, CharacterFeatureSlot.Brows, Brows, ref random);
        result = RandomizeFeature(result, CharacterFeatureSlot.Mouth, Mouth, ref random);
        result = RandomizeFeature(result, CharacterFeatureSlot.TorsoAccent, Accents, ref random);
        return result;
    }

    private static CharacterDocument RandomizeFeature(
        CharacterDocument document,
        CharacterFeatureSlot slot,
        IReadOnlyList<string> ids,
        ref XorShift64 random)
    {
        if (ids.Count == 0)
            throw new InvalidOperationException($"No shipped feature IDs exist for {slot}.");

        CharacterDocument result = CharacterDocumentEditor.SetFeatureId(
            document,
            slot,
            ids[random.NextInt(ids.Count)]);
        result = CharacterDocumentEditor.SetFeatureTransform(
            result,
            slot,
            new NormalizedFeatureTransform(
                NextRange(ref random, NormalizedFeatureTransform.MinimumOffset,
                    NormalizedFeatureTransform.MaximumOffset),
                NextRange(ref random, NormalizedFeatureTransform.MinimumOffset,
                    NormalizedFeatureTransform.MaximumOffset),
                NextRange(ref random, NormalizedFeatureTransform.MinimumScale,
                    NormalizedFeatureTransform.MaximumScale)));
        return CharacterDocumentEditor.SetFeatureColor(result, slot, NextColor(ref random));
    }

    private static Rgba32 NextColor(ref XorShift64 random) => new(
        (byte)(32 + random.NextInt(224)),
        (byte)(32 + random.NextInt(224)),
        (byte)(32 + random.NextInt(224)));

    private static double NextRange(ref XorShift64 random, double minimum, double maximum)
    {
        double unit = random.NextUInt64() / (double)ulong.MaxValue;
        double value = minimum + ((maximum - minimum) * unit);
        return Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    private static string[] Ids(string prefix) =>
        typeof(CharacterFeatureIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.IsLiteral ? field.GetRawConstantValue() as string : field.GetValue(null) as string)
            .Where(value => value is not null && value.StartsWith(prefix, StringComparison.Ordinal))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

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
