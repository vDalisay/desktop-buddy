using System.Text;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Category dispatch boundary. The accepted Glasses compiler stays untouched and therefore remains
/// byte-compatible; new category adapters join here. Verification/UI/export should call this seam.
/// </summary>
public static class AssetForgeCompiler
{
    public static GeneratedAsset Generate(ReadOnlySpan<byte> sourcePng, AssetRecipe recipe) => recipe.Category switch
    {
        AssetCategory.Glasses => AssetForgeGenerator.Generate(sourcePng, recipe),
        AssetCategory.TorsoShape or AssetCategory.FootShape => GeneratePartReplacement(sourcePng, recipe),
        _ => throw new NotSupportedException($"Generation for {recipe.Category} is not implemented yet."),
    };

    private static GeneratedAsset GeneratePartReplacement(ReadOnlySpan<byte> sourcePng, AssetRecipe recipe)
    {
        IReadOnlyList<string> errors = recipe.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors), nameof(recipe));

        byte[] source = sourcePng.ToArray();
        RgbaImage decoded = PngCodec.DecodeRgba8(source);
        if (decoded.Width != AssetForgeGenerator.SourceSize || decoded.Height != AssetForgeGenerator.SourceSize)
            throw new FormatException($"Source image must be exactly {AssetForgeGenerator.SourceSize}x{AssetForgeGenerator.SourceSize} RGBA PNG.");

        string inputHash = Hashing.Sha256Hex(source);
        string recipeHash = RecipeCodec.Hash(recipe);
        ForegroundExtractionResult foreground = ForegroundExtractor.Extract(decoded);
        MaskGrid mask = MaskGrid.FromImage(foreground.Image, recipe.Geometry);
        MaskDiagnostics diagnostics = MaskAnalyzer.Analyze(mask);
        if (diagnostics.FilledCells == 0)
            throw new InvalidOperationException("Source has no visible cells after foreground extraction and thresholding.");

        double maskFraction = (double)diagnostics.FilledCells / (mask.Width * mask.Height);
        if (maskFraction > 0.75)
            throw new InvalidOperationException($"The {recipe.Category} mask covers {maskFraction:P0} of the canvas; use a cleaner source with more transparent/background space.");

        CanonicalMesh mesh = PartReplacementGenerator.Generate(mask, recipe.Geometry, recipe.Category);
        string geometryHash = mesh.CanonicalHash();
        byte[] glb = GlbWriter.Write(mesh);
        GlbWriter.ValidateSingleMesh(glb);
        string glbHash = Hashing.Sha256Hex(glb);

        RgbaImage runtimeBase = PngCodec.ResizeBox(foreground.Image, recipe.Geometry.RuntimeTextureResolution);
        RgbaImage runtime = TextureBleed.FillTransparentWithNearestAuthoredColour(runtimeBase);
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
            UsedGlassesTemplate: true,
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
