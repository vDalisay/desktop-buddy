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
            CharacterDocumentValidator.Validate(normalizedDocument);
        if (!validation.IsValid)
        {
            return new CharacterCompileResult(
                null,
                Array.Empty<CharacterCompileWarning>(),
                validation.Errors);
        }

        var warnings = new List<CharacterCompileWarning>();
        CompiledFeatureAppearance eyes = CompileFeature(
            normalizedDocument.Features.Eyes,
            CharacterFeatureSlot.Eyes,
            "features.eyes.featureId",
            catalog,
            warnings);
        CompiledFeatureAppearance brows = CompileFeature(
            normalizedDocument.Features.Brows,
            CharacterFeatureSlot.Brows,
            "features.brows.featureId",
            catalog,
            warnings);
        CompiledFeatureAppearance mouth = CompileFeature(
            normalizedDocument.Features.Mouth,
            CharacterFeatureSlot.Mouth,
            "features.mouth.featureId",
            catalog,
            warnings);
        CompiledFeatureAppearance accent = CompileFeature(
            normalizedDocument.Features.TorsoAccent,
            CharacterFeatureSlot.TorsoAccent,
            "features.torsoAccent.featureId",
            catalog,
            warnings);

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
            eyes,
            brows,
            mouth,
            accent);

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
        string resolved = catalog.Resolve(slot, feature.FeatureId, out bool known);
        if (!known)
        {
            warnings.Add(new CharacterCompileWarning(
                path,
                feature.FeatureId,
                resolved,
                $"Unknown {slot} feature '{feature.FeatureId}' resolved to '{resolved}'."));
        }

        return new CompiledFeatureAppearance(
            resolved,
            new NormalizedFeatureTransform(
                feature.OffsetX,
                feature.OffsetY,
                feature.Scale),
            feature.Color);
    }

    private static CharacterCompileResult Failure(CharacterValidationIssue error) => new(
        null,
        Array.Empty<CharacterCompileWarning>(),
        [error]);
}
