using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopBuddy.AssetForge.Core;

public enum AssetFamily { BuddyStudio = 0, Environment = 1 }
public enum AssetCategory { Glasses = 0, TorsoShape = 1, FootShape = 2, Lamp = 10, Sofa = 11, Table = 12, Plant = 13, Painting = 14 }
public enum ShapeMode { FlatExtrusion = 0, RoundedExtrusion = 1, InflatedSolid = 2, Relief = 3 }
public enum SymmetryMode { Off = 0, MirrorLeftToRight = 1, MirrorRightToLeft = 2, AverageBothSides = 3 }

public sealed record GeometrySettings
{
    public int GeometryResolution { get; init; } = 512;
    public double AlphaThreshold { get; init; } = 0.50;
    public int ThicknessBiasPixels { get; init; }
    public double FrameThickness { get; init; } = 0.055;
    /// <summary>
    /// Signed source-canvas thickness adjustment applied only to the authored nose bridge.
    /// Zero preserves the source bridge exactly; positive values thicken and negative values thin.
    /// </summary>
    public int BridgeThicknessBiasPixels { get; init; }
    public double Depth { get; init; } = 0.065;
    public double Roundness { get; init; } = 0.85;
    public ShapeMode ShapeMode { get; init; } = ShapeMode.RoundedExtrusion;
    public SymmetryMode SymmetryMode { get; init; } = SymmetryMode.Off;
    public int RuntimeTextureResolution { get; init; } = 512;
    public double TempleThickness { get; init; } = 0.045;
    public double TempleLength { get; init; } = 0.52;
    public double TempleDrop { get; init; } = 0.00;
}

public sealed record ThumbnailSettings
{
    public double YawDegrees { get; init; } = 12;
    public double PitchDegrees { get; init; } = -8;
    public double Padding { get; init; } = 0.12;
}

public sealed record AssetRecipe
{
    public const int CurrentGeneratorVersion = 1;
    public const double DefaultLightingLevel = 0.36;
    public int GeneratorVersion { get; init; } = CurrentGeneratorVersion;
    public string PresetId { get; init; } = "glasses";
    public int PresetVersion { get; init; } = 2;
    public AssetFamily AssetFamily { get; init; } = AssetFamily.BuddyStudio;
    public AssetCategory Category { get; init; } = AssetCategory.Glasses;
    public string FeatureId { get; init; } = "glasses.new_asset";
    public string ContentId { get; init; } = "cosmetic.glasses.new_asset";
    public string DisplayName { get; init; } = "New Glasses";
    public string SourceFile { get; init; } = "source.png";
    public int PriceCredits { get; init; } = 100;
    public int SortOrder { get; init; } = 100;
    /// <summary>
    /// Texture-coloured emission floor applied on top of the normal Buddy scene lighting.
    /// Zero means fully scene-lit; 0.36 preserves the approved current appearance.
    /// </summary>
    public double LightingLevel { get; init; } = DefaultLightingLevel;
    public GeometrySettings Geometry { get; init; } = new();
    public ThumbnailSettings Thumbnail { get; init; } = new();

    public static AssetRecipe GlassesDefaults() => new();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (GeneratorVersion != CurrentGeneratorVersion) errors.Add($"Unsupported generator version {GeneratorVersion}.");
        if (PresetId != "glasses" || PresetVersion is not 1 and not 2) errors.Add("Current Asset Forge supports glasses@1 and glasses@2.");
        if (AssetFamily != AssetFamily.BuddyStudio || Category != AssetCategory.Glasses) errors.Add("Current Asset Forge exports Buddy Studio glasses only.");
        if (!StableId(FeatureId) || !FeatureId.StartsWith("glasses.", StringComparison.Ordinal)) errors.Add("FeatureId must be a stable lowercase glasses.* ID.");
        if (!StableId(ContentId) || !ContentId.StartsWith("cosmetic.glasses.", StringComparison.Ordinal)) errors.Add("ContentId must be a stable lowercase cosmetic.glasses.* ID.");
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 80) errors.Add("DisplayName must contain 1-80 characters.");
        if (!string.Equals(SourceFile, "source.png", StringComparison.Ordinal)) errors.Add("Recipe source must be source.png.");
        if (PriceCredits <= 0 || PriceCredits > 100000) errors.Add("PriceCredits must be within 1-100000.");
        if (SortOrder < 0 || SortOrder > 100000) errors.Add("SortOrder must be within 0-100000.");
        if (!FiniteRange(LightingLevel, 0, 1)) errors.Add("LightingLevel must be within 0-1.");
        if (Geometry.GeometryResolution is < 32 or > 512 || 1024 % Geometry.GeometryResolution != 0) errors.Add("GeometryResolution must be a 32-512 divisor of 1024.");
        if (Geometry.RuntimeTextureResolution is < 64 or > 1024 || 1024 % Geometry.RuntimeTextureResolution != 0) errors.Add("RuntimeTextureResolution must be a 64-1024 divisor of 1024.");
        if (!FiniteRange(Geometry.AlphaThreshold, 0.01, 0.99)) errors.Add("AlphaThreshold must be within 0.01-0.99.");
        if (Geometry.ThicknessBiasPixels is < -8 or > 8) errors.Add("ThicknessBiasPixels must be within -8..8.");
        if (!FiniteRange(Geometry.FrameThickness, 0.01, 0.25)) errors.Add("FrameThickness must be within 0.01-0.25.");
        if (Geometry.BridgeThicknessBiasPixels is < -24 or > 24) errors.Add("BridgeThicknessBiasPixels must be within -24..24.");
        if (!FiniteRange(Geometry.Depth, 0.01, 1.0)) errors.Add("Depth must be within 0.01-1.0.");
        if (!FiniteRange(Geometry.Roundness, 0, 1)) errors.Add("Roundness must be within 0-1.");
        if (Geometry.ShapeMode is not ShapeMode.FlatExtrusion and not ShapeMode.RoundedExtrusion)
            errors.Add("Glasses presets support only FlatExtrusion and RoundedExtrusion shape modes.");
        if (!FiniteRange(Geometry.TempleThickness, 0.01, 0.3)) errors.Add("TempleThickness must be within 0.01-0.3.");
        if (!FiniteRange(Geometry.TempleLength, 0.05, 1.5)) errors.Add("TempleLength must be within 0.05-1.5.");
        if (!FiniteRange(Geometry.TempleDrop, -0.5, 0.5)) errors.Add("TempleDrop must be within -0.5-0.5.");
        if (!FiniteRange(Thumbnail.Padding, 0, 0.45)) errors.Add("Thumbnail padding must be within 0-0.45.");
        return errors;
    }

    private static bool StableId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96) return false;
        foreach (char c in value)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_')) return false;
        return !value.StartsWith('.') && !value.EndsWith('.') && !value.Contains("..", StringComparison.Ordinal);
    }

    private static bool FiniteRange(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
}

public static class RecipeCodec
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static AssetRecipe Read(string json)
    {
        AssetRecipe recipe = JsonSerializer.Deserialize<AssetRecipe>(json, ReadOptions)
            ?? throw new FormatException("Recipe JSON was empty.");
        IReadOnlyList<string> errors = recipe.Validate();
        if (errors.Count > 0) throw new FormatException(string.Join("; ", errors));
        return recipe;
    }

    public static string WriteCanonical(AssetRecipe recipe)
    {
        IReadOnlyList<string> errors = recipe.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors), nameof(recipe));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("generatorVersion", recipe.GeneratorVersion);
            writer.WriteString("presetId", recipe.PresetId);
            writer.WriteNumber("presetVersion", recipe.PresetVersion);
            writer.WriteNumber("assetFamily", (int)recipe.AssetFamily);
            writer.WriteNumber("category", (int)recipe.Category);
            writer.WriteString("featureId", recipe.FeatureId);
            writer.WriteString("contentId", recipe.ContentId);
            writer.WriteString("displayName", recipe.DisplayName);
            writer.WriteString("sourceFile", recipe.SourceFile);
            writer.WriteNumber("priceCredits", recipe.PriceCredits);
            writer.WriteNumber("sortOrder", recipe.SortOrder);
            writer.WriteNumber("lightingLevel", recipe.LightingLevel);
            writer.WritePropertyName("geometry");
            writer.WriteStartObject();
            GeometrySettings g = recipe.Geometry;
            writer.WriteNumber("geometryResolution", g.GeometryResolution);
            writer.WriteNumber("alphaThreshold", g.AlphaThreshold);
            writer.WriteNumber("thicknessBiasPixels", g.ThicknessBiasPixels);
            writer.WriteNumber("frameThickness", g.FrameThickness);
            // Keep default-valued legacy/current recipes byte-canonical: bridge thickness is an
            // optional glasses@2 authoring refinement and zero means the pre-existing behavior.
            if (g.BridgeThicknessBiasPixels != 0)
                writer.WriteNumber("bridgeThicknessBiasPixels", g.BridgeThicknessBiasPixels);
            writer.WriteNumber("depth", g.Depth);
            writer.WriteNumber("roundness", g.Roundness);
            writer.WriteNumber("shapeMode", (int)g.ShapeMode);
            writer.WriteNumber("symmetryMode", (int)g.SymmetryMode);
            writer.WriteNumber("runtimeTextureResolution", g.RuntimeTextureResolution);
            writer.WriteNumber("templeThickness", g.TempleThickness);
            writer.WriteNumber("templeLength", g.TempleLength);
            writer.WriteNumber("templeDrop", g.TempleDrop);
            writer.WriteEndObject();
            writer.WritePropertyName("thumbnail");
            writer.WriteStartObject();
            writer.WriteNumber("yawDegrees", recipe.Thumbnail.YawDegrees);
            writer.WriteNumber("pitchDegrees", recipe.Thumbnail.PitchDegrees);
            writer.WriteNumber("padding", recipe.Thumbnail.Padding);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    public static string Hash(AssetRecipe recipe) => Hashing.Sha256Hex(Encoding.UTF8.GetBytes(WriteCanonical(recipe)));
}

public static class Hashing
{
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Sha256Hex(string value) => Sha256Hex(Encoding.UTF8.GetBytes(value));
}
