using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.CharacterEditor;

public enum CharacterPartSlot
{
    Head,
    Torso,
    LeftHand,
    RightHand,
    LeftFoot,
    RightFoot,
}

/// <summary>
/// Immutable editor mutations over the authoritative schema-v1 JSON boundary. Using the
/// document policy for every mutation keeps UI code independent of persistence DTO internals
/// and guarantees the same normalization/migration/validation path as disk loading.
/// </summary>
public static class CharacterDocumentEditor
{
    private static readonly IReadOnlyDictionary<CharacterPartSlot, string> PartNames =
        new Dictionary<CharacterPartSlot, string>
        {
            [CharacterPartSlot.Head] = "head",
            [CharacterPartSlot.Torso] = "torso",
            [CharacterPartSlot.LeftHand] = "leftHand",
            [CharacterPartSlot.RightHand] = "rightHand",
            [CharacterPartSlot.LeftFoot] = "leftFoot",
            [CharacterPartSlot.RightFoot] = "rightFoot",
        };

    private static readonly IReadOnlyDictionary<CharacterFeatureSlot, string> FeatureNames =
        new Dictionary<CharacterFeatureSlot, string>
        {
            [CharacterFeatureSlot.Eyes] = "eyes",
            [CharacterFeatureSlot.Brows] = "brows",
            [CharacterFeatureSlot.Mouth] = "mouth",
            [CharacterFeatureSlot.TorsoAccent] = "torsoAccent",
        };

    public static CharacterDocument Rename(CharacterDocument document, string displayName)
    {
        ArgumentNullException.ThrowIfNull(document);
        JsonObject root = Root(document);
        SetProperty(root, "displayName", JsonValue.Create(displayName));
        return Decode(root);
    }

    public static CharacterDocument SetPartColor(
        CharacterDocument document,
        CharacterPartSlot slot,
        Rgba32 color)
    {
        JsonObject root = Root(document);
        JsonObject colors = RequiredObject(root, "partColors");
        SetProperty(colors, PartNames[slot], JsonValue.Create(color.ToHex()));
        return Decode(root);
    }

    public static CharacterDocument SetFeatureId(
        CharacterDocument document,
        CharacterFeatureSlot slot,
        string featureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);
        JsonObject feature = Feature(root: Root(document), slot);
        string idProperty = FindProperty(feature, "featureId", "id");
        feature[idProperty] = featureId;
        return Decode(feature.GetRoot().AsObject());
    }

    public static CharacterDocument SetFeatureTransform(
        CharacterDocument document,
        CharacterFeatureSlot slot,
        in NormalizedFeatureTransform transform)
    {
        JsonObject feature = Feature(Root(document), slot);
        SetProperty(feature, "offsetX", JsonValue.Create(transform.OffsetX));
        SetProperty(feature, "offsetY", JsonValue.Create(transform.OffsetY));
        SetProperty(feature, "scale", JsonValue.Create(transform.Scale));
        return Decode(feature.GetRoot().AsObject());
    }

    public static Rgba32 ReadPartColor(CharacterDocument document, CharacterPartSlot slot)
    {
        ArgumentNullException.ThrowIfNull(document);
        CharacterPartColors colors = document.PartColors;
        return slot switch
        {
            CharacterPartSlot.Head => colors.Head,
            CharacterPartSlot.Torso => colors.Torso,
            CharacterPartSlot.LeftHand => colors.LeftHand,
            CharacterPartSlot.RightHand => colors.RightHand,
            CharacterPartSlot.LeftFoot => colors.LeftFoot,
            _ => colors.RightFoot,
        };
    }

    public static string ReadFeatureId(CharacterDocument document, CharacterFeatureSlot slot) =>
        ReadFeature(document, slot).FeatureId;

    public static NormalizedFeatureTransform ReadFeatureTransform(
        CharacterDocument document,
        CharacterFeatureSlot slot)
    {
        CharacterFeatureDocument feature = ReadFeature(document, slot);
        return new NormalizedFeatureTransform(feature.OffsetX, feature.OffsetY, feature.Scale);
    }

    public static Rgba32 ReadFeatureColor(CharacterDocument document, CharacterFeatureSlot slot) =>
        ReadFeature(document, slot).Color;

    private static CharacterFeatureDocument ReadFeature(
        CharacterDocument document,
        CharacterFeatureSlot slot)
    {
        ArgumentNullException.ThrowIfNull(document);
        CharacterFeatureSet features = document.Features;
        return slot switch
        {
            CharacterFeatureSlot.Eyes => features.Eyes,
            CharacterFeatureSlot.Brows => features.Brows,
            CharacterFeatureSlot.Mouth => features.Mouth,
            _ => features.TorsoAccent,
        };
    }

    public static CharacterDocument SetFeatureColor(
        CharacterDocument document,
        CharacterFeatureSlot slot,
        Rgba32 color)
    {
        JsonObject feature = Feature(Root(document), slot);
        SetProperty(feature, "color", JsonValue.Create(color.ToHex()));
        return Decode(feature.GetRoot().AsObject());
    }

    public static CharacterDocument WithIdentity(
        CharacterDocument document,
        Guid id,
        string displayName)
    {
        if (id == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(id));
        JsonObject root = Root(document);
        SetProperty(root, "id", JsonValue.Create(id.ToString("D")));
        SetProperty(root, "displayName", JsonValue.Create(displayName));
        return Decode(root);
    }

    public static string Canonical(CharacterDocument document) =>
        CharacterDocumentPolicy.Serialize(
            CharacterDocumentNormalizer.Normalize(document).Document);

    private static JsonObject Root(CharacterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonNode.Parse(CharacterDocumentPolicy.Serialize(document))?.AsObject()
            ?? throw new InvalidOperationException("Character document serialized to no JSON object.");
    }

    private static JsonObject Feature(JsonObject root, CharacterFeatureSlot slot)
    {
        JsonObject features = RequiredObject(root, "features");
        return RequiredObject(features, FeatureNames[slot]);
    }

    private static CharacterDocument Decode(JsonObject root)
    {
        CharacterDecodeResult decoded = CharacterDocumentPolicy.DecodeAndMigrate(root.ToJsonString());
        if (!decoded.IsSuccess || decoded.Document is null)
        {
            throw new InvalidOperationException(
                decoded.Detail ?? "Edited character document could not be decoded.");
        }

        CharacterNormalizationResult normalized =
            CharacterDocumentNormalizer.Normalize(decoded.Document);
        CharacterValidationResult validation =
            CharacterDocumentValidator.Validate(normalized.Document);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Errors));
        }
        return normalized.Document;
    }

    private static JsonObject RequiredObject(JsonObject owner, string expectedName)
    {
        string property = FindProperty(owner, expectedName);
        return owner[property] as JsonObject
            ?? throw new InvalidOperationException($"Character JSON property '{expectedName}' is not an object.");
    }

    private static void SetProperty(JsonObject owner, string expectedName, JsonNode? value)
    {
        string property = FindProperty(owner, expectedName);
        owner[property] = value;
    }

    private static string FindProperty(JsonObject owner, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            foreach ((string property, JsonNode? _) in owner)
            {
                if (string.Equals(property, candidate, StringComparison.OrdinalIgnoreCase))
                    return property;
            }
        }
        throw new InvalidOperationException(
            $"Character JSON is missing one of [{string.Join(", ", candidates)}].");
    }
}

internal static class CharacterJsonNodeExtensions
{
    public static JsonNode GetRoot(this JsonNode node)
    {
        JsonNode current = node;
        while (current.Parent is not null)
            current = current.Parent;
        return current;
    }
}
