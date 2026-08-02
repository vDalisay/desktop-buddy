using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DesktopBuddy.Domain.Characters;

public static class CharacterDocumentValidator
{
    private const string ForbiddenNameCharacters = "\\/:*?\"<>|";

    public static CharacterValidationResult Validate(CharacterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<CharacterValidationIssue>();

        if (document.SchemaVersion != CharacterDocumentPolicy.CurrentSchemaVersion)
            Add(errors, "schemaVersion", "Character schema is not current.");

        if (document.Id == Guid.Empty)
            Add(errors, "id", "Character ID must be a non-empty GUID.");

        ValidateName(document.DisplayName, errors);

        if (document.PartColors is null)
            Add(errors, "partColors", "Part colors are required.");
        if (document.Features is null)
            Add(errors, "features", "Features are required.");
        if (document.ExtensionData is null)
            Add(errors, "$", "Extension data collection is required.");

        if (document.Features is not null)
        {
            ValidateFeature(document.Features.Eyes, "features.eyes", errors);
            ValidateFeature(document.Features.Brows, "features.brows", errors);
            ValidateFeature(document.Features.Mouth, "features.mouth", errors);
            ValidateFeature(document.Features.TorsoAccent, "features.torsoAccent", errors);
        }

        return new CharacterValidationResult(errors.ToArray());
    }

    private static void ValidateName(
        string? displayName,
        ICollection<CharacterValidationIssue> errors)
    {
        if (displayName is null)
        {
            Add(errors, "displayName", "Display name is required.");
            return;
        }

        if (!string.Equals(displayName, displayName.Trim(), StringComparison.Ordinal))
            Add(errors, "displayName", "Display name must already be trimmed.");

        int scalarCount = 0;
        ReadOnlySpan<char> remaining = displayName.AsSpan();
        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(
                remaining,
                out Rune rune,
                out int consumed);
            if (status != OperationStatus.Done)
            {
                Add(errors, "displayName", "Display name contains invalid Unicode.");
                return;
            }

            scalarCount++;
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or
                UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator ||
                (rune.Value == '\r' || rune.Value == '\n') ||
                (rune.Value <= 0x7F && ForbiddenNameCharacters.IndexOf((char)rune.Value) >= 0))
            {
                Add(errors, "displayName", "Display name contains a forbidden character.");
                break;
            }

            remaining = remaining[consumed..];
        }

        if (scalarCount is < 1 or > 40)
        {
            Add(
                errors,
                "displayName",
                "Display name must contain between 1 and 40 Unicode scalar values.");
        }
    }

    private static void ValidateFeature(
        CharacterFeatureDocument? feature,
        string path,
        ICollection<CharacterValidationIssue> errors)
    {
        if (feature is null)
        {
            Add(errors, path, "Feature is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(feature.FeatureId))
            Add(errors, $"{path}.featureId", "Feature ID is required.");

        ValidateFiniteBounded(
            feature.OffsetX,
            NormalizedFeatureTransform.MinimumOffset,
            NormalizedFeatureTransform.MaximumOffset,
            $"{path}.offsetX",
            errors);
        ValidateFiniteBounded(
            feature.OffsetY,
            NormalizedFeatureTransform.MinimumOffset,
            NormalizedFeatureTransform.MaximumOffset,
            $"{path}.offsetY",
            errors);
        ValidateFiniteBounded(
            feature.Scale,
            NormalizedFeatureTransform.MinimumScale,
            NormalizedFeatureTransform.MaximumScale,
            $"{path}.scale",
            errors);
    }

    private static void ValidateFiniteBounded(
        double value,
        double minimum,
        double maximum,
        string path,
        ICollection<CharacterValidationIssue> errors)
    {
        if (!double.IsFinite(value))
        {
            Add(errors, path, "Value must be finite.");
            return;
        }

        if (value < minimum || value > maximum)
            Add(errors, path, $"Value must be within [{minimum}, {maximum}].");
    }

    private static void Add(
        ICollection<CharacterValidationIssue> errors,
        string path,
        string message) => errors.Add(new CharacterValidationIssue(path, message));
}
