using System.Globalization;
using System.Text;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Shared AF-14 cache boundary. Thumbnail identity is deliberately independent from preview UI
/// state: it derives from generated geometry, generated texture and the canonical thumbnail recipe.
/// Both Buddy and Environment exporters can therefore use the same cache contract even though the
/// Buddy producer is a Godot render and the Environment producer is a pure CPU crop.
/// </summary>
public static class AssetThumbnailCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, byte[]> Memory = new(StringComparer.Ordinal);

    public static string ThumbnailRecipeHash(ThumbnailSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string canonical = string.Join("\n",
            settings.YawDegrees.ToString("R", CultureInfo.InvariantCulture),
            settings.PitchDegrees.ToString("R", CultureInfo.InvariantCulture),
            settings.Padding.ToString("R", CultureInfo.InvariantCulture),
            EnvironmentThumbnailGenerator.OutputSize.ToString(CultureInfo.InvariantCulture));
        return Hashing.Sha256Hex(Encoding.UTF8.GetBytes(canonical));
    }

    public static string KeyFor(GeneratedAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string payload = string.Join("\n",
            asset.GeometryHash,
            asset.AlbedoHash,
            ThumbnailRecipeHash(asset.Recipe.Thumbnail));
        return Hashing.Sha256Hex(Encoding.UTF8.GetBytes(payload));
    }

    public static byte[] GetOrCreate(GeneratedAsset asset, Func<byte[]> producer)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(producer);
        string key = KeyFor(asset);
        lock (Gate)
        {
            if (Memory.TryGetValue(key, out byte[]? cached))
                return (byte[])cached.Clone();
        }

        byte[] produced = producer() ?? throw new InvalidOperationException("Thumbnail producer returned null.");
        ValidateThumbnail(produced);
        byte[] stable = (byte[])produced.Clone();
        lock (Gate)
        {
            if (!Memory.TryGetValue(key, out byte[]? winner))
            {
                Memory.Add(key, stable);
                winner = stable;
            }
            return (byte[])winner.Clone();
        }
    }

    public static void ClearMemoryCache()
    {
        lock (Gate) Memory.Clear();
    }

    private static void ValidateThumbnail(ReadOnlySpan<byte> png)
    {
        RgbaImage image = PngCodec.DecodeRgba8(png);
        if (image.Width != EnvironmentThumbnailGenerator.OutputSize || image.Height != EnvironmentThumbnailGenerator.OutputSize)
            throw new InvalidOperationException(
                $"Asset Forge thumbnails must be exactly {EnvironmentThumbnailGenerator.OutputSize}x{EnvironmentThumbnailGenerator.OutputSize} RGBA PNG; found {image.Width}x{image.Height}.");
    }
}
