using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Lightweight runtime diagnostics for Asset Forge replacements. This is sampled at low frequency
/// by the paint performance logger; it is never used by gameplay or paint mapping decisions.
/// </summary>
public partial class BuddyVisualRigView
{
    internal readonly record struct GeneratedReplacementDiagnosticsSnapshot(
        int ActiveParts,
        int Vertices,
        int Triangles,
        int CachedPaintMeshes);

    internal GeneratedReplacementDiagnosticsSnapshot CaptureGeneratedReplacementDiagnostics()
    {
        if (!IsInitialized)
            return default;

        int active = 0;
        int vertices = 0;
        int triangles = 0;
        Measure(_topVisual, BuddyPartId.Torso);
        Measure(_shoesVisual, BuddyPartId.LeftFoot);
        Measure(_rightShoesVisual, BuddyPartId.RightFoot);
        return new GeneratedReplacementDiagnosticsSnapshot(
            active,
            vertices,
            triangles,
            GeneratedPaintCaches.Count);

        void Measure(Node3D? visualRoot, BuddyPartId partId)
        {
            if (!IsPartVisualReplaced(partId) || !GodotObject.IsInstanceValid(visualRoot))
                return;
            MeshInstance3D? surface = FindGeneratedReplacementSurface(visualRoot!);
            if (!GodotObject.IsInstanceValid(surface) || surface!.Mesh is not ArrayMesh mesh)
                return;

            active++;
            for (int surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
            {
                if (mesh.SurfaceGetPrimitiveType(surfaceIndex) != Mesh.PrimitiveType.Triangles)
                    continue;
                Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surfaceIndex);
                Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                vertices += positions.Length;
                triangles += indices.Length >= 3 ? indices.Length / 3 : positions.Length / 3;
            }
        }
    }
}
