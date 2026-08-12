using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace DesktopBuddy.AssetForge.Core;

public static class GlbWriter
{
    private const uint Magic=0x46546C67, Version=2, JsonChunk=0x4E4F534A, BinChunk=0x004E4942;
    public static byte[] Write(CanonicalMesh mesh)
    {
        if(mesh.Positions.Count==0||mesh.Indices.Count==0||mesh.Positions.Count!=mesh.Normals.Count||mesh.Positions.Count!=mesh.Uvs.Count)throw new ArgumentException("Mesh is incomplete.",nameof(mesh));
        using var bin=new MemoryStream();
        int posOffset=0; foreach(Vector3 v in mesh.Positions)WriteVec3(bin,v); int posLength=(int)bin.Length;
        Align4(bin); int normOffset=(int)bin.Length; foreach(Vector3 v in mesh.Normals)WriteVec3(bin,v); int normLength=(int)bin.Length-normOffset;
        Align4(bin); int uvOffset=(int)bin.Length; foreach(Vector2 v in mesh.Uvs)WriteVec2(bin,v); int uvLength=(int)bin.Length-uvOffset;
        Align4(bin); int indexOffset=(int)bin.Length; Span<byte> four=stackalloc byte[4]; foreach(uint i in mesh.Indices){BinaryPrimitives.WriteUInt32LittleEndian(four,i);bin.Write(four);} int indexLength=(int)bin.Length-indexOffset; Align4(bin);
        Vector3 min=new(float.PositiveInfinity),max=new(float.NegativeInfinity);foreach(Vector3 p in mesh.Positions){min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
        string json=BuildJson(mesh,bin.Length,posOffset,posLength,normOffset,normLength,uvOffset,uvLength,indexOffset,indexLength,min,max);
        byte[] jsonBytes=Encoding.UTF8.GetBytes(json); int jsonPad=(4-jsonBytes.Length%4)%4; int total=12+8+jsonBytes.Length+jsonPad+8+(int)bin.Length;
        using var output=new MemoryStream(total); WriteU32(output,Magic);WriteU32(output,Version);WriteU32(output,(uint)total);WriteU32(output,(uint)(jsonBytes.Length+jsonPad));WriteU32(output,JsonChunk);output.Write(jsonBytes);for(int i=0;i<jsonPad;i++)output.WriteByte(0x20);WriteU32(output,(uint)bin.Length);WriteU32(output,BinChunk);bin.Position=0;bin.CopyTo(output);return output.ToArray();
    }

    public static void ValidateSingleMesh(ReadOnlySpan<byte> glb)
    {
        if(glb.Length<20||BinaryPrimitives.ReadUInt32LittleEndian(glb[..4])!=Magic||BinaryPrimitives.ReadUInt32LittleEndian(glb.Slice(4,4))!=Version||BinaryPrimitives.ReadUInt32LittleEndian(glb.Slice(8,4))!=glb.Length)throw new FormatException("Invalid GLB header.");
        int jsonLength=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.Slice(12,4)));if(BinaryPrimitives.ReadUInt32LittleEndian(glb.Slice(16,4))!=JsonChunk||20+jsonLength>glb.Length)throw new FormatException("Invalid GLB JSON chunk.");
        using JsonDocument doc=JsonDocument.Parse(glb.Slice(20,jsonLength).TrimEnd((byte)0x20));JsonElement root=doc.RootElement;
        if(!root.TryGetProperty("meshes",out JsonElement meshes)||meshes.GetArrayLength()!=1)throw new FormatException("Generated GLB must contain exactly one mesh.");
        if(!root.TryGetProperty("nodes",out JsonElement nodes)||nodes.GetArrayLength()!=1)throw new FormatException("Generated GLB must contain exactly one node.");
    }

    private static string BuildJson(CanonicalMesh mesh,long bufferLength,int po,int pl,int no,int nl,int uo,int ul,int io,int il,Vector3 min,Vector3 max)
    {
        string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
        return "{"+
        "\"asset\":{\"version\":\"2.0\",\"generator\":\"DesktopBuddy.AssetForge.Core/1\"},"+
        "\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0}],"+
        "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"TEXCOORD_0\":2},\"indices\":3,\"mode\":4}]}],"+
        $"\"buffers\":[{{\"byteLength\":{bufferLength}}}],"+
        $"\"bufferViews\":[{{\"buffer\":0,\"byteOffset\":{po},\"byteLength\":{pl},\"target\":34962}},{{\"buffer\":0,\"byteOffset\":{no},\"byteLength\":{nl},\"target\":34962}},{{\"buffer\":0,\"byteOffset\":{uo},\"byteLength\":{ul},\"target\":34962}},{{\"buffer\":0,\"byteOffset\":{io},\"byteLength\":{il},\"target\":34963}}],"+
        $"\"accessors\":[{{\"bufferView\":0,\"componentType\":5126,\"count\":{mesh.Positions.Count},\"type\":\"VEC3\",\"min\":[{F(min.X)},{F(min.Y)},{F(min.Z)}],\"max\":[{F(max.X)},{F(max.Y)},{F(max.Z)}]}},{{\"bufferView\":1,\"componentType\":5126,\"count\":{mesh.Normals.Count},\"type\":\"VEC3\"}},{{\"bufferView\":2,\"componentType\":5126,\"count\":{mesh.Uvs.Count},\"type\":\"VEC2\"}},{{\"bufferView\":3,\"componentType\":5125,\"count\":{mesh.Indices.Count},\"type\":\"SCALAR\"}}]"+"}";
    }
    private static void WriteVec3(Stream s,Vector3 v){WriteF(s,v.X);WriteF(s,v.Y);WriteF(s,v.Z);}private static void WriteVec2(Stream s,Vector2 v){WriteF(s,v.X);WriteF(s,v.Y);}private static void WriteF(Stream s,float v){Span<byte>b=stackalloc byte[4];BinaryPrimitives.WriteSingleLittleEndian(b,v);s.Write(b);}private static void WriteU32(Stream s,uint v){Span<byte>b=stackalloc byte[4];BinaryPrimitives.WriteUInt32LittleEndian(b,v);s.Write(b);}private static void Align4(Stream s){while((s.Length&3)!=0)s.WriteByte(0);}
}
