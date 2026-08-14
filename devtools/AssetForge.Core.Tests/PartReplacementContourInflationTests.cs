using System.Numerics;
using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class PartReplacementContourInflationTests
{
    [Theory]
    [InlineData(AssetCategory.TorsoShape)]
    [InlineData(AssetCategory.FootShape)]
    public void Default_inflated_replacement_is_closed_round_shell_without_cross_side_triangles(AssetCategory category)
    {
        AssetRecipe recipe = category == AssetCategory.TorsoShape
            ? AssetRecipe.TorsoShapeDefaults()
            : AssetRecipe.FootShapeDefaults();
        GeometrySettings geometry = recipe.Geometry with
        {
            GeometryResolution = 128,
            RuntimeTextureResolution = 128,
            SurfaceSmoothness = 1.0,
            Roundness = 0.9,
            ShapeMode = ShapeMode.InflatedSolid,
            SymmetryMode = SymmetryMode.Off,
            ThicknessBiasPixels = 0,
        };
        RgbaImage source = category == AssetCategory.TorsoShape
            ? TwoCircleTorso()
            : Ellipse(512, 520, 205, 155);

        CanonicalMesh mesh = PartReplacementContourInflationGenerator.Generate(source, geometry, category);

        Assert.True(mesh.TriangleCount > 1_000);
        Assert.True(mesh.TriangleCount < 20_000,
            $"Contour-conforming {category} produced {mesh.TriangleCount:N0} triangles; runtime target is <20k.");
        AssertWatertight(mesh);

        int boundaryVertices = 0;
        foreach ((Vector3 position, Vector3 normal) in mesh.Positions.Zip(mesh.Normals))
        {
            Assert.True(float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z));
            Assert.True(float.IsFinite(normal.X) && float.IsFinite(normal.Y) && float.IsFinite(normal.Z));
            Assert.InRange(normal.Length(), 0.999f, 1.001f);
            if (MathF.Abs(position.Z) > 0.00001f)
                continue;
            boundaryVertices++;
            Assert.True(MathF.Abs(normal.Z) < 0.001f,
                $"Shared rounded-shell boundary normal should be tangent to Z; got {normal}.");
        }
        Assert.True(boundaryVertices > 100,
            $"Expected a genuine shared sub-pixel contour, found only {boundaryVertices} boundary vertices.");

        for (int triangle = 0; triangle < mesh.Indices.Count; triangle += 3)
        {
            float za = mesh.Positions[checked((int)mesh.Indices[triangle])].Z;
            float zb = mesh.Positions[checked((int)mesh.Indices[triangle + 1])].Z;
            float zc = mesh.Positions[checked((int)mesh.Indices[triangle + 2])].Z;
            bool hasFront = za > 0.00001f || zb > 0.00001f || zc > 0.00001f;
            bool hasBack = za < -0.00001f || zb < -0.00001f || zc < -0.00001f;
            Assert.False(hasFront && hasBack,
                "InflatedSolid should close by sharing the Z=0 contour, not by adding an explicit front-to-back side triangle.");
        }
    }

    [Fact]
    public void Clean_ellipse_uses_full_resolution_boundary_and_avoids_pathological_cut_slivers()
    {
        const int cx = 511;
        const int cy = 503;
        const int rx = 193;
        const int ry = 251;
        RgbaImage source = Ellipse(cx, cy, rx, ry);
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

        CanonicalMesh mesh = PartReplacementContourInflationGenerator.Generate(
            source,
            geometry,
            AssetCategory.TorsoShape);

        double error = MeanEllipseBoundaryErrorPixels(mesh, cx, cy, rx, ry);
        Assert.True(error < 1.5,
            $"Expected the rendered Z=0 rim to track the 1024px source within ~1.5px on average; measured {error:F3}px.");

        double[] qualities = TriangleQualities(mesh).Order().ToArray();
        Assert.NotEmpty(qualities);
        Assert.True(qualities[0] > 0.008,
            $"Contour clipping emitted an extreme sliver (minimum normalized quality {qualities[0]:F6}).");
        int percentileIndex = Math.Clamp((int)Math.Floor((qualities.Length - 1) * 0.01), 0, qualities.Length - 1);
        Assert.True(qualities[percentileIndex] > 0.02,
            $"The lowest 1% of triangles are too irregular for stable smooth shading (q1={qualities[percentileIndex]:F6}).");
    }

    [Fact]
    public void Contour_inflation_is_deterministic_and_compiler_selects_it_for_default_torso()
    {
        RgbaImage source = TwoCircleTorso();
        AssetRecipe recipe = AssetRecipe.TorsoShapeDefaults() with
        {
            FeatureId = "top.contour_inflation_test",
            ContentId = "cosmetic.top.contour_inflation_test",
            DisplayName = "Contour Inflation Test",
            Geometry = AssetRecipe.TorsoShapeDefaults().Geometry with
            {
                GeometryResolution = 128,
                RuntimeTextureResolution = 128,
            },
        };
        byte[] png = PngCodec.EncodeRgba8(source);

        GeneratedAsset a = AssetForgeCompiler.Generate(png, recipe);
        GeneratedAsset b = AssetForgeCompiler.Generate(png, recipe);

        Assert.Equal(a.GeometryHash, b.GeometryHash);
        Assert.Equal(a.GlbBytes, b.GlbBytes);
        Assert.True(a.Mesh.Positions.Count(position => MathF.Abs(position.Z) <= 0.00001f) > 100);
        Assert.DoesNotContain(a.Mesh.Indices.Chunk(3), triangle =>
        {
            float za = a.Mesh.Positions[checked((int)triangle[0])].Z;
            float zb = a.Mesh.Positions[checked((int)triangle[1])].Z;
            float zc = a.Mesh.Positions[checked((int)triangle[2])].Z;
            return (za > 0.00001f || zb > 0.00001f || zc > 0.00001f) &&
                   (za < -0.00001f || zb < -0.00001f || zc < -0.00001f);
        });
    }

    [Fact]
    public void Transformed_or_legacy_profiles_stay_on_established_generator_path()
    {
        GeometrySettings basis = AssetRecipe.TorsoShapeDefaults().Geometry;
        Assert.True(PartReplacementContourInflationGenerator.CanGenerate(basis));
        Assert.False(PartReplacementContourInflationGenerator.CanGenerate(basis with { SurfaceSmoothness = 0.0 }));
        Assert.False(PartReplacementContourInflationGenerator.CanGenerate(basis with { ThicknessBiasPixels = 1 }));
        Assert.False(PartReplacementContourInflationGenerator.CanGenerate(basis with { SymmetryMode = SymmetryMode.AverageBothSides }));
        Assert.False(PartReplacementContourInflationGenerator.CanGenerate(basis with { ShapeMode = ShapeMode.RoundedExtrusion }));
    }

    private static void AssertWatertight(CanonicalMesh mesh)
    {
        var edges = new Dictionary<(uint A, uint B), int>();
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            Add(mesh.Indices[i], mesh.Indices[i + 1]);
            Add(mesh.Indices[i + 1], mesh.Indices[i + 2]);
            Add(mesh.Indices[i + 2], mesh.Indices[i]);
        }
        Assert.All(edges, pair => Assert.Equal(2, pair.Value));
        return;

        void Add(uint a, uint b)
        {
            (uint A, uint B) key = a < b ? (a, b) : (b, a);
            edges[key] = edges.GetValueOrDefault(key) + 1;
        }
    }

    private static IEnumerable<double> TriangleQualities(CanonicalMesh mesh)
    {
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            Vector3 a = mesh.Positions[checked((int)mesh.Indices[i])];
            Vector3 b = mesh.Positions[checked((int)mesh.Indices[i + 1])];
            Vector3 c = mesh.Positions[checked((int)mesh.Indices[i + 2])];
            double ab2 = Vector3.DistanceSquared(a, b);
            double bc2 = Vector3.DistanceSquared(b, c);
            double ca2 = Vector3.DistanceSquared(c, a);
            double twiceArea = Vector3.Cross(b - a, c - a).Length();
            double denominator = ab2 + bc2 + ca2;
            if (denominator <= 1e-18)
                yield return 0.0;
            else
                yield return 2.0 * Math.Sqrt(3.0) * twiceArea / denominator;
        }
    }

    private static double MeanEllipseBoundaryErrorPixels(CanonicalMesh mesh, int cx, int cy, int rx, int ry)
    {
        int count = 0;
        double total = 0.0;
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            if (MathF.Abs(mesh.Positions[i].Z) > 0.00001f)
                continue;
            double x = mesh.Uvs[i].X * PartReplacementTemplateSpace.CanvasSize;
            double y = mesh.Uvs[i].Y * PartReplacementTemplateSpace.CanvasSize;
            double nx = (x - cx) / rx;
            double ny = (y - cy) / ry;
            double radius = Math.Sqrt(nx * nx + ny * ny);
            total += Math.Abs(radius - 1.0) * Math.Min(rx, ry);
            count++;
        }
        Assert.True(count > 0);
        return total / count;
    }

    private static RgbaImage TwoCircleTorso()
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        FillEllipse(pixels, 512, 370, 153, 185);
        FillEllipse(pixels, 512, 610, 250, 190);
        return new RgbaImage(1024, 1024, pixels);
    }

    private static RgbaImage Ellipse(int cx, int cy, int rx, int ry)
    {
        byte[] pixels = new byte[1024 * 1024 * 4];
        FillEllipse(pixels, cx, cy, rx, ry);
        return new RgbaImage(1024, 1024, pixels);
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
