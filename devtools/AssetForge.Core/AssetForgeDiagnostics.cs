using System.Diagnostics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Developer-only AF-15 telemetry. Nothing here participates in canonical output or runtime game
/// state; it exists so Asset Forge can report whether an authoring operation is becoming expensive
/// before content scale turns that into a shipping problem.
/// </summary>
public sealed record AssetForgeGenerationMetrics(
    AssetCategory Category,
    string StableId,
    long ElapsedMilliseconds,
    int VertexCount,
    int TriangleCount,
    int GlbBytes,
    int AlbedoBytes,
    DateTimeOffset RecordedAtUtc);

public sealed record AssetForgeThumbnailCacheMetrics(long Hits, long Misses)
{
    public long Requests => Hits + Misses;
    public double HitRate => Requests == 0 ? 0 : Hits / (double)Requests;
}

public static class AssetForgeDiagnostics
{
    private static readonly object Gate = new();
    private static AssetForgeGenerationMetrics? _lastGeneration;
    private static long _thumbnailHits;
    private static long _thumbnailMisses;

    public static AssetForgeGenerationMetrics? LastGeneration
    {
        get { lock (Gate) return _lastGeneration; }
    }

    public static AssetForgeThumbnailCacheMetrics ThumbnailCache
    {
        get
        {
            lock (Gate) return new AssetForgeThumbnailCacheMetrics(_thumbnailHits, _thumbnailMisses);
        }
    }

    internal static GenerationTimer BeginGeneration(AssetRecipe recipe) => new(recipe, Stopwatch.StartNew());

    internal static void RecordThumbnailCacheHit()
    {
        lock (Gate) _thumbnailHits++;
    }

    internal static void RecordThumbnailCacheMiss()
    {
        lock (Gate) _thumbnailMisses++;
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            _lastGeneration = null;
            _thumbnailHits = 0;
            _thumbnailMisses = 0;
        }
    }

    internal sealed class GenerationTimer : IDisposable
    {
        private readonly AssetRecipe _recipe;
        private readonly Stopwatch _watch;
        private bool _completed;

        public GenerationTimer(AssetRecipe recipe, Stopwatch watch)
        {
            _recipe = recipe;
            _watch = watch;
        }

        public void Complete(GeneratedAsset asset)
        {
            if (_completed) return;
            _completed = true;
            _watch.Stop();
            string stableId = _recipe.AssetFamily == AssetFamily.Environment
                ? _recipe.AssetId
                : _recipe.FeatureId;
            var metrics = new AssetForgeGenerationMetrics(
                _recipe.Category,
                stableId,
                _watch.ElapsedMilliseconds,
                asset.VertexCount,
                asset.TriangleCount,
                asset.GlbBytes.Length,
                asset.AlbedoPng.Length,
                DateTimeOffset.UtcNow);
            lock (Gate) _lastGeneration = metrics;
        }

        public void Dispose()
        {
            if (!_completed) _watch.Stop();
        }
    }
}
