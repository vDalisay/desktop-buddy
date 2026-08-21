using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DesktopBuddy.Domain.Content;

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
    public const string HairBobBangs = "hair.bob_bangs";
    public const string HairBuzzCut = "hair.buzz_cut";
    public const string EyesSoftOval = "eyes.soft_oval";
    public const string EyesRoundDot = "eyes.round_dot";
    public const string EyesHorizontalLed = "eyes.horizontal_led";
    public const string EyesLashedOval = "eyes.lashed_oval";
    public const string BrowsSoftArc = "brows.soft_arc";
    public const string BrowsStraight = "brows.straight";
    public const string BrowsSegmented = "brows.segmented";
    public const string BrowsBushy = "brows.bushy";
    public const string NoseNone = "nose.none";
    public const string NoseButton = "nose.button";
    public const string NoseTriangle = "nose.triangle";
    public const string NoseBroadOval = "nose.broad_oval";
    public const string MouthRounded = "mouth.rounded";
    public const string MouthPixel = "mouth.pixel";
    public const string MouthLine = "mouth.line";
    public const string MouthOval = "mouth.oval";
    public const string EarsNone = "ears.none";
    public const string EarsRoundTabs = "ears.round_tabs";
    public const string EarsPointedTips = "ears.pointed_tips";
    public const string EarsFlatDiscs = "ears.flat_discs";
    public const string AccentNone = "accent.none";
    public const string AccentPanel = "accent.panel";
    public const string AccentChevron = "accent.chevron";
    public const string AccentBolts = "accent.bolts";
    public const string GlassesNone = "glasses.none";
    public const string GlassesWorkClassic = "glasses.work_classic";
    public const string GlassesRoundWire = "glasses.round_wire";
    public const string GlassesShades = "glasses.shades";
    public const string HeadwearNone = "headwear.none";
    public const string HeadwearSoftCap = "headwear.soft_cap";
    public const string HeadwearKnitBeanie = "headwear.knit_beanie";
    public const string HeadwearWideBrim = "headwear.wide_brim";
    public const string TopNone = "top.none";
    public const string TopUtilityBib = "top.utility_bib";
    public const string ShoesNone = "shoes.none";
    public const string ShoesSoftSteps = "shoes.soft_steps";

    // Second cosmetic wave (owner instruction 2026-08-21).
    public const string FaceWrinkles = "face.wrinkles";
    public const string FaceChiseledCheeks = "face.chiseled_cheeks";
    public const string FaceFreckles = "face.freckles";
    public const string FaceRosyCheeks = "face.rosy_cheeks";
    public const string FaceStubble = "face.stubble";
    public const string HairElderTufts = "hair.elder_tufts";
    public const string EyesSleepyHalf = "eyes.sleepy_half";
    public const string EyesAngrySlant = "eyes.angry_slant";
    public const string EyesWideSparkle = "eyes.wide_sparkle";
    public const string EyesNarrowSlit = "eyes.narrow_slit";
    public const string EyesBigRound = "eyes.big_round";
    public const string NosePointedBeak = "nose.pointed_beak";
    public const string NoseWideFlat = "nose.wide_flat";
    public const string NoseUpturned = "nose.upturned";
    public const string NoseHooked = "nose.hooked";
    public const string NoseTinyDot = "nose.tiny_dot";
    public const string MouthWideGrin = "mouth.wide_grin";
    public const string MouthFrown = "mouth.frown";
    public const string MouthSmirk = "mouth.smirk";
    public const string MouthOpenSmile = "mouth.open_smile";
    public const string MouthPucker = "mouth.pucker";
    public const string EarsElf = "ears.elf";
    public const string GlassesSquareFrames = "glasses.square_frames";
    public const string GlassesCatEye = "glasses.cat_eye";
    public const string GlassesAviators = "glasses.aviators";
    public const string GlassesHalfMoon = "glasses.half_moon";
    public const string GlassesVisor = "glasses.visor";
    public const string HeadwearBallCap = "headwear.ball_cap";
    public const string HeadwearSunflowerHat = "headwear.sunflower_hat";
    public const string HeadwearFedora = "headwear.fedora";
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
        yield return Definition(CharacterFeatureIds.HairShortSweep, CharacterFeatureSlot.Hair, 10, false, CosmeticTransformPolicy.None, CharacterFeatureIds.HairNone, Rgba32.Parse("#6A4937"), ownershipContentId: ContentIds.CosmeticHairShortSweep);
        yield return Definition(CharacterFeatureIds.HairBobBangs, CharacterFeatureSlot.Hair, 20, false, CosmeticTransformPolicy.None, CharacterFeatureIds.HairNone, Rgba32.Parse("#6A4937"), ownershipContentId: ContentIds.CosmeticHairBobBangs);
        yield return Definition(CharacterFeatureIds.HairBuzzCut, CharacterFeatureSlot.Hair, 30, false, CosmeticTransformPolicy.None, CharacterFeatureIds.HairNone, Rgba32.Parse("#6A4937"), ownershipContentId: ContentIds.CosmeticHairBuzzCut);
        yield return Definition(CharacterFeatureIds.BrowsSoftArc, CharacterFeatureSlot.Brows, 0, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.BrowsSoftArc, ink);
        yield return Definition(CharacterFeatureIds.BrowsStraight, CharacterFeatureSlot.Brows, 10, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.BrowsSoftArc, ink, ownershipContentId: ContentIds.CosmeticBrowsStraight);
        yield return Definition(CharacterFeatureIds.BrowsSegmented, CharacterFeatureSlot.Brows, 20, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.BrowsSoftArc, ink, ownershipContentId: ContentIds.CosmeticBrowsSegmented);
        yield return Definition(CharacterFeatureIds.BrowsBushy, CharacterFeatureSlot.Brows, 30, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.BrowsSoftArc, ink, ownershipContentId: ContentIds.CosmeticBrowsBushy);
        yield return Definition(CharacterFeatureIds.EyesSoftOval, CharacterFeatureSlot.Eyes, 0, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, ink);
        yield return Definition(CharacterFeatureIds.EyesRoundDot, CharacterFeatureSlot.Eyes, 10, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, ink, ownershipContentId: ContentIds.CosmeticEyesRoundDot);
        yield return Definition(CharacterFeatureIds.EyesHorizontalLed, CharacterFeatureSlot.Eyes, 20, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, ink, ownershipContentId: ContentIds.CosmeticEyesHorizontalLed);
        yield return Definition(CharacterFeatureIds.EyesLashedOval, CharacterFeatureSlot.Eyes, 30, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, ink, ownershipContentId: ContentIds.CosmeticEyesLashedOval);
        yield return Definition(CharacterFeatureIds.NoseNone, CharacterFeatureSlot.Nose, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.NoseNone);
        yield return Definition(CharacterFeatureIds.NoseButton, CharacterFeatureSlot.Nose, 10, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.NoseNone, Rgba32.Parse("#F0A06B"), ownershipContentId: ContentIds.CosmeticNoseButton);
        yield return Definition(CharacterFeatureIds.NoseTriangle, CharacterFeatureSlot.Nose, 20, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.NoseNone, Rgba32.Parse("#F0A06B"), ownershipContentId: ContentIds.CosmeticNoseTriangle);
        yield return Definition(CharacterFeatureIds.NoseBroadOval, CharacterFeatureSlot.Nose, 30, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.NoseNone, Rgba32.Parse("#F0A06B"), ownershipContentId: ContentIds.CosmeticNoseBroadOval);
        yield return Definition(CharacterFeatureIds.MouthRounded, CharacterFeatureSlot.Mouth, 0, true, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, ink);
        yield return Definition(CharacterFeatureIds.MouthPixel, CharacterFeatureSlot.Mouth, 10, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, ink, ownershipContentId: ContentIds.CosmeticMouthPixel);
        yield return Definition(CharacterFeatureIds.MouthLine, CharacterFeatureSlot.Mouth, 20, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, ink, ownershipContentId: ContentIds.CosmeticMouthLine);
        yield return Definition(CharacterFeatureIds.MouthOval, CharacterFeatureSlot.Mouth, 30, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, ink, ownershipContentId: ContentIds.CosmeticMouthOval);
        yield return Definition(CharacterFeatureIds.EarsNone, CharacterFeatureSlot.Ears, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.EarsNone);
        yield return Definition(CharacterFeatureIds.EarsRoundTabs, CharacterFeatureSlot.Ears, 10, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EarsNone, Rgba32.Parse("#74B9E8"), ownershipContentId: ContentIds.CosmeticEarsRoundTabs);
        yield return Definition(CharacterFeatureIds.EarsPointedTips, CharacterFeatureSlot.Ears, 20, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EarsNone, Rgba32.Parse("#74B9E8"), ownershipContentId: ContentIds.CosmeticEarsPointedTips);
        yield return Definition(CharacterFeatureIds.EarsFlatDiscs, CharacterFeatureSlot.Ears, 30, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EarsNone, Rgba32.Parse("#74B9E8"), ownershipContentId: ContentIds.CosmeticEarsFlatDiscs);
        yield return Definition(CharacterFeatureIds.AccentNone, CharacterFeatureSlot.Accessories, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.AccentNone, ink);
        yield return Definition(CharacterFeatureIds.AccentPanel, CharacterFeatureSlot.Accessories, 10, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.AccentNone, ink, ownershipContentId: ContentIds.CosmeticAccentPanel);
        yield return Definition(CharacterFeatureIds.AccentChevron, CharacterFeatureSlot.Accessories, 20, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.AccentNone, ink, ownershipContentId: ContentIds.CosmeticAccentChevron);
        yield return Definition(CharacterFeatureIds.AccentBolts, CharacterFeatureSlot.Accessories, 30, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.AccentNone, ink, ownershipContentId: ContentIds.CosmeticAccentBolts);
        yield return Definition(CharacterFeatureIds.GlassesNone, CharacterFeatureSlot.Glasses, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.GlassesNone);
        yield return Definition(
            CharacterFeatureIds.GlassesWorkClassic,
            CharacterFeatureSlot.Glasses,
            10,
            false,
            CosmeticTransformPolicy.MoveAndUniformScale,
            CharacterFeatureIds.GlassesNone,
            ink,
            ownershipContentId: ContentIds.CosmeticWorkGlasses);
        yield return Definition(CharacterFeatureIds.GlassesRoundWire, CharacterFeatureSlot.Glasses, 20, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.GlassesNone, ink, ownershipContentId: ContentIds.CosmeticGlassesRoundWire);
        yield return Definition(CharacterFeatureIds.GlassesShades, CharacterFeatureSlot.Glasses, 30, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.GlassesNone, ink, ownershipContentId: ContentIds.CosmeticGlassesShades);
        yield return Definition(CharacterFeatureIds.HeadwearNone, CharacterFeatureSlot.Headwear, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.HeadwearNone);
        yield return Definition(CharacterFeatureIds.HeadwearSoftCap, CharacterFeatureSlot.Headwear, 10, false, CosmeticTransformPolicy.None, CharacterFeatureIds.HeadwearNone, Rgba32.Parse("#C95B63"), hidesHair: true, ownershipContentId: ContentIds.CosmeticHeadwearSoftCap);
        yield return Definition(CharacterFeatureIds.HeadwearKnitBeanie, CharacterFeatureSlot.Headwear, 20, false, CosmeticTransformPolicy.None, CharacterFeatureIds.HeadwearNone, Rgba32.Parse("#C95B63"), hidesHair: true, ownershipContentId: ContentIds.CosmeticHeadwearKnitBeanie);
        yield return Definition(CharacterFeatureIds.HeadwearWideBrim, CharacterFeatureSlot.Headwear, 30, false, CosmeticTransformPolicy.None, CharacterFeatureIds.HeadwearNone, Rgba32.Parse("#C95B63"), hidesHair: true, ownershipContentId: ContentIds.CosmeticHeadwearWideBrim);
        yield return Definition(CharacterFeatureIds.TopNone, CharacterFeatureSlot.Tops, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.TopNone);
        yield return Definition(CharacterFeatureIds.TopUtilityBib, CharacterFeatureSlot.Tops, 10, false, CosmeticTransformPolicy.None, CharacterFeatureIds.TopNone, Rgba32.Parse("#E3A33A"), ownershipContentId: ContentIds.CosmeticTopUtilityBib);
        yield return Definition(CharacterFeatureIds.ShoesNone, CharacterFeatureSlot.Shoes, 0, true, CosmeticTransformPolicy.None, CharacterFeatureIds.ShoesNone);
        yield return Definition(CharacterFeatureIds.ShoesSoftSteps, CharacterFeatureSlot.Shoes, 10, false, CosmeticTransformPolicy.None, CharacterFeatureIds.ShoesNone, Rgba32.Parse("#5A6575"), ownershipContentId: ContentIds.CosmeticShoesSoftSteps);
        yield return Definition(CharacterFeatureIds.FaceWrinkles, CharacterFeatureSlot.Face, 10, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.FaceClassicPlate, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticFaceWrinkles);
        yield return Definition(CharacterFeatureIds.FaceChiseledCheeks, CharacterFeatureSlot.Face, 20, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.FaceClassicPlate, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticFaceChiseledCheeks);
        yield return Definition(CharacterFeatureIds.FaceFreckles, CharacterFeatureSlot.Face, 30, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.FaceClassicPlate, Rgba32.Parse("#6A4937"), ownershipContentId: ContentIds.CosmeticFaceFreckles);
        yield return Definition(CharacterFeatureIds.FaceRosyCheeks, CharacterFeatureSlot.Face, 40, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.FaceClassicPlate, Rgba32.Parse("#C95B63"), ownershipContentId: ContentIds.CosmeticFaceRosyCheeks);
        yield return Definition(CharacterFeatureIds.FaceStubble, CharacterFeatureSlot.Face, 50, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.FaceClassicPlate, Rgba32.Parse("#5A6575"), ownershipContentId: ContentIds.CosmeticFaceStubble);
        yield return Definition(CharacterFeatureIds.HairElderTufts, CharacterFeatureSlot.Hair, 40, false, CosmeticTransformPolicy.None, CharacterFeatureIds.HairNone, Rgba32.Parse("#9AA0A6"), ownershipContentId: ContentIds.CosmeticHairElderTufts);
        yield return Definition(CharacterFeatureIds.EyesSleepyHalf, CharacterFeatureSlot.Eyes, 40, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticEyesSleepyHalf);
        yield return Definition(CharacterFeatureIds.EyesAngrySlant, CharacterFeatureSlot.Eyes, 50, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticEyesAngrySlant);
        yield return Definition(CharacterFeatureIds.EyesWideSparkle, CharacterFeatureSlot.Eyes, 60, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticEyesWideSparkle);
        yield return Definition(CharacterFeatureIds.EyesNarrowSlit, CharacterFeatureSlot.Eyes, 70, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticEyesNarrowSlit);
        yield return Definition(CharacterFeatureIds.EyesBigRound, CharacterFeatureSlot.Eyes, 80, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EyesSoftOval, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticEyesBigRound);
        yield return Definition(CharacterFeatureIds.NosePointedBeak, CharacterFeatureSlot.Nose, 40, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.NoseNone, Rgba32.Parse("#F0A06B"), ownershipContentId: ContentIds.CosmeticNosePointedBeak);
        yield return Definition(CharacterFeatureIds.NoseWideFlat, CharacterFeatureSlot.Nose, 50, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.NoseNone, Rgba32.Parse("#F0A06B"), ownershipContentId: ContentIds.CosmeticNoseWideFlat);
        yield return Definition(CharacterFeatureIds.NoseUpturned, CharacterFeatureSlot.Nose, 60, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.NoseNone, Rgba32.Parse("#F0A06B"), ownershipContentId: ContentIds.CosmeticNoseUpturned);
        yield return Definition(CharacterFeatureIds.NoseHooked, CharacterFeatureSlot.Nose, 70, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.NoseNone, Rgba32.Parse("#F0A06B"), ownershipContentId: ContentIds.CosmeticNoseHooked);
        yield return Definition(CharacterFeatureIds.NoseTinyDot, CharacterFeatureSlot.Nose, 80, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.NoseNone, Rgba32.Parse("#F0A06B"), ownershipContentId: ContentIds.CosmeticNoseTinyDot);
        yield return Definition(CharacterFeatureIds.MouthWideGrin, CharacterFeatureSlot.Mouth, 40, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticMouthWideGrin);
        yield return Definition(CharacterFeatureIds.MouthFrown, CharacterFeatureSlot.Mouth, 50, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticMouthFrown);
        yield return Definition(CharacterFeatureIds.MouthSmirk, CharacterFeatureSlot.Mouth, 60, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticMouthSmirk);
        yield return Definition(CharacterFeatureIds.MouthOpenSmile, CharacterFeatureSlot.Mouth, 70, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticMouthOpenSmile);
        yield return Definition(CharacterFeatureIds.MouthPucker, CharacterFeatureSlot.Mouth, 80, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.MouthRounded, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticMouthPucker);
        yield return Definition(CharacterFeatureIds.EarsElf, CharacterFeatureSlot.Ears, 40, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.EarsNone, Rgba32.Parse("#74B9E8"), ownershipContentId: ContentIds.CosmeticEarsElf);
        yield return Definition(CharacterFeatureIds.GlassesSquareFrames, CharacterFeatureSlot.Glasses, 40, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.GlassesNone, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticGlassesSquareFrames);
        yield return Definition(CharacterFeatureIds.GlassesCatEye, CharacterFeatureSlot.Glasses, 50, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.GlassesNone, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticGlassesCatEye);
        yield return Definition(CharacterFeatureIds.GlassesAviators, CharacterFeatureSlot.Glasses, 60, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.GlassesNone, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticGlassesAviators);
        yield return Definition(CharacterFeatureIds.GlassesHalfMoon, CharacterFeatureSlot.Glasses, 70, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.GlassesNone, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticGlassesHalfMoon);
        yield return Definition(CharacterFeatureIds.GlassesVisor, CharacterFeatureSlot.Glasses, 80, false, CosmeticTransformPolicy.MoveAndUniformScale, CharacterFeatureIds.GlassesNone, Rgba32.Parse("#183042"), ownershipContentId: ContentIds.CosmeticGlassesVisor);
        yield return Definition(CharacterFeatureIds.HeadwearBallCap, CharacterFeatureSlot.Headwear, 40, false, CosmeticTransformPolicy.None, CharacterFeatureIds.HeadwearNone, Rgba32.Parse("#C95B63"), hidesHair: true, ownershipContentId: ContentIds.CosmeticHeadwearBallCap);
        yield return Definition(CharacterFeatureIds.HeadwearSunflowerHat, CharacterFeatureSlot.Headwear, 50, false, CosmeticTransformPolicy.None, CharacterFeatureIds.HeadwearNone, Rgba32.Parse("#E3A33A"), hidesHair: true, ownershipContentId: ContentIds.CosmeticHeadwearSunflowerHat);
        yield return Definition(CharacterFeatureIds.HeadwearFedora, CharacterFeatureSlot.Headwear, 60, false, CosmeticTransformPolicy.None, CharacterFeatureIds.HeadwearNone, Rgba32.Parse("#183042"), hidesHair: true, ownershipContentId: ContentIds.CosmeticHeadwearFedora);
    }

    private static CosmeticDefinition Definition(
        string id,
        CharacterFeatureSlot slot,
        int order,
        bool free,
        CosmeticTransformPolicy policy,
        string fallback,
        Rgba32? primary = null,
        bool hidesHair = false,
        string? ownershipContentId = null) => new(
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
            hidesHair,
            ownershipContentId);

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
