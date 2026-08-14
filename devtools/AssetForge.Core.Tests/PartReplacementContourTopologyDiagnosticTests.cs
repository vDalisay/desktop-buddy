using System.Numerics;
using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class PartReplacementContourTopologyDiagnosticTests
{
    [Fact]
    public void Contour_topology_has_no_open_edges_with_position_diagnostics()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        FillEllipse(pixels, 512, 370, 153, 185);
        FillEllipse(pixels, 512, 610, 250, 190);
        var source = new RgbaImage(1024, 1024, pixels);
        GeometrySettings geometry = AssetRecipe.TorsoShapeDefaults().Geometry with
        {
            GeometryResolution = 128,
            RuntimeTextureResolution = 128,
            SurfaceSmoothness = 1.0,
            Roundness = 0.9,
            ShapeMode = ShapeMode.InflatedSolid,
            SymmetryMode = SymmetryMode.Off,
            ThicknessBiasPixels = 0,
        };
        CanonicalMesh mesh = PartReplacementTopologyWeld.Apply(
            PartReplacementContourInflationGenerator.Generate(source, geometry, AssetCategory.TorsoShape));

        var edges = new Dictionary<(uint A, uint B), int>();
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            Add(mesh.Indices[i], mesh.Indices[i + 1]);
            Add(mesh.Indices[i + 1], mesh.Indices[i + 2]);
            Add(mesh.Indices[i + 2], mesh.Indices[i]);
        }

        var open = edges.Where(pair => pair.Value != 2).Take(12).ToArray();
        if (open.Length == 0)
            return;

        string details = string.Join("\n", open.Select(pair =>
        {
            int a = checked((int)pair.Key.A);
            int b = checked((int)pair.Key.B);
            Vector3 pa = mesh.Positions[a];
            Vector3 pb = mesh.Positions[b];
            Vector2 ua = mesh.Uvs[a];
            Vector2 ub = mesh.Uvs[b];
            return $"edge {pair.Key.A}-{pair.Key.B} count={pair.Value}: pA={pa} uvA={ua}; pB={pb} uvB={ub}";
        }));
        Assert.Fail($"Found {edges.Count(pair => pair.Value != 2)} non-manifold/open edges. First entries:\n{details}");
        return;

        void Add(uint a, uint b)
        {
            (uint A, uint B) key = a < b ? (a, b) : (b, a);
            edges[key] = edges.GetValueOrDefault(key) + 1;
        }
    }

    private static void FillEllipse(byte[] pixels, int cx, int cy, int rx, int ry)
    {
        for (int y = Math.Max(0, cy - ry - 1); y <= Math.Min(1023, cy + ry + 1); y++)
        for (int x = Math.Max(0, cx - rx - 1); x <= Math.Min(1023, cx + rx + 1); x++)
        {
            double nx = (x - cx) / (double)rx;
            double ny = (y - cy) / (double)ry;
            if (nx * nx + ny * ny > 1.0)
                continue;
            int i = ((y * 1024) + x) * 4;
            pixels[i] = 54;
            pixels[i + 1] = 150;
            pixels[i + 2] = 225;
            pixels[i + 3] = 255;
        }
    }
}
