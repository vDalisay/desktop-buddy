using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DesktopBuddy.Domain.Characters;

public enum CharacterFeatureSlot
{
    Face,
    Hair,
    Brows,
    Eyes,
    Nose,
    Mouth,
    Ears,
    Accessories,
    Glasses,
    Headwear,
    Tops,
    Shoes,

    // Source-compatibility alias for the pre-Studio accent slot.
    TorsoAccent = Accessories,
}

public static class CharacterFeatureIds
{
    public const string FaceClassicPlate = "face.classic_plate";
    public const string HairNone = "hair.none";
    public const string HairShortSweep = "hair.short_sweep";
    public const string EyesSoftOval = "eyes.soft_oval";
    public const string EyesRoundDot = "eyes.round_dot";
    public const string EyesHorizontalLed = "eyes.horizontal_led";
    public const string BrowsSoftArc = "brows.soft_arc";
    public const string BrowsStraight = "brows.straight";
    public const string BrowsSegmented = "brows.segmented";
    public const string NoseNone = "nose.none";
    public const string MouthRounded = "mouth.rounded";
    public const string MouthPixel = "mouth.pixel";
    public const string MouthLine = "mouth.line";
    public const string EarsNone = "ears.none";
    public const string AccentNone = "accent.none";
    public const string AccentPanel = "accent.panel";
    public const string AccentChevron = "accent.chevron";
    public const string AccentBolts = "accent.bolts";
    public const string GlassesNone = "glasses.none";
    public const string GlassesWorkClassic = "glasses.work_classic";
    public const string HeadwearNone = "headwear.none";
    public const string HeadwearSoftCap = "headwear.soft_cap";
    public const string TopNone = "top.none";
    public const string ShoesNone = "shoes.none";
}

public sealed class CharacterFeatureCatalog
{
    private readonly IReadOnlyDictionary<string, CosmeticDefinition> _definitionsById;
    private readonly IReadOnlyDictionary<CharacterFeatureSlot, IReadOnlyList<CosmeticDefinition>> _definitionsBySlot;
    private readonly IReadOnlyDictionary<CharacterFeatureSlot, string> _defaultsBySlot;

    /// <summary>Legacy constructor retained for Phase-A tests and call sites.</summary>
    public CharacterFeatureCatalog(
        IEnumerable<string> eyeIds,
        string defaultEyeId,
        IEnumerable<string> browIds,
        string defaultBrowId,
        IEnumerable<string> mouthIds,
        string defaultMouthId,
        IEnumerable<string> torsoAccentIds,
        string defaultTorsoAccentId)
        : this(CreateLegacyDefinitions(
            eyeIds, defaultEyeId,
            browIds, defaultBrowId,
            mouthIds, defaultMouthId,
            torsoAccentIds, defaultTorsoAccentId))
    {
    }

    public CharacterFeatureCatalog(IEnumerable<CosmeticDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        CosmeticDefinition[] authored = definitions.ToArray();
        var byId = new Dictionary<string, CosmeticDefinition>(StringComparer.Ordinal);
        var bySlot = new Dictionary<CharacterFeatureSlot, IReadOnlyList<CosmeticDefinition>>();
        var defaults = new Dictionary<CharacterFeatureSlot, string>();

        foreach (CosmeticDefinition definition in authored)
            if (!byId.TryAdd(definition.Id, definition))
                throw new ArgumentException($"Duplicate cosmetic ID '{definition.Id}'.", nameof(definitions));

        foreach (CharacterFeatureSlot slot in CanonicalSlots())
        {
            CosmeticDefinition[] slotDefinitions = authored
                .Where(definition => definition.Slot == slot)
                .OrderBy(definition => definition.SortOrder)
                .ThenBy(definition => definition.Id, StringComparer.Ordinal)
                .ToArray();
            if (slotDefinitions.Length == 0)
                throw new ArgumentException($"Missing cosmetic definitions for {slot}.", nameof(definitions));
            if (slotDefinitions.Select(definition => definition.SortOrder).Distinct().Count() != slotDefinitions.Length)
                throw new ArgumentException($"Sort orders must be unique within {slot}.", nameof(definitions));

            CosmeticDefinition[] slotDefaults = slotDefinitions
                .Where(definition => string.Equals(definition.Id, definition.FallbackId, StringComparison.Ordinal))
                .ToArray();
            if (slotDefaults.Length != 1)
                throw new ArgumentException($"Slot {slot} requires exactly one self-fallback default.", nameof(definitions));
            CosmeticDefinition slotDefault = slotDefaults[0];
            if (!slotDefault.IsFreeDefault)
                throw new ArgumentException($"Default cosmetic '{slotDefault.Id}' must be free.", nameof(definitions));
            foreach (CosmeticDefinition definition in slotDefinitions)
                if (!byId.TryGetValue(definition.FallbackId, out CosmeticDefinition? fallback) || fallback.Slot != slot)
                    throw new ArgumentException($"Fallback '{definition.FallbackId}' must exist in {slot}.", nameof(definitions));

            bySlot.Add(slot, Array.AsReadOnly(slotDefinitions));
            defaults.Add(slot, slotDefault.Id);
        }

        _definitionsById = new ReadOnlyDictionary<string, CosmeticDefinition>(byId);
        _definitionsBySlot = new ReadOnlyDictionary<CharacterFeatureSlot, IReadOnlyList<CosmeticDefinition>>(bySlot);
        _defaultsBySlot = new ReadOnlyDictionary<CharacterFeatureSlot, string>(defaults);
    }

    public static CharacterFeatureCatalog Shipped { get; } = new(CreateShippedDefinitions());

    public IEnumerable<string> AllIds => _definitionsById.Keys;
    public IReadOnlyList<string> GetIds(CharacterFeatureSlot slot) =>
        GetDefinitions(slot).Select(definition => definition.Id).ToArray();
    public IReadOnlyList<CosmeticDefinition> GetDefinitions(CharacterFeatureSlot slot) =>
        _definitionsBySlot[Canonical(slot)];
    public string GetDefaultId(CharacterFeatureSlot slot) => _defaultsBySlot[Canonical(slot)];

    public bool Contains(CharacterFeatureSlot slot, string featureId) =>
        featureId is not null &&
        _definitionsById.TryGetValue(featureId, out CosmeticDefinition? definition) &&
        definition.Slot == Canonical(slot);

    public bool TryGetDefinition(string featureId, out CosmeticDefinition definition)
    {
        if (featureId is not null && _definitionsById.TryGetValue(featureId, out CosmeticDefinition? found))
        {
            definition = found;
            return true;
        }
        definition = null!;
        return false;
    }

    public bool TryGetSlot(string featureId, out CharacterFeatureSlot slot)
    {
        if (TryGetDefinition(featureId, out CosmeticDefinition definition))
        {
            slot = definition.Slot;
            return true;
        }
        slot = default;
        return false;
    }

    public string Resolve(CharacterFeatureSlot slot, string featureId, out bool known)
    {
        slot = Canonical(slot);
        known = Contains(slot, featureId);
        return known ? featureId : GetDefaultId(slot);
    }

    public CosmeticDefinition ResolveDefinition(CharacterFeatureSlot slot, string featureId, out bool known) =>
        _definitionsById[Resolve(slot, featureId, out known)];

    private static CharacterFeatureSlot Canonical(CharacterFeatureSlot slot) =>
        slot == CharacterFeatureSlot.TorsoAccent ? CharacterFeatureSlot.Accessories : slot;

    private static IEnumerable<CharacterFeatureSlot> CanonicalSlots()
    {
        yield return CharacterFeatureSlot.Face;
        yield return CharacterFeatureSlot.Hair;
        yield return CharacterFeatureSlot.Brows;
        yield return CharacterFeatureSlot.Eyes;
        yield return CharacterFeatureSlot.Nose;
        yield return CharacterFeatureSlot.Mouth;
        yield return CharacterFeatureSlot.Ears;
        yield return CharacterFeatureSlot.Accessories;
        yield return CharacterFeatureSlot.Glasses;
        yield return CharacterFeatureSlot.Headwear;
        yield return CharacterFeatureSlot.Tops;
        yield return CharacterFeatureSlot.Shoes;
    }

    private static IEnumerable<CosmeticDefinition> CreateShippedDefinitions()
    {
        Rgba32 ink = Rgba32.Parse("#183042");
        yield return Definition(CharacterFeatureIds.FaceClassicPlate, CharacterFeatureSlot.Face, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.FaceClassicPlate);
        yield return Definition(CharacterFeatureIds.HairNone, CharacterFeatureSlot.Hair, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.HairNone);
        yield return Definition(CharacterFeatureIds.HairShortSweep, CharacterFeatureSlot.Hair, 10, true, CosmeticTransformPolicy.None, CharacterFeatureIds.HairNone, Rgba32.Parse("#6A4937"));
        yield return Definition(CharacterFeatureIds.BrowsSoftArc, CharacterFeatureSlot.Brows, 0, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.BrowsSoftArc, ink);
        yield return Definition(CharacterFeatureIds.BrowsStraight, CharacterFeatureSlot.Brows, 10, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.BrowsSoftArc, ink);
        yield return Definition(CharacterFeatureIds.BrowsSegmented, CharacterFeatureSlot.Brows, 20, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.BrowsSoftArc, ink);
        yield return Definition(CharacterFeatureIds.EyesSoftOval, CharacterFeatureSlot.Eyes, 0, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, ink);
        yield return Definition(CharacterFeatureIds.EyesRoundDot, CharacterFeatureSlot.Eyes, 10, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, ink);
        yield return Definition(CharacterFeatureIds.EyesHorizontalLed, CharacterFeatureSlot.Eyes, 20, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, ink);
        yield return Definition(CharacterFeatureIds.NoseNone, CharacterFeatureSlot.Nose, 0, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.NoseNone);
        yield return Definition(CharacterFeatureIds.MouthRounded, CharacterFeatureSlot.Mouth, 0, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, ink);
        yield return Definition(CharacterFeatureIds.MouthPixel, CharacterFeatureSlot.Mouth, 10, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, ink);
        yield return Definition(CharacterFeatureIds.MouthLine, CharacterFeatureSlot.Mouth, 20, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, ink);
        yield return Definition(CharacterFeatureIds.EarsNone, CharacterFeatureSlot.Ears, 0, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EarsNone);
        yield return Definition(CharacterFeatureIds.AccentNone, CharacterFeatureSlot.Accessories, 0, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.AccentNone, ink);
        yield return Definition(CharacterFeatureIds.AccentPanel, CharacterFeatureSlot.Accessories, 10, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.AccentNone, ink);
        yield return Definition(CharacterFeatureIds.AccentChevron, CharacterFeatureSlot.Accessories, 20, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.AccentNone, ink);
        yield return Definition(CharacterFeatureIds.AccentBolts, CharacterFeatureSlot.Accessories, 30, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.AccentNone, ink);
        yield return Definition(CharacterFeatureIds.GlassesNone, CharacterFeatureSlot.Glasses, 0, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.GlassesNone);
        yield return Definition(CharacterFeatureIds.GlassesWorkClassic, CharacterFeatureSlot.Glasses, 10, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.GlassesNone, ink);
        yield return Definition(CharacterFeatureIds.HeadwearNone, CharacterFeatureSlot.Headwear, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.HeadwearNone);
        yield return Definition(CharacterFeatureIds.HeadwearSoftCap, CharacterFeatureSlot.Headwear, 10, true, CosmeticTransformPolicy.None, CharacterFeatureIds.HeadwearNone, Rgba32.Parse("#C95B63"), hidesHair: true);
        yield return Definition(CharacterFeatureIds.TopNone, CharacterFeatureSlot.Tops, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.TopNone);
        yield return Definition(CharacterFeatureIds.ShoesNone, CharacterFeatureSlot.Shoes, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.ShoesNone);
    }

    private static CosmeticDefinition Definition(
        string id,
        CharacterFeatureSlot slot,
        int order,
        bool free,
        CosmeticTransformPolicy policy,
        string fallback,
        Rgba32? primary = null,
        bool hidesHair = false) => new(
            id,
            slot,
            $"buddy_studio.cosmetic.{id}.name",
            order,
            free,
            policy,
            policy == CosmeticTransformPolicy.None ? CosmeticTransformBounds.None : CosmeticTransformBounds.Standard,
            NormalizedFeatureTransform.Identity,
            primary is Rgba32 color ? [new CosmeticColorChannelDefinition(CosmeticDefinition.PrimaryColorChannel, color)] : [],
            fallback,
            hidesHair);

    private static IEnumerable<CosmeticDefinition> CreateLegacyDefinitions(
        IEnumerable<string> eyeIds, string defaultEyeId,
        IEnumerable<string> browIds, string defaultBrowId,
        IEnumerable<string> mouthIds, string defaultMouthId,
        IEnumerable<string> accentIds, string defaultAccentId)
    {
        Rgba32 ink = Rgba32.Parse("#183042");
        foreach (CosmeticDefinition definition in CreateShippedDefinitions().Where(definition =>
                     definition.Slot is not (CharacterFeatureSlot.Eyes or CharacterFeatureSlot.Brows or CharacterFeatureSlot.Mouth or CharacterFeatureSlot.Accessories)))
            yield return definition;
        foreach ((IEnumerable<string> ids, string defaultId, CharacterFeatureSlot slot) in new[]
                 {
                     (eyeIds, defaultEyeId, CharacterFeatureSlot.Eyes),
                     (browIds, defaultBrowId, CharacterFeatureSlot.Brows),
                     (mouthIds, defaultMouthId, CharacterFeatureSlot.Mouth),
                     (accentIds, defaultAccentId, CharacterFeatureSlot.Accessories),
                 })
        {
            int order = 0;
            foreach (string id in ids)
            {
                yield return Definition(id, slot, order, true, CosmeticTransformPolicy.MoveAndUniformScale, defaultId, ink);
                order += 10;
            }
        }
    }
}
