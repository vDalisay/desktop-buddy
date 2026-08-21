using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.Buddy.Presentation3D.Characters;

/// <summary>
/// Closed renderer registry for the shipped Phase A catalog. Construction fails when the
/// engine-free catalog and Godot renderer sets diverge.
/// </summary>
public sealed class CharacterFeatureRendererRegistry
{
    private readonly Dictionary<string, ICharacterEyeRenderer> _eyes =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ICharacterBrowRenderer> _brows =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ICharacterMouthRenderer> _mouths =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ICharacterAccentRenderer> _accents =
        new(StringComparer.Ordinal);

    public CharacterFeatureRendererRegistry(CharacterFeatureCatalog? catalog = null)
    {
        Catalog = catalog ?? CharacterFeatureCatalog.Shipped;

        Add(new ProceduralEyeRenderer(CharacterFeatureIds.EyesSoftOval, EyeVariant.SoftOval));
        Add(new ProceduralEyeRenderer(CharacterFeatureIds.EyesRoundDot, EyeVariant.RoundDot));
        Add(new ProceduralEyeRenderer(CharacterFeatureIds.EyesHorizontalLed, EyeVariant.HorizontalLed));
        Add(new ProceduralEyeRenderer(CharacterFeatureIds.EyesLashedOval, EyeVariant.LashedOval));
        Add(new ProceduralEyeRenderer(CharacterFeatureIds.EyesSleepyHalf, EyeVariant.SleepyHalf));
        Add(new ProceduralEyeRenderer(CharacterFeatureIds.EyesAngrySlant, EyeVariant.AngrySlant));
        Add(new ProceduralEyeRenderer(CharacterFeatureIds.EyesWideSparkle, EyeVariant.WideSparkle));
        Add(new ProceduralEyeRenderer(CharacterFeatureIds.EyesNarrowSlit, EyeVariant.NarrowSlit));
        Add(new ProceduralEyeRenderer(CharacterFeatureIds.EyesBigRound, EyeVariant.BigRound));

        Add(new ProceduralBrowRenderer(CharacterFeatureIds.BrowsSoftArc, BrowVariant.SoftArc));
        Add(new ProceduralBrowRenderer(CharacterFeatureIds.BrowsStraight, BrowVariant.Straight));
        Add(new ProceduralBrowRenderer(CharacterFeatureIds.BrowsSegmented, BrowVariant.Segmented));
        Add(new ProceduralBrowRenderer(CharacterFeatureIds.BrowsBushy, BrowVariant.Bushy));

        Add(new ProceduralMouthRenderer(CharacterFeatureIds.MouthRounded, MouthVariant.Rounded));
        Add(new ProceduralMouthRenderer(CharacterFeatureIds.MouthPixel, MouthVariant.Pixel));
        Add(new ProceduralMouthRenderer(CharacterFeatureIds.MouthLine, MouthVariant.Line));
        Add(new ProceduralMouthRenderer(CharacterFeatureIds.MouthOval, MouthVariant.Oval));
        Add(new ProceduralMouthRenderer(CharacterFeatureIds.MouthWideGrin, MouthVariant.WideGrin));
        Add(new ProceduralMouthRenderer(CharacterFeatureIds.MouthFrown, MouthVariant.Frown));
        Add(new ProceduralMouthRenderer(CharacterFeatureIds.MouthSmirk, MouthVariant.Smirk));
        Add(new ProceduralMouthRenderer(CharacterFeatureIds.MouthOpenSmile, MouthVariant.OpenSmile));
        Add(new ProceduralMouthRenderer(CharacterFeatureIds.MouthPucker, MouthVariant.Pucker));

        Add(new ProceduralAccentRenderer(CharacterFeatureIds.AccentNone, AccentVariant.None));
        Add(new ProceduralAccentRenderer(CharacterFeatureIds.AccentPanel, AccentVariant.Panel));
        Add(new ProceduralAccentRenderer(CharacterFeatureIds.AccentChevron, AccentVariant.Chevron));
        Add(new ProceduralAccentRenderer(CharacterFeatureIds.AccentBolts, AccentVariant.Bolts));

        ValidateExactSet(CharacterFeatureSlot.Eyes, _eyes.Keys);
        ValidateExactSet(CharacterFeatureSlot.Brows, _brows.Keys);
        ValidateExactSet(CharacterFeatureSlot.Mouth, _mouths.Keys);
        ValidateExactSet(CharacterFeatureSlot.TorsoAccent, _accents.Keys);
    }

    public CharacterFeatureCatalog Catalog { get; }
    public IReadOnlyCollection<string> EyeIds => _eyes.Keys;
    public IReadOnlyCollection<string> BrowIds => _brows.Keys;
    public IReadOnlyCollection<string> MouthIds => _mouths.Keys;
    public IReadOnlyCollection<string> AccentIds => _accents.Keys;

    public ICharacterEyeRenderer Eyes(string id) => Resolve(_eyes, id, CharacterFeatureSlot.Eyes);
    public ICharacterBrowRenderer Brows(string id) => Resolve(_brows, id, CharacterFeatureSlot.Brows);
    public ICharacterMouthRenderer Mouth(string id) => Resolve(_mouths, id, CharacterFeatureSlot.Mouth);
    public ICharacterAccentRenderer Accent(string id) => Resolve(_accents, id, CharacterFeatureSlot.TorsoAccent);

    private void Add(ICharacterEyeRenderer renderer) => AddUnique(_eyes, renderer);
    private void Add(ICharacterBrowRenderer renderer) => AddUnique(_brows, renderer);
    private void Add(ICharacterMouthRenderer renderer) => AddUnique(_mouths, renderer);
    private void Add(ICharacterAccentRenderer renderer) => AddUnique(_accents, renderer);

    private static void AddUnique<T>(Dictionary<string, T> target, T renderer)
        where T : ICharacterFeatureRenderer
    {
        if (!target.TryAdd(renderer.FeatureId, renderer))
            throw new InvalidOperationException($"Duplicate feature renderer '{renderer.FeatureId}'.");
    }

    private static T Resolve<T>(
        IReadOnlyDictionary<string, T> values,
        string id,
        CharacterFeatureSlot slot)
    {
        if (id is not null && values.TryGetValue(id, out T? renderer))
            return renderer;
        throw new ArgumentOutOfRangeException(nameof(id), id,
            $"No {slot} renderer is registered for the feature ID.");
    }

    private void ValidateExactSet(CharacterFeatureSlot slot, IEnumerable<string> rendererIds)
    {
        string[] expected = Catalog.GetIds(slot).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        string[] actual = rendererIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Renderer registry mismatch for {slot}. Expected [{string.Join(", ", expected)}], " +
                $"actual [{string.Join(", ", actual)}].");
        }
    }
}
