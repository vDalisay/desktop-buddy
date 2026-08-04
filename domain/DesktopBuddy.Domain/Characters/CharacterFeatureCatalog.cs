using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DesktopBuddy.Domain.Characters;

public enum CharacterFeatureSlot
{
    Eyes,
    Brows,
    Mouth,
    TorsoAccent,
}

public static class CharacterFeatureIds
{
    public const string EyesButton = "eyes.button";
    public const string EyesSoftOval = "eyes.soft_oval";
    public const string EyesRoundDot = "eyes.round_dot";
    public const string EyesHorizontalLed = "eyes.horizontal_led";

    public const string BrowsSoftArc = "brows.soft_arc";
    public const string BrowsStraight = "brows.straight";
    public const string BrowsSegmented = "brows.segmented";

    public const string MouthRounded = "mouth.rounded";
    public const string MouthPixel = "mouth.pixel";
    public const string MouthLine = "mouth.line";

    public const string AccentNone = "accent.none";
    public const string AccentPanel = "accent.panel";
    public const string AccentChevron = "accent.chevron";
    public const string AccentBolts = "accent.bolts";
}

public sealed class CharacterFeatureCatalog
{
    private readonly Dictionary<string, CharacterFeatureSlot> _slotsById;
    private readonly IReadOnlyDictionary<CharacterFeatureSlot, IReadOnlyList<string>> _idsBySlot;
    private readonly IReadOnlyDictionary<CharacterFeatureSlot, string> _defaultsBySlot;

    public CharacterFeatureCatalog(
        IEnumerable<string> eyeIds,
        string defaultEyeId,
        IEnumerable<string> browIds,
        string defaultBrowId,
        IEnumerable<string> mouthIds,
        string defaultMouthId,
        IEnumerable<string> torsoAccentIds,
        string defaultTorsoAccentId)
    {
        ArgumentNullException.ThrowIfNull(eyeIds);
        ArgumentNullException.ThrowIfNull(browIds);
        ArgumentNullException.ThrowIfNull(mouthIds);
        ArgumentNullException.ThrowIfNull(torsoAccentIds);

        var idsBySlot = new Dictionary<CharacterFeatureSlot, IReadOnlyList<string>>
        {
            [CharacterFeatureSlot.Eyes] = Freeze(eyeIds, nameof(eyeIds)),
            [CharacterFeatureSlot.Brows] = Freeze(browIds, nameof(browIds)),
            [CharacterFeatureSlot.Mouth] = Freeze(mouthIds, nameof(mouthIds)),
            [CharacterFeatureSlot.TorsoAccent] = Freeze(torsoAccentIds, nameof(torsoAccentIds)),
        };

        _slotsById = new Dictionary<string, CharacterFeatureSlot>(StringComparer.Ordinal);
        foreach ((CharacterFeatureSlot slot, IReadOnlyList<string> ids) in idsBySlot)
        {
            foreach (string id in ids)
            {
                if (!_slotsById.TryAdd(id, slot))
                {
                    throw new ArgumentException(
                        $"Feature ID '{id}' belongs to more than one slot.");
                }
            }
        }

        var defaults = new Dictionary<CharacterFeatureSlot, string>
        {
            [CharacterFeatureSlot.Eyes] = ValidateDefault(
                CharacterFeatureSlot.Eyes, defaultEyeId, idsBySlot),
            [CharacterFeatureSlot.Brows] = ValidateDefault(
                CharacterFeatureSlot.Brows, defaultBrowId, idsBySlot),
            [CharacterFeatureSlot.Mouth] = ValidateDefault(
                CharacterFeatureSlot.Mouth, defaultMouthId, idsBySlot),
            [CharacterFeatureSlot.TorsoAccent] = ValidateDefault(
                CharacterFeatureSlot.TorsoAccent, defaultTorsoAccentId, idsBySlot),
        };

        _idsBySlot = new ReadOnlyDictionary<CharacterFeatureSlot, IReadOnlyList<string>>(idsBySlot);
        _defaultsBySlot = new ReadOnlyDictionary<CharacterFeatureSlot, string>(defaults);
    }

    public static CharacterFeatureCatalog Shipped { get; } = new(
        [
            CharacterFeatureIds.EyesButton,
            CharacterFeatureIds.EyesSoftOval,
            CharacterFeatureIds.EyesRoundDot,
            CharacterFeatureIds.EyesHorizontalLed,
        ],
        CharacterFeatureIds.EyesButton,
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

    public IReadOnlyList<string> GetIds(CharacterFeatureSlot slot) => _idsBySlot[slot];

    public string GetDefaultId(CharacterFeatureSlot slot) => _defaultsBySlot[slot];

    public bool Contains(CharacterFeatureSlot slot, string featureId) =>
        featureId is not null &&
        _slotsById.TryGetValue(featureId, out CharacterFeatureSlot actual) &&
        actual == slot;

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
        known = Contains(slot, featureId);
        return known ? featureId : GetDefaultId(slot);
    }

    private static IReadOnlyList<string> Freeze(IEnumerable<string> ids, string parameterName)
    {
        string[] values = ids.ToArray();
        if (values.Length == 0)
            throw new ArgumentException("Each feature slot requires at least one ID.", parameterName);

        for (int index = 0; index < values.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(values[index]))
                throw new ArgumentException("Feature IDs cannot be empty.", parameterName);
        }

        return Array.AsReadOnly(values);
    }

    private static string ValidateDefault(
        CharacterFeatureSlot slot,
        string defaultId,
        IReadOnlyDictionary<CharacterFeatureSlot, IReadOnlyList<string>> idsBySlot)
    {
        if (string.IsNullOrWhiteSpace(defaultId) ||
            !idsBySlot[slot].Contains(defaultId, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Default ID '{defaultId}' does not belong to slot {slot}.");
        }

        return defaultId;
    }
}
