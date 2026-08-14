using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

public readonly record struct EnvironmentGeneratedBounds(float Width, float Height, float Depth)
{
    public static EnvironmentGeneratedBounds Analyze(CanonicalMesh mesh)
    {
        if (mesh.Positions.Count == 0) return default;
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
        foreach (Vector3 p in mesh.Positions)
        {
            minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X);
            minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y);
            minZ = MathF.Min(minZ, p.Z); maxZ = MathF.Max(maxZ, p.Z);
        }
        return new EnvironmentGeneratedBounds(maxX - minX, maxY - minY, maxZ - minZ);
    }
}

/// <summary>
/// Deterministic front-derived 2.5D generator for floor Environment assets. Legacy Lamp@1 keeps
/// its accepted visible-bounds auto-fit contract. Lamp@2 and later template-space presets preserve
/// literal 1024x1024 authoring coordinates through EnvironmentTemplateMapping, so moving/scaling
/// clean source art produces the documented in-room placement change without silent re-centring.
/// </summary>
public static class EnvironmentSilhouetteGenerator
{
    private const float Diagonal = 1.41421356237f;

    public static CanonicalMesh Generate(MaskGrid grid, AssetRecipe recipe)
    {
        if (recipe.AssetFamily != AssetFamily.Environment)
            throw new ArgumentException("Environment silhouette generation requires an Environment recipe.", nameof(recipe));
        if (grid.FilledCount == 0)
            throw new InvalidOperationException("Source contains no visible Environment geometry after thresholding.");

        Bounds bounds = FindBounds(grid);
        float logicalHeight = (float)recipe.Environment.LogicalHeight;
        bool literalTemplate = EnvironmentTemplateMapping.UsesLiteralTemplateSpace(recipe);
        float legacyUnitsPerCell = logicalHeight / Math.Max(1, bounds.MaxY - bounds.MinY + 1);
        float legacyCenterX = (bounds.MinX + bounds.MaxX + 1) * .5f;
        float legacyFloorY = bounds.MaxY + 1;
        float halfDepth = logicalHeight * (float)recipe.Geometry.Depth * .5f;
        float[] inward = BuildDistance(grid, recipe.Geometry.SurfaceSmoothness);
        float maxInset = inward.DefaultIfEmpty(0f).Max();

        var mesh = new CanonicalMesh();
        var vertices = new Dictionary<int, uint>();

        uint Vertex(int vx, int vy, bool front)
        {
            int key = (((vy * (grid.Width + 1)) + vx) << 1) | (front ? 1 : 0);
            if (vertices.TryGetValue(key, out uint existing)) return existing;

            float x;
            float y;
            if (literalTemplate)
            {
                Vector2 mapped = EnvironmentTemplateMapping.GridVertexToWorld(
                    vx,
                    vy,
                    grid.Width,
                    grid.Height,
                    recipe);
                x = mapped.X;
                y = mapped.Y;
            }
            else
            {
                x = (vx - legacyCenterX) * legacyUnitsPerCell;
                y = -(legacyFloorY - vy) * legacyUnitsPerCell;
            }

            float surface = SurfaceHalfDepth(grid, inward, maxInset, vx, vy, halfDepth, recipe.Geometry);
            uint created = mesh.AddVertex(
                new Vector3(x, y, front ? surface : -surface),
                new Vector2(vx / (float)grid.Width, vy / (float)grid.Height));
            vertices.Add(key, created);
            return created;
        }

        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            if (!grid[x, y]) continue;
            uint fTl = Vertex(x, y, true); uint fTr = Vertex(x + 1, y, true);
            uint fBl = Vertex(x, y + 1, true); uint fBr = Vertex(x + 1, y + 1, true);
            uint bTl = Vertex(x, y, false); uint bTr = Vertex(x + 1, y, false);
            uint bBl = Vertex(x, y + 1, false); uint bBr = Vertex(x + 1, y + 1, false);
            mesh.AddTriangle(fBl, fBr, fTr); mesh.AddTriangle(fBl, fTr, fTl);
            mesh.AddTriangle(bBl, bTr, bBr); mesh.AddTriangle(bBl, bTl, bTr);
            if (!grid[x - 1, y]) { mesh.AddTriangle(fTl, bBl, fBl); mesh.AddTriangle(fTl, bTl, bBl); }
            if (!grid[x + 1, y]) { mesh.AddTriangle(fTr, fBr, bBr); mesh.AddTriangle(fTr, bBr, bTr); }
            if (!grid[x, y - 1]) { mesh.AddTriangle(fTl, fTr, bTr); mesh.AddTriangle(fTl, bTr, bTl); }
            if (!grid[x, y + 1]) { mesh.AddTriangle(fBl, bBl, bBr); mesh.AddTriangle(fBl, bBr, fBr); }
        }
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Bounds FindBounds(MaskGrid grid)
    {
        int minX = grid.Width, maxX = -1, minY = grid.Height, maxY = -1;
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            if (!grid[x, y]) continue;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        if (maxX < minX) throw new InvalidOperationException("No Environment silhouette bounds could be resolved.");
        return new Bounds(minX, maxX, minY, maxY);
    }

    private static float[] BuildDistance(MaskGrid grid, double smoothness)
    {
        const float infinity = 1_000_000f;
        float[] values = new float[grid.Width * grid.Height];
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            int index = y * grid.Width + x;
            values[index] = !grid[x, y] || IsBoundary(grid, x, y) ? 0f : infinity;
        }

        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            if (!grid[x, y]) continue;
            int i = y * grid.Width + x;
            Relax(grid, values, x, y, i, -1, 0, 1f); Relax(grid, values, x, y, i, 0, -1, 1f);
            Relax(grid, values, x, y, i, -1, -1, Diagonal); Relax(grid, values, x, y, i, 1, -1, Diagonal);
        }
        for (int y = grid.Height - 1; y >= 0; y--)
        for (int x = grid.Width - 1; x >= 0; x--)
        {
            if (!grid[x, y]) continue;
            int i = y * grid.Width + x;
            Relax(grid, values, x, y, i, 1, 0, 1f); Relax(grid, values, x, y, i, 0, 1, 1f);
            Relax(grid, values, x, y, i, 1, 1, Diagonal); Relax(grid, values, x, y, i, -1, 1, Diagonal);
        }

        int passes = Math.Clamp((int)Math.Round(Math.Clamp(smoothness, 0.0, 1.0) * 10.0), 0, 10);
        if (passes == 0) return values;
        float[] scratch = new float[values.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            Array.Copy(values, scratch, values.Length);
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                if (!grid[x, y]) continue;
                int i = y * grid.Width + x;
                if (IsBoundary(grid, x, y)) { scratch[i] = 0; continue; }
                float total = values[i] * 4f, weight = 4f;
                foreach ((int dx, int dy, float w) in Neighbors())
                {
                    int nx = x + dx, ny = y + dy;
                    if (!grid[nx, ny]) continue;
                    total += values[ny * grid.Width + nx] * w;
                    weight += w;
                }
                scratch[i] = total / weight;
            }
            (values, scratch) = (scratch, values);
        }
        return values;
    }

    private static IEnumerable<(int X, int Y, float Weight)> Neighbors()
    {
        yield return (-1, 0, 1f); yield return (1, 0, 1f); yield return (0, -1, 1f); yield return (0, 1, 1f);
        yield return (-1, -1, .7f); yield return (1, -1, .7f); yield return (-1, 1, .7f); yield return (1, 1, .7f);
    }

    private static void Relax(MaskGrid grid, float[] values, int x, int y, int index, int dx, int dy, float cost)
    {
        int nx = x + dx, ny = y + dy;
        if (!grid[nx, ny]) return;
        values[index] = MathF.Min(values[index], values[ny * grid.Width + nx] + cost);
    }

    private static bool IsBoundary(MaskGrid grid, int x, int y) =>
        !grid[x - 1, y] || !grid[x + 1, y] || !grid[x, y - 1] || !grid[x, y + 1];

    private static float SurfaceHalfDepth(MaskGrid grid, float[] inward, float maxInset, int vx, int vy, float halfDepth, GeometrySettings settings)
    {
        float inset = VertexInset(grid, inward, vx, vy);
        float normalized = maxInset <= .0001f ? 0f : Math.Clamp(inset / maxInset, 0f, 1f);
        float roundness = (float)settings.Roundness;
        return settings.ShapeMode switch
        {
            ShapeMode.RoundedExtrusion => Rounded(inset, halfDepth, roundness),
            ShapeMode.InflatedSolid => halfDepth * ((.18f + (1f - roundness) * .24f) +
                (1f - (.18f + (1f - roundness) * .24f)) * MathF.Sin(MathF.Pow(normalized, .82f + (1f - roundness) * .55f) * MathF.PI * .5f)),
            ShapeMode.Relief => halfDepth * ((.48f - roundness * .16f) +
                (1f - (.48f - roundness * .16f)) * SmoothStep(MathF.Pow(normalized, .72f))),
            _ => throw new InvalidOperationException($"Environment silhouette mode {settings.ShapeMode} is unsupported."),
        };
    }

    private static float Rounded(float inset, float halfDepth, float roundness)
    {
        float bevel = 1.5f + roundness * 10f;
        float t = SmoothStep(Math.Clamp(inset / bevel, 0f, 1f));
        float side = halfDepth * (1f - .86f * roundness);
        return side + (halfDepth - side) * t;
    }

    private static float VertexInset(MaskGrid grid, float[] inward, int vx, int vy)
    {
        bool any = false, touchesEmpty = false;
        float total = 0; int count = 0;
        for (int cy = vy - 1; cy <= vy; cy++)
        for (int cx = vx - 1; cx <= vx; cx++)
        {
            if (cx < 0 || cy < 0 || cx >= grid.Width || cy >= grid.Height || !grid[cx, cy])
            { touchesEmpty = true; continue; }
            any = true; total += inward[cy * grid.Width + cx]; count++;
        }
        return !any || touchesEmpty || count == 0 ? 0f : total / count + 1f;
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
    private readonly record struct Bounds(int MinX, int MaxX, int MinY, int MaxY);
}
