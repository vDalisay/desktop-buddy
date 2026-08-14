using System.Numerics;
using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class PartReplacementSurfacePolisherTests
{
    [Fact]
    public void Surface_polish_preserves_authored_outline_uvs_and_topology_while_repairing_rim_normals()
    {
        RgbaImage source = Ellipse(511, 503, 193, 251);
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
            PartReplacementContourInflationGenerator.Generate(
                source,
                geometry,
                AssetCategory.TorsoShape));

        Vector3[] beforePositions = mesh.Positions.ToArray();
        Vector2[] beforeUvs = mesh.Uvs.ToArray();
        uint[] beforeIndices = mesh.Indices.ToArray();
        bool[] boundary = beforePositions
            .Select(static position => MathF.Abs(position.Z) <= 0.00001f)
            .ToArray();

        PartReplacementSurfacePolisher.Apply(mesh);

        Assert.Equal(beforeIndices, mesh.Indices);
        Assert.Equal(beforeUvs, mesh.Uvs);
        Assert.Equal(beforePositions.Length, mesh.Positions.Count);

        float maximumZDelta = 0f;
        for (int index = 0; index < mesh.Positions.Count; index++)
        {
            Vector3 before = beforePositions[index];
            Vector3 after = mesh.Positions[index];
            Assert.Equal(before.X, after.X);
            Assert.Equal(before.Y, after.Y);
            maximumZDelta = MathF.Max(maximumZDelta, MathF.Abs(after.Z - before.Z));
            if (boundary[index])
                Assert.Equal(0f, after.Z);

            Vector3 normal = mesh.Normals[index];
            Assert.True(float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z));
            Assert.InRange(normal.Length(), 0.999f, 1.001f);
        }

        Assert.True(maximumZDelta < 0.02f,
            $"Rim polish moved depth too aggressively ({maximumZDelta:F6} world units).");
        AssertBoundaryNormalsFollowContour(mesh, boundary);
    }

    [Fact]
    public void Compiler_output_keeps_contour_polish_deterministic()
    {
        RgbaImage source = Ellipse(512, 520, 205, 155);
        AssetRecipe recipe = AssetRecipe.TorsoShapeDefaults() with
        {
            FeatureId = "top.surface_polish_test",
            ContentId = "cosmetic.top.surface_polish_test",
            DisplayName = "Surface Polish Test",
            Geometry = AssetRecipe.TorsoShapeDefaults().Geometry with
            {
                GeometryResolution = 128,
                RuntimeTextureResolution = 128,
                SurfaceSmoothness = 1.0,
                Roundness = 0.9,
                ShapeMode = ShapeMode.InflatedSolid,
                SymmetryMode = SymmetryMode.Off,
                ThicknessBiasPixels = 0,
            },
        };
        byte[] png = PngCodec.EncodeRgba8(source);

        GeneratedAsset first = AssetForgeCompiler.Generate(png, recipe);
        GeneratedAsset second = AssetForgeCompiler.Generate(png, recipe);

        Assert.Equal(first.GeometryHash, second.GeometryHash);
        Assert.Equal(first.GlbBytes, second.GlbBytes);
        bool[] boundary = first.Mesh.Positions
            .Select(static position => MathF.Abs(position.Z) <= 0.00001f)
            .ToArray();
        AssertBoundaryNormalsFollowContour(first.Mesh, boundary);
    }

    private static void AssertBoundaryNormalsFollowContour(CanonicalMesh mesh, IReadOnlyList<bool> boundary)
    {
        var neighbours = new HashSet<int>[mesh.Positions.Count];
        for (int index = 0; index < neighbours.Length; index++)
            neighbours[index] = [];

        for (int triangle = 0; triangle < mesh.Indices.Count; triangle += 3)
        {
            int a = checked((int)mesh.Indices[triangle]);
            int b = checked((int)mesh.Indices[triangle + 1]);
            int c = checked((int)mesh.Indices[triangle + 2]);
            Add(a, b); Add(b, c); Add(c, a);
        }

        int checkedBoundary = 0;
        for (int index = 0; index < boundary.Count; index++)
        {
            if (!boundary[index])
                continue;
            int[] contour = neighbours[index].Where(neighbour => boundary[neighbour]).ToArray();
            Assert.Equal(2, contour.Length);
            Vector2 tangent = ToXy(mesh.Positions[contour[1]]) - ToXy(mesh.Positions[contour[0]]);
            if (tangent.LengthSquared() <= 0.000000000001f)
                continue;
            tangent = Vector2.Normalize(tangent);
            Vector3 normal = mesh.Normals[index];
            Vector2 normalXy = Vector2.Normalize(new Vector2(normal.X, normal.Y));
            Assert.True(MathF.Abs(Vector2.Dot(tangent, normalXy)) < 0.06f,
                $"Boundary normal {normal} is not perpendicular to contour tangent {tangent} at vertex {index}.");
            Assert.True(MathF.Abs(normal.Z) < 0.001f,
                $"Boundary normal should remain in the silhouette plane; got {normal}.");
            checkedBoundary++;
        }
        Assert.True(checkedBoundary > 100);
        return;

        void Add(int a, int b)
        {
            if (!boundary[a] || !boundary[b])
                return;
            neighbours[a].Add(b);
            neighbours[b].Add(a);
        }
    }

    private static Vector2 ToXy(Vector3 value) => new(value.X, value.Y);

    private static RgbaImage Ellipse(int cx, int cy, int rx, int ry)
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        for (int y = Math.Max(0, cy - ry - 1); y <= Math.Min(1023, cy + ry + 1); y++)
        for (int x = Math.Max(0, cx - rx - 1); x <= Math.Min(1023, cx + rx + 1); x++)
        {
            double nx = (x - cx) / (double)rx;
            double ny = (y - cy) / (double)ry;
            if (nx * nx + ny * ny > 1.0)
                continue;
            int index = ((y * 1024) + x) * 4;
            pixels[index] = 54;
            pixels[index + 1] = 150;
            pixels[index + 2] = 225;
            pixels[index + 3] = 255;
        }
        return new RgbaImage(1024, 1024, pixels);
    }
}
