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
    public const string TopNone = "top.none";
    public const string ShoesNone = "shoes.none";
}

public sealed class CharacterFeatureCatalog
{
    private readonly Dictionary<string, CharacterFeatureSlot> _slotsById;
    private readonly IReadOnlyDictionary<CharacterFeatureSlot, IReadOnlyList<string>> _idsBySlot;
    private readonly IReadOnlyDictionary<CharacterFeatureSlot, string> _defaultsBySlot;

    /// <summary>Legacy constructor retained for existing tests and Phase-A call sites.</summary>
    public CharacterFeatureCatalog(
        IEnumerable<string> eyeIds,
        string defaultEyeId,
        IEnumerable<string> browIds,
        string defaultBrowId,
        IEnumerable<string> mouthIds,
        string defaultMouthId,
        IEnumerable<string> torsoAccentIds,
        string defaultTorsoAccentId)
        : this(new Dictionary<CharacterFeatureSlot, (IEnumerable<string> Ids, string Default)>
        {
            [CharacterFeatureSlot.Face] = ([CharacterFeatureIds.FaceClassicPlate], CharacterFeatureIds.FaceClassicPlate),
            [CharacterFeatureSlot.Hair] = ([CharacterFeatureIds.HairNone], CharacterFeatureIds.HairNone),
            [CharacterFeatureSlot.Brows] = (browIds, defaultBrowId),
            [CharacterFeatureSlot.Eyes] = (eyeIds, defaultEyeId),
            [CharacterFeatureSlot.Nose] = ([CharacterFeatureIds.NoseNone], CharacterFeatureIds.NoseNone),
            [CharacterFeatureSlot.Mouth] = (mouthIds, defaultMouthId),
            [CharacterFeatureSlot.Ears] = ([CharacterFeatureIds.EarsNone], CharacterFeatureIds.EarsNone),
            [CharacterFeatureSlot.Accessories] = (torsoAccentIds, defaultTorsoAccentId),
            [CharacterFeatureSlot.Glasses] = ([CharacterFeatureIds.GlassesNone, CharacterFeatureIds.GlassesWorkClassic], CharacterFeatureIds.GlassesNone),
            [CharacterFeatureSlot.Headwear] = ([CharacterFeatureIds.HeadwearNone], CharacterFeatureIds.HeadwearNone),
            [CharacterFeatureSlot.Tops] = ([CharacterFeatureIds.TopNone], CharacterFeatureIds.TopNone),
            [CharacterFeatureSlot.Shoes] = ([CharacterFeatureIds.ShoesNone], CharacterFeatureIds.ShoesNone),
        })
    {
    }

    public CharacterFeatureCatalog(
        IReadOnlyDictionary<CharacterFeatureSlot, (IEnumerable<string> Ids, string Default)> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var idsBySlot = new Dictionary<CharacterFeatureSlot, IReadOnlyList<string>>();
        var defaults = new Dictionary<CharacterFeatureSlot, string>();
        _slotsById = new Dictionary<string, CharacterFeatureSlot>(StringComparer.Ordinal);

        foreach (CharacterFeatureSlot slot in CanonicalSlots())
        {
            if (!definitions.TryGetValue(slot, out var definition))
                throw new ArgumentException($"Missing feature catalogue definition for {slot}.", nameof(definitions));
            IReadOnlyList<string> ids = Freeze(definition.Ids, slot.ToString());
            idsBySlot[slot] = ids;
            defaults[slot] = ValidateDefault(slot, definition.Default, idsBySlot);
            foreach (string id in ids)
            {
                if (!_slotsById.TryAdd(id, slot))
                    throw new ArgumentException($"Feature ID '{id}' belongs to more than one slot.");
            }
        }

        _idsBySlot = new ReadOnlyDictionary<CharacterFeatureSlot, IReadOnlyList<string>>(idsBySlot);
        _defaultsBySlot = new ReadOnlyDictionary<CharacterFeatureSlot, string>(defaults);
    }

    public static CharacterFeatureCatalog Shipped { get; } = new(
        [
            CharacterFeatureIds.EyesSoftOval,
            CharacterFeatureIds.EyesRoundDot,
            CharacterFeatureIds.EyesHorizontalLed,
        ],
        CharacterFeatureIds.EyesSoftOval,
        [
            CharacterFeatureIds.BrowsSoftArc,
            CharacterFeatureIds.BrowsStraight,
            CharacterFeatureIds.BrowsSegmented,
        ],
        CharacterFeatureIds.BrowsSoftArc,
        [
            CharacterFeatureIds.MouthRounded,
            CharacterFeatureIds.MouthPixel,
            CharacterFeatureIds.MouthLine,
        ],
        CharacterFeatureIds.MouthRounded,
        [
            CharacterFeatureIds.AccentNone,
            CharacterFeatureIds.AccentPanel,
            CharacterFeatureIds.AccentChevron,
            CharacterFeatureIds.AccentBolts,
        ],
        CharacterFeatureIds.AccentNone);

    public IEnumerable<string> AllIds => _slotsById.Keys;
    public IReadOnlyList<string> GetIds(CharacterFeatureSlot slot) => _idsBySlot[Canonical(slot)];
    public string GetDefaultId(CharacterFeatureSlot slot) => _defaultsBySlot[Canonical(slot)];

    public bool Contains(CharacterFeatureSlot slot, string featureId) =>
        featureId is not null &&
        _slotsById.TryGetValue(featureId, out CharacterFeatureSlot actual) &&
        actual == Canonical(slot);

    public bool TryGetSlot(string featureId, out CharacterFeatureSlot slot)
    {
        if (featureId is null)
        {
            slot = default;
            return false;
        }
        return _slotsById.TryGetValue(featureId, out slot);
    }

    public string Resolve(CharacterFeatureSlot slot, string featureId, out bool known)
    {
        slot = Canonical(slot);
        known = Contains(slot, featureId);
        return known ? featureId : GetDefaultId(slot);
    }

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

    private static IReadOnlyList<string> Freeze(IEnumerable<string> ids, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(ids);
        string[] values = ids.ToArray();
        if (values.Length == 0)
            throw new ArgumentException("Each feature slot requires at least one ID.", parameterName);
        for (int index = 0; index < values.Length; index++)
            if (string.IsNullOrWhiteSpace(values[index]))
                throw new ArgumentException("Feature IDs cannot be empty.", parameterName);
        return Array.AsReadOnly(values);
    }

    private static string ValidateDefault(
        CharacterFeatureSlot slot,
        string defaultId,
        IReadOnlyDictionary<CharacterFeatureSlot, IReadOnlyList<string>> idsBySlot)
    {
        if (string.IsNullOrWhiteSpace(defaultId) ||
            !idsBySlot[slot].Contains(defaultId, StringComparer.Ordinal))
            throw new ArgumentException($"Default ID '{defaultId}' does not belong to slot {slot}.");
        return defaultId;
    }
}