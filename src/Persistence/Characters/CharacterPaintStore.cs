using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Persistence.Characters;

public sealed record CharacterPaintLoadResult(
    CharacterLoadResult Character,
    IReadOnlyDictionary<PaintPart, byte[]> Surfaces,
    string? Detail = null)
{
    public bool IsSuccess => Character.IsSuccess;
}

public sealed record CharacterPaintSaveResult(
    CharacterSaveResult Character,
    string? Detail = null)
{
    public bool IsSuccess => Character.IsSuccess;
}

/// <summary>
/// Paint-aware transaction boundary. A complete staged directory is validated before the
/// active directory is swapped, so character.json never points at missing newly-written PNGs.
/// Native builds offload blocking work; single-threaded browser-WASM executes it inline.
/// </summary>
public sealed class CharacterPaintStore
{
    private const string StagingSuffix = ".paint-staging";
    private const string PreviousSuffix = ".paint-previous";
    private readonly ICharacterFileSystem _fileSystem;
    private readonly CharacterPaths _paths;
    private readonly CharacterStore _documents;

    /// <summary>
    /// Standalone convenience constructor for isolated tools/tests. Production composition should
    /// prefer <see cref="CharacterStore.CreatePaintStore"/> so document validation, filesystem
    /// policy and feature catalog remain identical across document-only and paint-aware loads.
    /// </summary>
    public CharacterPaintStore(ICharacterFileSystem fileSystem, string resolvedRoot)
        : this(
            fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)),
            new CharacterStore(fileSystem, resolvedRoot))
    {
    }

    internal CharacterPaintStore(ICharacterFileSystem fileSystem, CharacterStore documents)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _paths = documents.Paths;
    }

    public Task<CharacterPaintLoadResult> LoadAsync(Guid id, CancellationToken token = default) =>
        PersistenceWork.Run(() => LoadCore(id, token), CancellationToken.None);

    public Task<CharacterPaintSaveResult> SaveAsync(
        CharacterDocument document,
        IReadOnlyDictionary<PaintPart, ReadOnlyMemory<byte>> surfaces,
        CancellationToken token = default) =>
        PersistenceWork.Run(() => SaveCore(document, surfaces, token), CancellationToken.None);

    internal CharacterPaintLoadResult LoadBrowserSynchronously(
        Guid id,
        CancellationToken token = default) =>
        LoadCore(id, token);

    internal CharacterPaintSaveResult SaveBrowserSynchronously(
        CharacterDocument document,
        IReadOnlyDictionary<PaintPart, ReadOnlyMemory<byte>> surfaces,
        CancellationToken token = default)
    {
        BrowserMarker("paint-store-enter");
        CharacterPaintSaveResult result = SaveCore(document, surfaces, token);
        BrowserMarker("paint-store-return");
        return result;
    }

    private CharacterPaintLoadResult LoadCore(Guid id, CancellationToken token)
    {
        CharacterLoadResult character = _documents.LoadForPaint(id, token);
        if (!character.IsSuccess || character.Document is null)
            return new CharacterPaintLoadResult(character, new Dictionary<PaintPart, byte[]>(), character.Detail);

        try
        {
            var surfaces = new Dictionary<PaintPart, byte[]>();
            long aggregate = 0;
            foreach ((PaintPart part, string relative) in character.Document.Paint.Declared())
            {
                token.ThrowIfCancellationRequested();
                if (!PaintPolicy.IsWhitelistedPath(part, relative))
                    throw new InvalidDataException($"Paint path for {part} is not whitelisted.");
                string path = ResolvePaintPath(id, relative);
                if (!_fileSystem.FileExists(path) || _fileSystem.IsReparsePoint(path))
                    throw new InvalidDataException($"Declared paint file for {part} is missing or linked.");
                byte[] encoded = _fileSystem.ReadAllBytes(path, PaintPolicy.MaximumEncodedPngBytes);
                aggregate += encoded.LongLength;
                if (aggregate > PaintPolicy.MaximumAggregateEncodedBytes)
                    throw new InvalidDataException("Aggregate paint payload exceeds 12 MiB.");
                surfaces.Add(part, PaintPngCodec.Decode(encoded));
            }
            return new CharacterPaintLoadResult(character, surfaces);
        }
        catch (OperationCanceledException)
        {
            return new CharacterPaintLoadResult(
                new CharacterLoadResult(CharacterLoadStatus.Cancelled, null),
                new Dictionary<PaintPart, byte[]>());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new CharacterPaintLoadResult(
                new CharacterLoadResult(CharacterLoadStatus.Invalid, null, exception.Message),
                new Dictionary<PaintPart, byte[]>(),
                exception.Message);
        }
    }

    private CharacterPaintSaveResult SaveCore(
        CharacterDocument document,
        IReadOnlyDictionary<PaintPart, ReadOnlyMemory<byte>> surfaces,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(surfaces);
        BrowserMarker("core-enter");
        if (document.Id == Guid.Empty)
            return Failure(CharacterSaveStatus.RejectedPath, "Empty character ID.");

        string active = _paths.Directory(document.Id);
        string staging = active + StagingSuffix;
        string previous = active + PreviousSuffix;
        try
        {
            token.ThrowIfCancellationRequested();
            BrowserMarker("path-check-begin");
            if ((_fileSystem.DirectoryExists(active) && _fileSystem.IsReparsePoint(active)) ||
                (_fileSystem.DirectoryExists(staging) && _fileSystem.IsReparsePoint(staging)))
            {
                return Failure(CharacterSaveStatus.RejectedPath, "Character transaction directory is linked.");
            }
            BrowserMarker("path-check-complete");

            _fileSystem.CreateDirectory(_paths.Root);
            BrowserMarker("root-ready");
            _fileSystem.DeleteDirectory(staging, recursive: true);
            BrowserMarker("staging-cleared");
            _fileSystem.DeleteDirectory(previous, recursive: true);
            BrowserMarker("previous-cleared");
            _fileSystem.CreateDirectory(staging);
            BrowserMarker("staging-ready");
            _fileSystem.CreateDirectory(Path.Combine(staging, "paint"));
            BrowserMarker("paint-dir-ready");

            var nonBlank = new List<PaintPart>();
            long aggregate = 0;
            foreach (PaintPart part in Enum.GetValues<PaintPart>())
            {
                BrowserMarker($"surface-{part}-begin");
                if (!surfaces.TryGetValue(part, out ReadOnlyMemory<byte> memory))
                {
                    BrowserMarker($"surface-{part}-missing");
                    continue;
                }
                if (memory.Length != PaintPolicy.SurfaceBytes)
                    throw new InvalidDataException($"Paint surface for {part} is not 512x512 RGBA8.");
                BrowserMarker($"surface-{part}-scan");
                if (memory.Span.IndexOfAnyExcept((byte)0) < 0)
                {
                    BrowserMarker($"surface-{part}-blank");
                    continue;
                }
                BrowserMarker($"surface-{part}-encode-begin");
                byte[] encoded = PaintPngCodec.Encode(memory.Span);
                BrowserMarker($"surface-{part}-encode-complete");
                aggregate += encoded.LongLength;
                if (aggregate > PaintPolicy.MaximumAggregateEncodedBytes)
                    throw new InvalidDataException("Aggregate paint payload exceeds 12 MiB.");
                string relative = PaintPolicy.WhitelistedPaths[part];
                string target = ResolveUnder(staging, relative);
                _fileSystem.WriteAllBytesDurable(target, encoded);
                BrowserMarker($"surface-{part}-write-complete");
                byte[] verified = _fileSystem.ReadAllBytes(target, PaintPolicy.MaximumEncodedPngBytes);
                BrowserMarker($"surface-{part}-readback-complete");
                if (!PaintPngCodec.Decode(verified).AsSpan().SequenceEqual(memory.Span))
                    throw new InvalidDataException($"Staged paint verification failed for {part}.");
                BrowserMarker($"surface-{part}-verify-complete");
                nonBlank.Add(part);
            }
            BrowserMarker("surfaces-complete");

            CharacterDocument normalized = CharacterDocumentNormalizer.Normalize(document with
            {
                Paint = CharacterPaintManifest.ForNonBlank(nonBlank),
            }).Document;
            BrowserMarker("normalize-complete");
            string json = CharacterDocumentPolicy.Serialize(normalized);
            BrowserMarker("serialize-complete");
            _fileSystem.WriteAllTextDurable(Path.Combine(staging, CharacterPaths.PrimaryFileName), json);
            BrowserMarker("document-write-complete");
            CharacterDecodeResult staged = CharacterDocumentPolicy.DecodeAndMigrate(
                _fileSystem.ReadAllText(Path.Combine(staging, CharacterPaths.PrimaryFileName)));
            BrowserMarker("document-readback-complete");
            if (!staged.IsSuccess || staged.Document is null)
                throw new InvalidDataException(staged.Detail ?? "Staged character document is invalid.");
            foreach ((PaintPart part, string path) in staged.Document.Paint.Declared())
            {
                if (!PaintPolicy.IsWhitelistedPath(part, path) || !_fileSystem.FileExists(ResolveUnder(staging, path)))
                    throw new InvalidDataException($"Staged paint reference for {part} is invalid.");
            }
            BrowserMarker("manifest-verify-complete");

            token.ThrowIfCancellationRequested();
            if (_fileSystem.DirectoryExists(active))
            {
                BrowserMarker("active-move-previous-begin");
                _fileSystem.MoveDirectory(active, previous);
                BrowserMarker("active-move-previous-complete");
            }
            try
            {
                BrowserMarker("staging-move-active-begin");
                _fileSystem.MoveDirectory(staging, active);
                BrowserMarker("staging-move-active-complete");
                _fileSystem.DeleteDirectory(previous, recursive: true);
                BrowserMarker("previous-delete-complete");
            }
            catch
            {
                if (!_fileSystem.DirectoryExists(active) && _fileSystem.DirectoryExists(previous))
                    _fileSystem.MoveDirectory(previous, active);
                throw;
            }

            BrowserMarker("core-success");
            return new CharacterPaintSaveResult(
                new CharacterSaveResult(CharacterSaveStatus.Saved, normalized));
        }
        catch (OperationCanceledException)
        {
            SafeDelete(staging);
            return Failure(CharacterSaveStatus.Cancelled, "Paint save was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SafeDelete(staging);
            return Failure(CharacterSaveStatus.IoFailure, exception.Message);
        }
    }

    private string ResolvePaintPath(Guid id, string relative) => ResolveUnder(_paths.Directory(id), relative);

    private static string ResolveUnder(string root, string relative)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Paint path escaped the character directory.");
        return full;
    }

    private CharacterPaintSaveResult Failure(CharacterSaveStatus status, string detail) =>
        new(new CharacterSaveResult(status, null, detail), detail);

    private void SafeDelete(string path)
    {
        try { _fileSystem.DeleteDirectory(path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void BrowserMarker(string stage)
    {
        if (OperatingSystem.IsBrowser())
            GD.Print($"DESKTOP_BUDDY_WEB_PAINT_SAVE_STAGE:{stage}");
    }
}
