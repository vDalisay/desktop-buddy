using System.Buffers.Binary;
using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

public sealed class MaskGrid
{
    private readonly bool[] _cells;
    public MaskGrid(int width, int height) { Width = width; Height = height; _cells = new bool[width * height]; }
    public int Width { get; }
    public int Height { get; }
    public bool this[int x, int y] { get => x >= 0 && y >= 0 && x < Width && y < Height && _cells[y * Width + x]; set { if (x >= 0 && y >= 0 && x < Width && y < Height) _cells[y * Width + x] = value; } }
    public int FilledCount => _cells.Count(static value => value);
    public MaskGrid Clone() { var copy = new MaskGrid(Width, Height); Array.Copy(_cells, copy._cells, _cells.Length); return copy; }

    public static MaskGrid FromImage(RgbaImage image, GeometrySettings settings)
    {
        int size = settings.GeometryResolution;
        int blockX = image.Width / size, blockY = image.Height / size;
        var grid = new MaskGrid(size, size);
        double threshold = settings.AlphaThreshold * 255.0;
        for (int gy = 0; gy < size; gy++)
        for (int gx = 0; gx < size; gx++)
        {
            long alpha = 0;
            for (int y = 0; y < blockY; y++)
            for (int x = 0; x < blockX; x++) alpha += image.Alpha(gx * blockX + x, gy * blockY + y);
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
            bool left = source[x, y], right = source[mirror, y];
            bool l = left, r = right;
            if (mode == SymmetryMode.MirrorLeftToRight) r = left;
            else if (mode == SymmetryMode.MirrorRightToLeft) l = right;
            else l = r = left || right;
            result[x, y] = l; result[mirror, y] = r;
        }
        return result;
    }

    private static MaskGrid Dilate(MaskGrid source)
    {
        var result = source.Clone();
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
            if (source[x, y]) { result[x - 1, y] = true; result[x + 1, y] = true; result[x, y - 1] = true; result[x, y + 1] = true; }
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
    private static readonly (int X, int Y)[] Neighbors = [(1,0),(-1,0),(0,1),(0,-1)];
    public static MaskDiagnostics Analyze(MaskGrid grid)
    {
        int components = CountRegions(grid, true, out _);
        int holes = CountRegions(grid, false, out int exteriorEmpty);
        holes -= exteriorEmpty;
        int boundaries = 0;
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++) if (grid[x,y])
            foreach ((int dx,int dy) in Neighbors) if (!grid[x+dx,y+dy]) boundaries++;
        return new MaskDiagnostics(components, Math.Max(0, holes), grid.FilledCount, boundaries);
    }

    private static int CountRegions(MaskGrid grid, bool target, out int exteriorCount)
    {
        var visited = new bool[grid.Width * grid.Height];
        int count = 0; exteriorCount = 0;
        var queue = new Queue<(int X,int Y)>();
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            int index = y * grid.Width + x;
            if (visited[index] || grid[x,y] != target) continue;
            count++; bool exterior = false; visited[index] = true; queue.Enqueue((x,y));
            while (queue.Count > 0)
            {
                (int cx,int cy)=queue.Dequeue();
                if (cx == 0 || cy == 0 || cx == grid.Width-1 || cy == grid.Height-1) exterior = true;
                foreach ((int dx,int dy) in Neighbors)
                {
                    int nx=cx+dx, ny=cy+dy;
                    if (nx<0||ny<0||nx>=grid.Width||ny>=grid.Height) continue;
                    int ni=ny*grid.Width+nx;
                    if (!visited[ni] && grid[nx,ny] == target) { visited[ni]=true; queue.Enqueue((nx,ny)); }
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

    public void AddVertex(Vector3 position, Vector2 uv) { Positions.Add(position); Uvs.Add(uv); Normals.Add(Vector3.Zero); }
    public void AddTriangle(uint a,uint b,uint c) { Indices.Add(a); Indices.Add(b); Indices.Add(c); }
    public void RecalculateNormals()
    {
        for (int i=0;i<Normals.Count;i++) Normals[i]=Vector3.Zero;
        for (int i=0;i<Indices.Count;i+=3)
        {
            int a=(int)Indices[i], b=(int)Indices[i+1], c=(int)Indices[i+2];
            Vector3 cross=Vector3.Cross(Positions[b]-Positions[a], Positions[c]-Positions[a]);
            if (cross.LengthSquared() <= 1e-16f) continue;
            Normals[a]+=cross; Normals[b]+=cross; Normals[c]+=cross;
        }
        for(int i=0;i<Normals.Count;i++) Normals[i]=Normals[i].LengthSquared()>1e-16f?Vector3.Normalize(Normals[i]):Vector3.UnitZ;
    }

    public string CanonicalHash()
    {
        using var stream = new MemoryStream();
        foreach (Vector3 p in Positions) { WriteQuantized(stream, p.X); WriteQuantized(stream, p.Y); WriteQuantized(stream, p.Z); }
        foreach (Vector3 n in Normals) { WriteQuantized(stream, n.X); WriteQuantized(stream, n.Y); WriteQuantized(stream, n.Z); }
        foreach (Vector2 uv in Uvs) { WriteQuantized(stream, uv.X); WriteQuantized(stream, uv.Y); }
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
    public static CanonicalMesh GenerateGlasses(MaskGrid grid, GeometrySettings settings)
    {
        if (grid.FilledCount == 0) throw new InvalidOperationException("Source contains no visible geometry after thresholding.");
        var mesh = new CanonicalMesh();
        float depth=(float)settings.Depth, half=depth*0.5f;
        float cell=2f/grid.Width;
        for(int y=0;y<grid.Height;y++)
        for(int x=0;x<grid.Width;x++) if(grid[x,y])
        {
            float x0=-1f+x*cell, x1=x0+cell, y1=1f-y*cell, y0=y1-cell;
            float u0=(float)x/grid.Width,u1=(float)(x+1)/grid.Width,v0=(float)y/grid.Height,v1=(float)(y+1)/grid.Height;
            AddQuad(mesh,new(x0,y0,half),new(x1,y0,half),new(x1,y1,half),new(x0,y1,half),new(u0,v1),new(u1,v1),new(u1,v0),new(u0,v0),front:true);
            AddQuad(mesh,new(x0,y0,-half),new(x0,y1,-half),new(x1,y1,-half),new(x1,y0,-half),new(u0,v1),new(u0,v0),new(u1,v0),new(u1,v1),front:true);
            if(!grid[x-1,y]) AddQuad(mesh,new(x0,y0,-half),new(x0,y0,half),new(x0,y1,half),new(x0,y1,-half),new(u0,v1),new(u0,v1),new(u0,v0),new(u0,v0),true);
            if(!grid[x+1,y]) AddQuad(mesh,new(x1,y0,half),new(x1,y0,-half),new(x1,y1,-half),new(x1,y1,half),new(u1,v1),new(u1,v1),new(u1,v0),new(u1,v0),true);
            if(!grid[x,y-1]) AddQuad(mesh,new(x0,y1,half),new(x1,y1,half),new(x1,y1,-half),new(x0,y1,-half),new(u0,v0),new(u1,v0),new(u1,v0),new(u0,v0),true);
            if(!grid[x,y+1]) AddQuad(mesh,new(x0,y0,-half),new(x1,y0,-half),new(x1,y0,half),new(x0,y0,half),new(u0,v1),new(u1,v1),new(u1,v1),new(u0,v1),true);
        }
        AddTemples(mesh,grid,settings,half);
        mesh.RecalculateNormals();
        return mesh;
    }

    private static void AddTemples(CanonicalMesh mesh,MaskGrid grid,GeometrySettings settings,float frontHalfDepth)
    {
        int minX=grid.Width,maxX=-1,minY=grid.Height,maxY=-1;
        for(int y=0;y<grid.Height;y++) for(int x=0;x<grid.Width;x++) if(grid[x,y]){minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
        float cell=2f/grid.Width;
        float left=-1f+minX*cell, right=-1f+(maxX+1)*cell;
        float centerY=1f-((minY+maxY+1)*0.5f)*cell-(float)settings.TempleDrop;
        float thick=(float)settings.TempleThickness, length=(float)settings.TempleLength;
        float zCenter=frontHalfDepth-length*0.5f;
        AddBox(mesh,new(left+thick*0.5f,centerY,zCenter),new(thick,thick,length));
        AddBox(mesh,new(right-thick*0.5f,centerY,zCenter),new(thick,thick,length));
    }

    private static void AddBox(CanonicalMesh mesh,Vector3 center,Vector3 size)
    {
        Vector3 h=size*0.5f;
        Vector3[] p=[center+new Vector3(-h.X,-h.Y,-h.Z),center+new Vector3(h.X,-h.Y,-h.Z),center+new Vector3(h.X,h.Y,-h.Z),center+new Vector3(-h.X,h.Y,-h.Z),center+new Vector3(-h.X,-h.Y,h.Z),center+new Vector3(h.X,-h.Y,h.Z),center+new Vector3(h.X,h.Y,h.Z),center+new Vector3(-h.X,h.Y,h.Z)];
        uint b=(uint)mesh.Positions.Count; foreach(Vector3 v in p)mesh.AddVertex(v,new(.5f,.5f));
        uint[] t=[0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7]; for(int i=0;i<t.Length;i+=3)mesh.AddTriangle(b+t[i],b+t[i+1],b+t[i+2]);
    }

    private static void AddQuad(CanonicalMesh mesh,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector2 ua,Vector2 ub,Vector2 uc,Vector2 ud,bool front)
    {
        uint start=(uint)mesh.Positions.Count; mesh.AddVertex(a,ua);mesh.AddVertex(b,ub);mesh.AddVertex(c,uc);mesh.AddVertex(d,ud);
        if(front){mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);}else{mesh.AddTriangle(start,start+2,start+1);mesh.AddTriangle(start,start+3,start+2);}
    }
}
