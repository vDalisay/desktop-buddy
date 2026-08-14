using System.Globalization;
using System.IO;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Low-frequency diagnostics for real local Paint Buddy playtests. The game is normally launched
/// through play_game.bat rather than the Godot editor profiler, so this records enough CPU/render
/// and generated-mesh workload data to diagnose the next bottleneck from an uploaded log alone.
/// </summary>
public partial class CharacterEditorHost
{
    private const double PaintPerformanceSampleSeconds = 1.0;
    private const string PaintPerformanceRelativePath = "user://diagnostics/paint-performance-latest.csv";
    private double _paintPerformanceAccumulator;
    private bool _paintPerformanceLogStarted;
    private long _previousPaintRaycasts;
    private long _previousPaintBvhNodes;
    private long _previousPaintTriangleTests;
    private int _previousPaintTextureUploads;

    public override void _PhysicsProcess(double delta)
    {
        if (!IsInitialized || !IsPaintMode || !GodotObject.IsInstanceValid(_preview))
        {
            _paintPerformanceAccumulator = 0.0;
            return;
        }

        _paintPerformanceAccumulator += Math.Max(0.0, delta);
        if (_paintPerformanceAccumulator < PaintPerformanceSampleSeconds)
            return;

        double elapsed = _paintPerformanceAccumulator;
        _paintPerformanceAccumulator = 0.0;
        EnsurePaintPerformanceLog();

        Buddy.Presentation3D.BuddyVisualRigView.GeneratedReplacementDiagnosticsSnapshot replacement =
            _preview.CaptureGeneratedReplacementDiagnostics();
        long raycasts = replacement.Raycasts - _previousPaintRaycasts;
        long bvhNodes = replacement.BvhNodeVisits - _previousPaintBvhNodes;
        long triangleTests = replacement.TriangleTests - _previousPaintTriangleTests;
        int textureUploads = GodotObject.IsInstanceValid(_paintCanvas) && _paintTextures is not null
            ? _paintTextures.UploadCount
            : 0;
        int uploadDelta = Math.Max(0, textureUploads - _previousPaintTextureUploads);
        _previousPaintRaycasts = replacement.Raycasts;
        _previousPaintBvhNodes = replacement.BvhNodeVisits;
        _previousPaintTriangleTests = replacement.TriangleTests;
        _previousPaintTextureUploads = textureUploads;

        double fps = Monitor(0);                 // TIME_FPS
        double processMs = Monitor(1) * 1000.0; // TIME_PROCESS
        double physicsMs = Monitor(2) * 1000.0; // TIME_PHYSICS_PROCESS
        double staticMemoryMb = Monitor(4) / (1024.0 * 1024.0); // MEMORY_STATIC
        double objectCount = Monitor(7);         // OBJECT_COUNT
        double nodeCount = Monitor(9);           // OBJECT_NODE_COUNT
        double renderObjects = Monitor(11);      // RENDER_TOTAL_OBJECTS_IN_FRAME
        double renderPrimitives = Monitor(12);   // RENDER_TOTAL_PRIMITIVES_IN_FRAME
        double drawCalls = Monitor(13);          // RENDER_TOTAL_DRAW_CALLS_IN_FRAME
        double managedMemoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

        string line = string.Join(',',
            Csv(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)),
            F(fps),
            F(processMs),
            F(physicsMs),
            F(objectCount),
            F(nodeCount),
            F(renderObjects),
            F(renderPrimitives),
            F(drawCalls),
            F(staticMemoryMb),
            F(managedMemoryMb),
            GC.CollectionCount(0).ToString(CultureInfo.InvariantCulture),
            GC.CollectionCount(1).ToString(CultureInfo.InvariantCulture),
            GC.CollectionCount(2).ToString(CultureInfo.InvariantCulture),
            replacement.ActiveParts.ToString(CultureInfo.InvariantCulture),
            replacement.Vertices.ToString(CultureInfo.InvariantCulture),
            replacement.Triangles.ToString(CultureInfo.InvariantCulture),
            replacement.CachedPaintMeshes.ToString(CultureInfo.InvariantCulture),
            F(raycasts / elapsed),
            F(bvhNodes / elapsed),
            F(triangleTests / elapsed),
            uploadDelta.ToString(CultureInfo.InvariantCulture));

        try
        {
            File.AppendAllText(ProjectSettings.GlobalizePath(PaintPerformanceRelativePath), line + Environment.NewLine);
        }
        catch (IOException exception)
        {
            GD.PushWarning($"[PaintPerf] Could not append diagnostics: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            GD.PushWarning($"[PaintPerf] Could not append diagnostics: {exception.Message}");
        }
    }

    private void EnsurePaintPerformanceLog()
    {
        if (_paintPerformanceLogStarted)
            return;
        _paintPerformanceLogStarted = true;

        string absolutePath = ProjectSettings.GlobalizePath(PaintPerformanceRelativePath);
        try
        {
            string? directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(absolutePath,
                "timestamp,fps,process_ms,physics_ms,objects,nodes,render_objects,render_primitives,draw_calls," +
                "static_memory_mb,managed_memory_mb,gc0,gc1,gc2,replacement_parts,replacement_vertices," +
                "replacement_triangles,paint_cache_meshes,paint_raycasts_per_s,bvh_nodes_per_s," +
                "triangle_tests_per_s,paint_texture_uploads_per_sample" + Environment.NewLine);
            GD.Print($"[PaintPerf] Writing Paint Buddy diagnostics to: {absolutePath}");
        }
        catch (IOException exception)
        {
            GD.PushWarning($"[PaintPerf] Could not create diagnostics log: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            GD.PushWarning($"[PaintPerf] Could not create diagnostics log: {exception.Message}");
        }

        Buddy.Presentation3D.BuddyVisualRigView.GeneratedReplacementDiagnosticsSnapshot replacement =
            _preview.CaptureGeneratedReplacementDiagnostics();
        _previousPaintRaycasts = replacement.Raycasts;
        _previousPaintBvhNodes = replacement.BvhNodeVisits;
        _previousPaintTriangleTests = replacement.TriangleTests;
        _previousPaintTextureUploads = _paintTextures?.UploadCount ?? 0;
    }

    private static double Monitor(int monitor) =>
        Performance.GetMonitor((Performance.Monitor)monitor);

    private static string F(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Csv(string value) =>
        '"' + value.Replace("\"", "\"\"") + '"';
}
