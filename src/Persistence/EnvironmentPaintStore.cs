using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Failure-safe local store for the painted room background: one whitelisted 512x512 RGBA8 PNG
/// written through a staging file. Load never throws — a missing, oversized, linked or corrupt
/// file simply leaves the room at its blank default.
/// </summary>
public sealed class EnvironmentPaintStore
{
    private const string TemporarySuffix = ".tmp";
    private readonly ICharacterFileSystem _fileSystem;

    public EnvironmentPaintStore(ICharacterFileSystem fileSystem, string resolvedRoot)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        if (string.IsNullOrWhiteSpace(resolvedRoot))
            throw new ArgumentException("A resolved environment root is required.", nameof(resolvedRoot));
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedRoot));
    }

    public string Root { get; }

    public string PaintPath => Path.GetFullPath(Path.Combine(Root, EnvironmentCanvasPolicy.RelativePath));

    public Task SaveAsync(ReadOnlyMemory<byte> pixels, CancellationToken token = default) =>
        PersistenceWork.Run(() => SaveCore(pixels.Span), CancellationToken.None);

    public Task<byte[]?> LoadAsync(CancellationToken token = default) =>
        PersistenceWork.Run(Load, CancellationToken.None);

    /// <summary>Returns the stored 512x512 RGBA8 pixels, or null when there is nothing usable.</summary>
    public byte[]? Load()
    {
        try
        {
            string path = PaintPath;
            if (!_fileSystem.FileExists(path) || _fileSystem.IsReparsePoint(path)) return null;
            byte[] encoded = _fileSystem.ReadAllBytes(path, PaintPolicy.MaximumEncodedPngBytes);
            byte[] pixels = PaintPngCodec.Decode(encoded);
            if (pixels.Length != EnvironmentCanvasPolicy.Bytes) return null;
            MigrateOpaqueBaseToBlank(pixels);
            return pixels;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public void Delete()
    {
        try { _fileSystem.DeleteFile(PaintPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Rooms painted before the canvas floated above the wallpaper stored their unpainted area as
    /// opaque base grey, which would now hide any wallpaper. Those pixels become blank again; the
    /// player's strokes are untouched.
    /// </summary>
    private static void MigrateOpaqueBaseToBlank(byte[] pixels)
    {
        EnvironmentColor baseColor = EnvironmentCanvasPolicy.DefaultColor;
        for (int index = 0; index < pixels.Length; index += EnvironmentCanvasPolicy.BytesPerPixel)
        {
            if (pixels[index] != baseColor.Red || pixels[index + 1] != baseColor.Green ||
                pixels[index + 2] != baseColor.Blue || pixels[index + 3] != byte.MaxValue) continue;
            pixels[index] = 0;
            pixels[index + 1] = 0;
            pixels[index + 2] = 0;
            pixels[index + 3] = 0;
        }
    }

    private void SaveCore(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != EnvironmentCanvasPolicy.Bytes)
            throw new ArgumentException("The room canvas must be exactly 512x512 RGBA8.", nameof(pixels));
        string path = PaintPath;
        string directory = Path.GetDirectoryName(path) ?? Root;
        _fileSystem.CreateDirectory(directory);
        byte[] encoded = PaintPngCodec.Encode(pixels);
        string temporary = path + TemporarySuffix;
        _fileSystem.WriteAllBytesDurable(temporary, encoded);
        if (_fileSystem.FileExists(path)) _fileSystem.DeleteFile(path);
        _fileSystem.MoveFile(temporary, path);
    }
}
