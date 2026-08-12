using System.Text;

namespace DesktopBuddy.AssetForge.Core;

public sealed record GeneratedAsset(
    AssetRecipe Recipe,
    CanonicalMesh Mesh,
    MaskDiagnostics Diagnostics,
    ForegroundDiagnostics Foreground,
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
        IReadOnlyList<string> errors = recipe.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors), nameof(recipe));

        byte[] source = sourcePng.ToArray();
        RgbaImage decoded = PngCodec.DecodeRgba8(source);
        if (decoded.Width != SourceSize || decoded.Height != SourceSize)
            throw new FormatException($"Source image must be exactly {SourceSize}x{SourceSize} RGBA PNG.");

        string inputHash = Hashing.Sha256Hex(source);
        string recipeHash = RecipeCodec.Hash(recipe);

        // The author may give us a normal transparent PNG or a fully-opaque drawing on a flat
        // canvas (the common white-canvas case). The glasses preset canonicalizes both into the
        // same transparent foreground representation before any topology is derived.
        ForegroundExtractionResult foreground = ForegroundExtractor.Extract(decoded);
        MaskGrid mask = MaskGrid.FromImage(foreground.Image, recipe.Geometry);
        MaskDiagnostics diagnostics = MaskAnalyzer.Analyze(mask);
        if (diagnostics.FilledCells == 0)
            throw new InvalidOperationException("Source has no visible cells after foreground extraction and thresholding.");

        double maskFraction = (double)diagnostics.FilledCells / (mask.Width * mask.Height);
        if (maskFraction > 0.50)
        {
            throw new InvalidOperationException(
                $"The glasses mask still covers {maskFraction:P0} of the canvas. Refusing to create a slab-shaped asset; " +
                "use a cleaner transparent/uniform-background source or increase separation between the frame colour and the background.");
        }

        CanonicalMesh mesh = ExtrusionGenerator.GenerateGlasses(mask, recipe.Geometry);
        string geometryHash = mesh.CanonicalHash();
        byte[] glb = GlbWriter.Write(mesh);
        GlbWriter.ValidateSingleMesh(glb);
        string glbHash = Hashing.Sha256Hex(glb);

        // Runtime/preview albedo is built from the canonical foreground, not the original opaque
        // canvas. This prevents the removed white/flat background from reappearing in texture
        // sampling around the frame edges.
        RgbaImage runtime = PngCodec.ResizeBox(foreground.Image, recipe.Geometry.RuntimeTextureResolution);
        byte[] albedo = PngCodec.EncodeRgba8(runtime);
        string albedoHash = Hashing.Sha256Hex(albedo);
        string canonical = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(string.Join(
            "\n",
            recipe.GeneratorVersion,
            inputHash,
            recipeHash,
            geometryHash,
            glbHash,
            albedoHash)));

        return new GeneratedAsset(
            recipe,
            mesh,
            diagnostics,
            foreground.Diagnostics,
            glb,
            albedo,
            inputHash,
            recipeHash,
            geometryHash,
            glbHash,
            albedoHash,
            canonical);
    }
}
