using System.Buffers.Binary;
using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

public sealed class MaskGrid
{
    private readonly bool[] _cells;

    public MaskGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _cells = new bool[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public bool this[int x, int y]
    {
        get => x >= 0 && y >= 0 && x < Width && y < Height && _cells[y * Width + x];
        set { if (x >= 0 && y >= 0 && x < Width && y < Height) _cells[y * Width + x] = value; }
    }
    public int FilledCount => _cells.Count(static value => value);

    public MaskGrid Clone()
    {
        var copy = new MaskGrid(Width, Height);
        Array.Copy(_cells, copy._cells, _cells.Length);
        return copy;
    }

    public static MaskGrid FromImage(RgbaImage image, GeometrySettings settings)
    {
        int size = settings.GeometryResolution;
        int blockX = image.Width / size;
        int blockY = image.Height / size;
        var grid = new MaskGrid(size, size);
        double threshold = settings.AlphaThreshold * 255.0;
        for (int gy = 0; gy < size; gy++)
        for (int gx = 0; gx < size; gx++)
        {
            long alpha = 0;
            for (int y = 0; y < blockY; y++)
            for (int x = 0; x < blockX; x++)
                alpha += image.Alpha(gx * blockX + x, gy * blockY + y);
            grid[gx, gy] = (double)alpha / (blockX * blockY) >= threshold;
        }

        grid = ApplySymmetry(grid, settings.SymmetryMode);
        int bias = settings.ThicknessBiasPixels;
        for (int i = 0; i < Math.Abs(bias); i++) grid = bias > 0 ? Dilate(grid) : Erode(grid);
        return grid;
    }

    private static MaskGrid ApplySymmetry(MaskGrid source, SymmetryMode mode)
    {
        if (mode == SymmetryMode.Off) return source;
        var result = source.Clone();
        int half = source.Width / 2;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < half; x++)
        {
            int mirror = source.Width - 1 - x;
            bool left = source[x, y];
            bool right = source[mirror, y];
            bool l = left;
            bool r = right;
            if (mode == SymmetryMode.MirrorLeftToRight) r = left;
            else if (mode == SymmetryMode.MirrorRightToLeft) l = right;
            else l = r = left || right;
            result[x, y] = l;
            result[mirror, y] = r;
        }
        return result;
    }

    private static MaskGrid Dilate(MaskGrid source)
    {
        var result = source.Clone();
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            if (source[x, y])
            {
                result[x - 1, y] = true;
                result[x + 1, y] = true;
                result[x, y - 1] = true;
                result[x, y + 1] = true;
            }
        return result;
    }

    private static MaskGrid Erode(MaskGrid source)
    {
        var result = new MaskGrid(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            result[x, y] = source[x, y] && source[x - 1, y] && source[x + 1, y] && source[x, y - 1] && source[x, y + 1];
        return result;
    }
}

public readonly record struct MaskDiagnostics(int Components, int Holes, int FilledCells, int BoundaryEdges);

public static class MaskAnalyzer
{
    private static readonly (int X, int Y)[] Neighbors = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    public static MaskDiagnostics Analyze(MaskGrid grid)
    {
        int components = CountRegions(grid, true, out _);
        int holes = CountRegions(grid, false, out int exteriorEmpty) - exteriorEmpty;
        int boundaries = 0;
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
            if (grid[x, y])
                foreach ((int dx, int dy) in Neighbors)
                    if (!grid[x + dx, y + dy]) boundaries++;
        return new MaskDiagnostics(components, Math.Max(0, holes), grid.FilledCount, boundaries);
    }

    private static int CountRegions(MaskGrid grid, bool target, out int exteriorCount)
    {
        var visited = new bool[grid.Width * grid.Height];
        int count = 0;
        exteriorCount = 0;
        var queue = new Queue<(int X, int Y)>();
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            int index = y * grid.Width + x;
            if (visited[index] || grid[x, y] != target) continue;
            count++;
            bool exterior = false;
            visited[index] = true;
            queue.Enqueue((x, y));
            while (queue.Count > 0)
            {
                (int cx, int cy) = queue.Dequeue();
                if (cx == 0 || cy == 0 || cx == grid.Width - 1 || cy == grid.Height - 1) exterior = true;
                foreach ((int dx, int dy) in Neighbors)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= grid.Width || ny >= grid.Height) continue;
                    int ni = ny * grid.Width + nx;
                    if (!visited[ni] && grid[nx, ny] == target)
                    {
                        visited[ni] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
            if (!target && exterior) exteriorCount++;
        }
        return count;
    }
}

public sealed class CanonicalMesh
{
    public List<Vector3> Positions { get; } = [];
    public List<Vector3> Normals { get; } = [];
    public List<Vector2> Uvs { get; } = [];
    public List<uint> Indices { get; } = [];
    public int TriangleCount => Indices.Count / 3;

    public uint AddVertex(Vector3 position, Vector2 uv)
    {
        uint index = checked((uint)Positions.Count);
        Positions.Add(position);
        Uvs.Add(uv);
        Normals.Add(Vector3.Zero);
        return index;
    }

    public void AddTriangle(uint a, uint b, uint c)
    {
        Indices.Add(a);
        Indices.Add(b);
        Indices.Add(c);
    }

    public void RecalculateNormals()
    {
        for (int i = 0; i < Normals.Count; i++) Normals[i] = Vector3.Zero;
        for (int i = 0; i < Indices.Count; i += 3)
        {
            int a = (int)Indices[i];
            int b = (int)Indices[i + 1];
            int c = (int)Indices[i + 2];
            Vector3 cross = Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
            if (cross.LengthSquared() <= 1e-16f) continue;
            Normals[a] += cross;
            Normals[b] += cross;
            Normals[c] += cross;
        }
        for (int i = 0; i < Normals.Count; i++)
            Normals[i] = Normals[i].LengthSquared() > 1e-16f ? Vector3.Normalize(Normals[i]) : Vector3.UnitZ;
    }

    public string CanonicalHash()
    {
        using var stream = new MemoryStream();
        foreach (Vector3 p in Positions)
        {
            WriteQuantized(stream, p.X);
            WriteQuantized(stream, p.Y);
            WriteQuantized(stream, p.Z);
        }
        foreach (Vector3 n in Normals)
        {
            WriteQuantized(stream, n.X);
            WriteQuantized(stream, n.Y);
            WriteQuantized(stream, n.Z);
        }
        foreach (Vector2 uv in Uvs)
        {
            WriteQuantized(stream, uv.X);
            WriteQuantized(stream, uv.Y);
        }
        Span<byte> four = stackalloc byte[4];
        foreach (uint index in Indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(four, index);
            stream.Write(four);
        }
        return Hashing.Sha256Hex(stream.ToArray());
    }

    private static void WriteQuantized(Stream stream, float value)
    {
        float quantized = MathF.Round(value * 1_000_000f) / 1_000_000f;
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, quantized);
        stream.Write(bytes);
    }
}

public static class ExtrusionGenerator
{
    private const int TempleSegments = 12;
    private static readonly (int X, int Y)[] Neighbors = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    public static CanonicalMesh GenerateGlasses(MaskGrid grid, GeometrySettings settings)
    {
        if (grid.FilledCount == 0) throw new InvalidOperationException("Source contains no visible geometry after thresholding.");
        var mesh = new CanonicalMesh();
        int[] inwardDistance = BuildInwardDistance(grid);
        var vertices = new Dictionary<int, uint>();
        float cell = 2f / grid.Width;
        float halfDepth = (float)settings.Depth * 0.5f;

        uint Vertex(int vx, int vy, bool front)
        {
            int key = (((vy * (grid.Width + 1)) + vx) << 1) | (front ? 1 : 0);
            if (vertices.TryGetValue(key, out uint existing)) return existing;
            float x = -1f + vx * cell;
            float y = 1f - vy * cell;
            float surfaceHalf = SurfaceHalfDepth(grid, inwardDistance, vx, vy, halfDepth, settings);
            float z = front ? surfaceHalf : -surfaceHalf;
            uint created = mesh.AddVertex(new Vector3(x, y, z), new Vector2((float)vx / grid.Width, (float)vy / grid.Height));
            vertices.Add(key, created);
            return created;
        }

        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            if (!grid[x, y]) continue;

            uint fTl = Vertex(x, y, true);
            uint fTr = Vertex(x + 1, y, true);
            uint fBl = Vertex(x, y + 1, true);
            uint fBr = Vertex(x + 1, y + 1, true);
            uint bTl = Vertex(x, y, false);
            uint bTr = Vertex(x + 1, y, false);
            uint bBl = Vertex(x, y + 1, false);
            uint bBr = Vertex(x + 1, y + 1, false);

            // Front (+Z) and back (-Z), sharing vertices across neighbouring cells so normals
            // follow the deterministic bevel profile instead of producing one flat normal/cell.
            mesh.AddTriangle(fBl, fBr, fTr);
            mesh.AddTriangle(fBl, fTr, fTl);
            mesh.AddTriangle(bBl, bTr, bBr);
            mesh.AddTriangle(bBl, bTl, bTr);

            if (!grid[x - 1, y])
            {
                mesh.AddTriangle(fTl, bBl, fBl);
                mesh.AddTriangle(fTl, bTl, bBl);
            }
            if (!grid[x + 1, y])
            {
                mesh.AddTriangle(fTr, fBr, bBr);
                mesh.AddTriangle(fTr, bBr, bTr);
            }
            if (!grid[x, y - 1])
            {
                mesh.AddTriangle(fTl, fTr, bTr);
                mesh.AddTriangle(fTl, bTr, bTl);
            }
            if (!grid[x, y + 1])
            {
                mesh.AddTriangle(fBl, bBl, bBr);
                mesh.AddTriangle(fBl, bBr, fBr);
            }
        }

        AddTemples(mesh, grid, settings, halfDepth);
        mesh.RecalculateNormals();
        return mesh;
    }

    private static int[] BuildInwardDistance(MaskGrid grid)
    {
        int[] distance = Enumerable.Repeat(int.MaxValue, grid.Width * grid.Height).ToArray();
        var queue = new Queue<(int X, int Y)>();
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            if (!grid[x, y]) continue;
            bool boundary = Neighbors.Any(n => !grid[x + n.X, y + n.Y]);
            if (!boundary) continue;
            distance[y * grid.Width + x] = 0;
            queue.Enqueue((x, y));
        }

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            int next = distance[y * grid.Width + x] + 1;
            foreach ((int dx, int dy) in Neighbors)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (!grid[nx, ny]) continue;
                int index = ny * grid.Width + nx;
                if (next >= distance[index]) continue;
                distance[index] = next;
                queue.Enqueue((nx, ny));
            }
        }
        return distance;
    }

    private static float SurfaceHalfDepth(
        MaskGrid grid,
        int[] inwardDistance,
        int vx,
        int vy,
        float halfDepth,
        GeometrySettings settings)
    {
        if (settings.ShapeMode == ShapeMode.FlatExtrusion || settings.Roundness <= 0.000001) return halfDepth;

        float roundness = (float)Math.Clamp(settings.Roundness, 0.0, 1.0);
        float bevelDepth = halfDepth * 0.75f * roundness;
        float sideHalf = halfDepth - bevelDepth;
        int bevelCells = Math.Max(1, (int)MathF.Round(1f + roundness * 3f));
        int inset = VertexInset(grid, inwardDistance, vx, vy);
        float t = Math.Clamp((float)inset / bevelCells, 0f, 1f);
        t = t * t * (3f - 2f * t);
        return sideHalf + bevelDepth * t;
    }

    private static int VertexInset(MaskGrid grid, int[] inwardDistance, int vx, int vy)
    {
        bool anyFilled = false;
        bool touchesEmpty = false;
        int minimum = int.MaxValue;
        for (int cy = vy - 1; cy <= vy; cy++)
        for (int cx = vx - 1; cx <= vx; cx++)
        {
            if (cx < 0 || cy < 0 || cx >= grid.Width || cy >= grid.Height || !grid[cx, cy])
            {
                touchesEmpty = true;
                continue;
            }
            anyFilled = true;
            minimum = Math.Min(minimum, inwardDistance[cy * grid.Width + cx]);
        }
        if (!anyFilled || touchesEmpty) return 0;
        return minimum == int.MaxValue ? 0 : minimum + 1;
    }

    private static void AddTemples(CanonicalMesh mesh, MaskGrid grid, GeometrySettings settings, float frontHalfDepth)
    {
        int minX = grid.Width;
        int maxX = -1;
        int minY = grid.Height;
        int maxY = -1;
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
            if (grid[x, y])
            {
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

        float cell = 2f / grid.Width;
        float radius = (float)settings.TempleThickness * 0.5f;
        float left = -1f + minX * cell + radius;
        float right = -1f + (maxX + 1) * cell - radius;
        float centerGridY = (minY + maxY + 1) * 0.5f;
        float centerY = 1f - centerGridY * cell - (float)settings.TempleDrop;
        float length = (float)settings.TempleLength;
        float endZ = frontHalfDepth - length;
        float rootV = centerGridY / grid.Height;
        float leftU = (minX + 0.5f) / grid.Width;
        float rightU = (maxX + 0.5f) / grid.Width;

        AddRoundBar(mesh, new Vector2(left, centerY), frontHalfDepth, endZ, radius, new Vector2(leftU, rootV));
        AddRoundBar(mesh, new Vector2(right, centerY), frontHalfDepth, endZ, radius, new Vector2(rightU, rootV));
    }

    private static void AddRoundBar(
        CanonicalMesh mesh,
        Vector2 center,
        float frontZ,
        float backZ,
        float radius,
        Vector2 uv)
    {
        uint[] front = new uint[TempleSegments];
        uint[] back = new uint[TempleSegments];
        for (int i = 0; i < TempleSegments; i++)
        {
            float angle = MathF.Tau * i / TempleSegments;
            float x = center.X + MathF.Cos(angle) * radius;
            float y = center.Y + MathF.Sin(angle) * radius;
            front[i] = mesh.AddVertex(new Vector3(x, y, frontZ), uv);
            back[i] = mesh.AddVertex(new Vector3(x, y, backZ), uv);
        }
        uint frontCenter = mesh.AddVertex(new Vector3(center.X, center.Y, frontZ), uv);
        uint backCenter = mesh.AddVertex(new Vector3(center.X, center.Y, backZ), uv);

        for (int i = 0; i < TempleSegments; i++)
        {
            int j = (i + 1) % TempleSegments;
            mesh.AddTriangle(front[i], back[j], front[j]);
            mesh.AddTriangle(front[i], back[i], back[j]);
            mesh.AddTriangle(frontCenter, front[i], front[j]);
            mesh.AddTriangle(backCenter, back[j], back[i]);
        }
    }
}
