using System.Text;

namespace DesktopBuddy.AssetForge.Core;

public sealed record GeneratedAsset(
    AssetRecipe Recipe,
    CanonicalMesh Mesh,
    MaskDiagnostics Diagnostics,
    byte[] GlbBytes,
    byte[] AlbedoPng,
    string InputHash,
    string RecipeHash,
    string GeometryHash,
    string GlbHash,
    string AlbedoHash,
    string CanonicalAssetHash)
{
    public int TriangleCount => Mesh.TriangleCount;
    public int VertexCount => Mesh.Positions.Count;
}

public static class AssetForgeGenerator
{
    public const int SourceSize = 1024;
    public static GeneratedAsset Generate(ReadOnlySpan<byte> sourcePng, AssetRecipe recipe)
    {
        IReadOnlyList<string> errors=recipe.Validate();if(errors.Count>0)throw new ArgumentException(string.Join("; ",errors),nameof(recipe));
        byte[] source=sourcePng.ToArray();RgbaImage image=PngCodec.DecodeRgba8(source);
        if(image.Width!=SourceSize||image.Height!=SourceSize)throw new FormatException($"Source image must be exactly {SourceSize}x{SourceSize} RGBA PNG.");
        string inputHash=Hashing.Sha256Hex(source),recipeHash=RecipeCodec.Hash(recipe);
        MaskGrid mask=MaskGrid.FromImage(image,recipe.Geometry);MaskDiagnostics diagnostics=MaskAnalyzer.Analyze(mask);
        if(diagnostics.FilledCells==0)throw new InvalidOperationException("Source has no visible cells after thresholding.");
        CanonicalMesh mesh=ExtrusionGenerator.GenerateGlasses(mask,recipe.Geometry);string geometryHash=mesh.CanonicalHash();
        byte[] glb=GlbWriter.Write(mesh);GlbWriter.ValidateSingleMesh(glb);string glbHash=Hashing.Sha256Hex(glb);
        RgbaImage runtime=PngCodec.ResizeBox(image,recipe.Geometry.RuntimeTextureResolution);byte[] albedo=PngCodec.EncodeRgba8(runtime);string albedoHash=Hashing.Sha256Hex(albedo);
        string canonical=Hashing.Sha256Hex(Encoding.UTF8.GetBytes(string.Join("\n",recipe.GeneratorVersion,inputHash,recipeHash,geometryHash,glbHash,albedoHash)));
        return new GeneratedAsset(recipe,mesh,diagnostics,glb,albedo,inputHash,recipeHash,geometryHash,glbHash,albedoHash,canonical);
    }
}
