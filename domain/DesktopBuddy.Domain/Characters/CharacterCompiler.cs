using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Characters;

public static class CharacterCompiler
{
    public static CharacterCompileResult Compile(
        CharacterDocument normalizedDocument,
        CharacterFeatureCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(normalizedDocument);
        ArgumentNullException.ThrowIfNull(catalog);

        CharacterNormalizationResult normalization =
            CharacterDocumentNormalizer.Normalize(normalizedDocument);
        if (normalization.Changed)
        {
            return Failure(
                new CharacterValidationIssue(
                    "$",
                    "Character document must be normalized before compilation."));
        }

        CharacterValidationResult validation =
            CharacterDocumentValidator.Validate(normalizedDocument, catalog);
        if (!validation.IsValid)
        {
            return new CharacterCompileResult(
                null,
                Array.Empty<CharacterCompileWarning>(),
                validation.Errors);
        }

        var warnings = new List<CharacterCompileWarning>();
        CharacterFeatureSet features = normalizedDocument.Features;
        CompiledFeatureAppearance face = CompileFeature(features.Face, CharacterFeatureSlot.Face, "features.face.featureId", catalog, warnings);
        CompiledFeatureAppearance hair = CompileFeature(features.Hair, CharacterFeatureSlot.Hair, "features.hair.featureId", catalog, warnings);
        CompiledFeatureAppearance brows = CompileFeature(features.Eyebrows, CharacterFeatureSlot.Brows, "features.eyebrows.featureId", catalog, warnings);
        CompiledFeatureAppearance eyes = CompileFeature(features.Eyes, CharacterFeatureSlot.Eyes, "features.eyes.featureId", catalog, warnings);
        CompiledFeatureAppearance nose = CompileFeature(features.Nose, CharacterFeatureSlot.Nose, "features.nose.featureId", catalog, warnings);
        CompiledFeatureAppearance mouth = CompileFeature(features.Mouth, CharacterFeatureSlot.Mouth, "features.mouth.featureId", catalog, warnings);
        CompiledFeatureAppearance ears = CompileFeature(features.Ears, CharacterFeatureSlot.Ears, "features.ears.featureId", catalog, warnings);
        CompiledFeatureAppearance accessories = CompileFeature(features.Accessories, CharacterFeatureSlot.Accessories, "features.accessories.featureId", catalog, warnings);
        CompiledFeatureAppearance glasses = CompileFeature(features.Glasses, CharacterFeatureSlot.Glasses, "features.glasses.featureId", catalog, warnings);
        CompiledFeatureAppearance headwear = CompileFeature(features.Headwear, CharacterFeatureSlot.Headwear, "features.headwear.featureId", catalog, warnings);
        CompiledFeatureAppearance tops = CompileFeature(features.Tops, CharacterFeatureSlot.Tops, "features.tops.featureId", catalog, warnings);
        CompiledFeatureAppearance shoes = CompileFeature(features.Shoes, CharacterFeatureSlot.Shoes, "features.shoes.featureId", catalog, warnings);

        CharacterPartColors colors = normalizedDocument.PartColors;
        var appearance = new CompiledCharacterAppearance(
            normalizedDocument.Id,
            new PartColorSet(
                colors.Head,
                colors.Torso,
                colors.LeftHand,
                colors.RightHand,
                colors.LeftFoot,
                colors.RightFoot),
            face,
            hair,
            brows,
            eyes,
            nose,
            mouth,
            ears,
            accessories,
            glasses,
            headwear,
            tops,
            shoes);

        return new CharacterCompileResult(
            appearance,
            warnings.ToArray(),
            Array.Empty<CharacterValidationIssue>());
    }

    private static CompiledFeatureAppearance CompileFeature(
        CharacterFeatureDocument feature,
        CharacterFeatureSlot slot,
        string path,
        CharacterFeatureCatalog catalog,
        ICollection<CharacterCompileWarning> warnings)
    {
        CosmeticDefinition definition = catalog.ResolveDefinition(slot, feature.FeatureId, out bool known);
        string resolved = definition.Id;
        if (!known)
        {
            warnings.Add(new CharacterCompileWarning(
                path,
                feature.FeatureId,
                resolved,
                $"Unknown {slot} feature '{feature.FeatureId}' resolved to '{resolved}'."));
        }

        var colors = new List<KeyValuePair<string, Rgba32>>(definition.ColorChannels.Count);
        foreach (CosmeticColorChannelDefinition channel in definition.ColorChannels)
        {
            Rgba32 color = feature.Colors.TryGetValue(channel.Id, out Rgba32 selected)
                ? selected
                : channel.Id == CosmeticDefinition.PrimaryColorChannel
                    ? feature.Color
                    : channel.DefaultColor;
            colors.Add(new KeyValuePair<string, Rgba32>(channel.Id, color));
        }

        bool knownInAnotherSlot = !known && catalog.TryGetDefinition(feature.FeatureId, out _);
        return new CompiledFeatureAppearance(
            resolved,
            knownInAnotherSlot
                ? definition.DefaultTransform
                : new NormalizedFeatureTransform(feature.OffsetX, feature.OffsetY, feature.Scale),
            new CompiledColorChannels(colors));
    }

    private static CharacterCompileResult Failure(CharacterValidationIssue error) => new(
        null,
        Array.Empty<CharacterCompileWarning>(),
        [error]);
}
