using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Persistence.Sharing;

namespace DesktopBuddy.Sharing;

public sealed class CharacterSharePayloadValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly CharacterFeatureCatalog _featureCatalog;

    public CharacterSharePayloadValidator(CharacterFeatureCatalog featureCatalog) =>
        _featureCatalog = featureCatalog ?? throw new ArgumentNullException(nameof(featureCatalog));

    public CharacterSharePayloadResult Validate(ShareFolderReadResult folder)
    {
        if (!folder.IsSuccess || folder.Manifest is null)
            return new CharacterSharePayloadResult(null, folder.Validation);

        var issues = new List<ShareValidationIssue>();
        try
        {
            if (!folder.Files.TryGetValue(ShareManifestPolicy.CharacterFileName, out byte[]? characterBytes))
                return Failure(ShareValidationCode.MissingFile, ShareManifestPolicy.CharacterFileName, "Buddy share is missing character.json.");

            string json = StrictUtf8.GetString(characterBytes);
            CharacterDecodeResult decoded = CharacterDocumentPolicy.DecodeAndMigrate(json);
            if (!decoded.IsSuccess || decoded.Document is null)
            {
                ShareValidationCode code = decoded.Status == CharacterDecodeStatus.UnsupportedFutureVersion
                    ? ShareValidationCode.UnsupportedSchema
                    : ShareValidationCode.InvalidPayload;
                return Failure(code, ShareManifestPolicy.CharacterFileName, decoded.Detail ?? "Character document is invalid.");
            }

            CharacterNormalizationResult normalized = CharacterDocumentNormalizer.Normalize(decoded.Document);
            CharacterValidationResult validation = CharacterDocumentValidator.Validate(normalized.Document, _featureCatalog);
            foreach (CharacterValidationIssue error in validation.Errors)
                issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidPayload, error.Path, error.Message));
            try
            {
                CharacterDocumentPolicy.ValidatePaintManifest(normalized.Document.Paint);
            }
            catch (FormatException exception)
            {
                issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidPayload, "paint", exception.Message));
            }

            var declaredByDocument = normalized.Document.Paint.Declared()
                .ToDictionary(pair => pair.Path, pair => pair.Part, StringComparer.Ordinal);
            HashSet<string> declaredByShare = folder.Manifest.Files
                .Select(file => file.Path)
                .Where(path => !string.Equals(path, ShareManifestPolicy.CharacterFileName, StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);

            foreach (string missing in declaredByDocument.Keys.Where(path => !declaredByShare.Contains(path)))
                issues.Add(new ShareValidationIssue(ShareValidationCode.MissingFile, missing, "Character document declares paint that the share manifest does not contain."));
            foreach (string extra in declaredByShare.Where(path => !declaredByDocument.ContainsKey(path)))
                issues.Add(new ShareValidationIssue(ShareValidationCode.UnexpectedFile, extra, "Share manifest contains paint not declared by character.json."));

            var surfaces = new Dictionary<PaintPart, byte[]>();
            foreach ((string path, PaintPart part) in declaredByDocument)
            {
                if (!folder.Files.TryGetValue(path, out byte[]? encoded))
                    continue;
                try
                {
                    byte[] pixels = PaintPngCodec.Decode(encoded);
                    if (pixels.Length != PaintPolicy.SurfaceBytes)
                        throw new InvalidDataException("Decoded paint surface has the wrong byte count.");
                    surfaces.Add(part, pixels);
                }
                catch (InvalidDataException exception)
                {
                    issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidPayload, path, exception.Message));
                }
            }

            CharacterCompileResult compiled = CharacterCompiler.Compile(normalized.Document, _featureCatalog);
            foreach (CharacterValidationIssue error in compiled.Errors)
                issues.Add(new ShareValidationIssue(ShareValidationCode.InvalidPayload, error.Path, error.Message));

            if (issues.Count > 0 || !compiled.IsSuccess)
                return new CharacterSharePayloadResult(null, new ShareValidationResult(issues));

            return new CharacterSharePayloadResult(
                new CharacterSharePayload(normalized.Document, surfaces, compiled.Warnings),
                ShareValidationResult.Valid);
        }
        catch (Exception exception) when (exception is DecoderFallbackException or InvalidOperationException or ArgumentException)
        {
            return Failure(ShareValidationCode.InvalidPayload, ShareManifestPolicy.CharacterFileName, exception.Message);
        }
    }

    private static CharacterSharePayloadResult Failure(ShareValidationCode code, string path, string message) =>
        new(null, new ShareValidationResult([new ShareValidationIssue(code, path, message)]));
}
