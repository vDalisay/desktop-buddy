using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class PartReplacementSubpixelContourTests
{
    [Fact]
    public void Full_resolution_alpha_reconstruction_removes_coarse_grid_boundary_error_without_densifying_cap()
    {
        const int cx = 511;
        const int cy = 503;
        const int rx = 193;
        const int ry = 251;
        RgbaImage source = Ellipse(cx, cy, rx, ry);
        GeometrySettings geometry = AssetRecipe.TorsoShapeDefaults().Geometry with
        {
            GeometryResolution = 64,
            RuntimeTextureResolution = 128,
            SurfaceSmoothness = 1.0,
            SymmetryMode = SymmetryMode.Off,
            ThicknessBiasPixels = 0,
        };
        MaskGrid mask = MaskGrid.FromImage(source, geometry);

        CanonicalMesh raw = PartReplacementGenerator.Generate(mask, geometry, AssetCategory.TorsoShape);
        double rawError = MeanEllipseBoundaryErrorPixels(raw, cx, cy, rx, ry);
        int triangles = raw.TriangleCount;

        CanonicalMesh refined = PartReplacementSubpixelContour.Apply(
            PartReplacementGenerator.Generate(mask, geometry, AssetCategory.TorsoShape),
            source,
            geometry,
            AssetCategory.TorsoShape);
        double refinedError = MeanEllipseBoundaryErrorPixels(refined, cx, cy, rx, ry);

        Assert.Equal(triangles, refined.TriangleCount);
        Assert.True(rawError > 1.0,
            $"The deliberately coarse 64 grid should expose visible boundary quantization; measured {rawError:F3}px.");
        Assert.True(refinedError < rawError * 0.25,
            $"Expected full-resolution contour projection to strongly reduce grid error; raw={rawError:F3}px, refined={refinedError:F3}px.");
    }

    [Fact]
    public void Subpixel_reconstruction_is_deterministic_and_produces_non_grid_uvs()
    {
        RgbaImage source = Ellipse(509, 497, 181, 237);
        GeometrySettings geometry = AssetRecipe.TorsoShapeDefaults().Geometry with
        {
            GeometryResolution = 64,
            SurfaceSmoothness = 1.0,
            SymmetryMode = SymmetryMode.Off,
            ThicknessBiasPixels = 0,
        };
        MaskGrid mask = MaskGrid.FromImage(source, geometry);

        CanonicalMesh a = PartReplacementSubpixelContour.Apply(
            PartReplacementGenerator.Generate(mask, geometry, AssetCategory.TorsoShape),
            source,
            geometry,
            AssetCategory.TorsoShape);
        CanonicalMesh b = PartReplacementSubpixelContour.Apply(
            PartReplacementGenerator.Generate(mask, geometry, AssetCategory.TorsoShape),
            source,
            geometry,
            AssetCategory.TorsoShape);

        Assert.Equal(a.CanonicalHash(), b.CanonicalHash());

        int[] rim = RimVertices(a).ToArray();
        Assert.NotEmpty(rim);
        Assert.Contains(rim, index =>
        {
            float gridU = a.Uvs[index].X * geometry.GeometryResolution;
            float gridV = a.Uvs[index].Y * geometry.GeometryResolution;
            return MathF.Abs(gridU - MathF.Round(gridU)) > 0.001f ||
                   MathF.Abs(gridV - MathF.Round(gridV)) > 0.001f;
        });
    }

    [Fact]
    public void Zero_smoothness_keeps_strict_grid_compatibility_path()
    {
        RgbaImage source = Ellipse(512, 500, 190, 240);
        GeometrySettings geometry = AssetRecipe.TorsoShapeDefaults().Geometry with
        {
            GeometryResolution = 64,
            SurfaceSmoothness = 0.0,
        };
        MaskGrid mask = MaskGrid.FromImage(source, geometry);
        CanonicalMesh raw = PartReplacementGenerator.Generate(mask, geometry, AssetCategory.TorsoShape);
        string expected = raw.CanonicalHash();

        CanonicalMesh result = PartReplacementSubpixelContour.Apply(
            raw,
            source,
            geometry,
            AssetCategory.TorsoShape);

        Assert.Equal(expected, result.CanonicalHash());
    }

    private static double MeanEllipseBoundaryErrorPixels(CanonicalMesh mesh, int cx, int cy, int rx, int ry)
    {
        int[] rim = RimVertices(mesh).ToArray();
        Assert.NotEmpty(rim);
        double total = 0.0;
        foreach (int index in rim)
        {
            double x = mesh.Uvs[index].X * PartReplacementTemplateSpace.CanvasSize;
            double y = mesh.Uvs[index].Y * PartReplacementTemplateSpace.CanvasSize;
            double nx = (x - cx) / rx;
            double ny = (y - cy) / ry;
            double normalizedRadius = Math.Sqrt(nx * nx + ny * ny);
            total += Math.Abs(normalizedRadius - 1.0) * Math.Min(rx, ry);
        }
        return total / rim.Length;
    }

    private static HashSet<int> RimVertices(CanonicalMesh mesh)
    {
        const float epsilon = 0.000001f;
        var rim = new HashSet<int>();
        for (int triangle = 0; triangle < mesh.Indices.Count; triangle += 3)
        {
            int a = checked((int)mesh.Indices[triangle]);
            int b = checked((int)mesh.Indices[triangle + 1]);
            int c = checked((int)mesh.Indices[triangle + 2]);
            float za = mesh.Positions[a].Z;
            float zb = mesh.Positions[b].Z;
            float zc = mesh.Positions[c].Z;
            bool side = (za > epsilon || zb > epsilon || zc > epsilon) &&
                        (za < -epsilon || zb < -epsilon || zc < -epsilon);
            if (!side)
                continue;
            rim.Add(a);
            rim.Add(b);
            rim.Add(c);
        }
        return rim;
    }

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
            int i = ((y * 1024) + x) * 4;
            pixels[i] = 45;
            pixels[i + 1] = 145;
            pixels[i + 2] = 220;
            pixels[i + 3] = 255;
        }
        return new RgbaImage(1024, 1024, pixels);
    }
}
