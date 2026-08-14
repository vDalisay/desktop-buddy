using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopBuddy.AssetForge.Core;

public enum AssetFamily { BuddyStudio = 0, Environment = 1 }
public enum AssetCategory { Glasses = 0, TorsoShape = 1, FootShape = 2, Lamp = 10, Sofa = 11, Table = 12, Plant = 13, Painting = 14 }
public enum ShapeMode { FlatExtrusion = 0, RoundedExtrusion = 1, InflatedSolid = 2, Relief = 3 }
public enum SymmetryMode { Off = 0, MirrorLeftToRight = 1, MirrorRightToLeft = 2, AverageBothSides = 3 }
public enum EnvironmentAnchorMode { Floor = 0, Wall = 1 }
public enum EnvironmentRenderMode { BehindBuddyFloor = 0, FrontDecoration = 1, WallDecoration = 2 }

public sealed record GeometrySettings
{
    public int GeometryResolution { get; init; } = 512;
    public double AlphaThreshold { get; init; } = 0.50;
    public int ThicknessBiasPixels { get; init; }
    public double FrameThickness { get; init; } = 0.055;
    public int BridgeThicknessBiasPixels { get; init; }
    public double Depth { get; init; } = 0.065;
    public double Roundness { get; init; } = 0.85;
    /// <summary>
    /// Replacement/environment depth-field relaxation amount. Zero intentionally preserves the
    /// original v1 replacement Manhattan field so old exported Buddy recipes regenerate byte-for-byte.
    /// 0..1 is the normal authored range; 1..3 deliberately applies additional smoothing passes for
    /// very soft/plush silhouettes without moving the authored XY silhouette or changing physics.
    /// </summary>
    public double SurfaceSmoothness { get; init; }
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

public sealed record EnvironmentAssetSettings
{
    public double LogicalHeight { get; init; } = 150;
    public EnvironmentAnchorMode Anchor { get; init; } = EnvironmentAnchorMode.Floor;
    public EnvironmentRenderMode RenderMode { get; init; } = EnvironmentRenderMode.BehindBuddyFloor;
    public bool AllowsRotation { get; init; } = true;
    public int RotationStepDegrees { get; init; } = 15;
    public double PivotX { get; init; } = 0.5;
    public double PivotY { get; init; } = 1.0;
}

public sealed record DecorationLightSettings
{
    public bool Enabled { get; init; } = true;
    public double EmissionStrength { get; init; } = 1.25;
    public bool LightEnabled { get; init; } = true;
    public double Brightness { get; init; } = 1.0;
    public double Range { get; init; } = 180;
    public byte Red { get; init; } = 255;
    public byte Green { get; init; } = 224;
    public byte Blue { get; init; } = 176;
    public double EmitterX { get; init; } = 0.5;
    public double EmitterY { get; init; } = 0.25;
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
    public string AssetId { get; init; } = string.Empty;
    public string FeatureId { get; init; } = "glasses.new_asset";
    public string ContentId { get; init; } = "cosmetic.glasses.new_asset";
    public string DisplayName { get; init; } = "New Glasses";
    public string SourceFile { get; init; } = "source.png";
    public int PriceCredits { get; init; } = 100;
    public int SortOrder { get; init; } = 100;
    public double LightingLevel { get; init; } = DefaultLightingLevel;
    public GeometrySettings Geometry { get; init; } = new();
    public ThumbnailSettings Thumbnail { get; init; } = new();
    public EnvironmentAssetSettings Environment { get; init; } = new();
    public DecorationLightSettings Light { get; init; } = new();

    public static AssetRecipe GlassesDefaults() => new();

    public static AssetRecipe TorsoShapeDefaults() => new()
    {
        PresetId = "torso_shape",
        PresetVersion = 1,
        Category = AssetCategory.TorsoShape,
        FeatureId = "top.new_asset",
        ContentId = "cosmetic.top.new_asset",
        DisplayName = "New Torso Shape",
        Geometry = new GeometrySettings
        {
            // 256 produced ~69k triangles for the user's test torso. 128 keeps the authored contour
            // and soft depth profile while reducing runtime/paint cost by roughly four times.
            GeometryResolution = 128,
            RuntimeTextureResolution = 512,
            Depth = 1.10,
            Roundness = 0.90,
            SurfaceSmoothness = 1.0,
            ShapeMode = ShapeMode.InflatedSolid,
            SymmetryMode = SymmetryMode.Off,
        },
    };

    public static AssetRecipe FootShapeDefaults() => new()
    {
        PresetId = "foot_shape",
        PresetVersion = 1,
        Category = AssetCategory.FootShape,
        FeatureId = "shoes.new_asset",
        ContentId = "cosmetic.shoes.new_asset",
        DisplayName = "New Foot Shape",
        Geometry = new GeometrySettings
        {
            GeometryResolution = 128,
            RuntimeTextureResolution = 512,
            Depth = 1.20,
            Roundness = 0.90,
            SurfaceSmoothness = 1.0,
            ShapeMode = ShapeMode.InflatedSolid,
            SymmetryMode = SymmetryMode.Off,
        },
    };

    public static AssetRecipe LampDefaults() => new()
    {
        PresetId = "lamp",
        PresetVersion = 1,
        AssetFamily = AssetFamily.Environment,
        Category = AssetCategory.Lamp,
        AssetId = "decoration.lamp.new_asset",
        FeatureId = string.Empty,
        ContentId = string.Empty,
        DisplayName = "New Lamp",
        PriceCredits = 120,
        Geometry = new GeometrySettings
        {
            GeometryResolution = 256,
            RuntimeTextureResolution = 512,
            Depth = 0.18,
            Roundness = 0.88,
            SurfaceSmoothness = 0.82,
            ShapeMode = ShapeMode.RoundedExtrusion,
            SymmetryMode = SymmetryMode.Off,
        },
        Environment = new EnvironmentAssetSettings
        {
            LogicalHeight = 150,
            Anchor = EnvironmentAnchorMode.Floor,
            RenderMode = EnvironmentRenderMode.BehindBuddyFloor,
            AllowsRotation = true,
            RotationStepDegrees = 15,
            PivotX = 0.5,
            PivotY = 1.0,
        },
        Light = new DecorationLightSettings(),
        Thumbnail = new ThumbnailSettings { YawDegrees = 10, PitchDegrees = -6, Padding = .10 },
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (GeneratorVersion != CurrentGeneratorVersion) errors.Add($"Unsupported generator version {GeneratorVersion}.");

        switch (Category)
        {
            case AssetCategory.Glasses:
                if (PresetId != "glasses" || PresetVersion is not 1 and not 2)
                    errors.Add("Glasses recipes support glasses@1 and glasses@2.");
                ValidateBuddyIdentity(errors, AssetFamily.BuddyStudio, "glasses.", "cosmetic.glasses.");
                if (Geometry.ShapeMode is not ShapeMode.FlatExtrusion and not ShapeMode.RoundedExtrusion)
                    errors.Add("Glasses presets support only FlatExtrusion and RoundedExtrusion shape modes.");
                if (!FiniteRange(Geometry.FrameThickness, 0.01, 0.25)) errors.Add("FrameThickness must be within 0.01-0.25.");
                if (Geometry.BridgeThicknessBiasPixels is < -24 or > 24) errors.Add("BridgeThicknessBiasPixels must be within -24..24.");
                if (!FiniteRange(Geometry.TempleThickness, 0.01, 0.3)) errors.Add("TempleThickness must be within 0.01-0.3.");
                if (!FiniteRange(Geometry.TempleLength, 0.05, 1.5)) errors.Add("TempleLength must be within 0.05-1.5.");
                if (!FiniteRange(Geometry.TempleDrop, -0.5, 0.5)) errors.Add("TempleDrop must be within -0.5-0.5.");
                if (Geometry.SurfaceSmoothness != 0)
                    errors.Add("SurfaceSmoothness is replacement/environment-only and must be zero for Glasses.");
                break;

            case AssetCategory.TorsoShape:
                if (PresetId != "torso_shape" || PresetVersion != 1)
                    errors.Add("Torso replacements currently support torso_shape@1 only.");
                ValidateBuddyIdentity(errors, AssetFamily.BuddyStudio, "top.", "cosmetic.top.");
                ValidateRoundedSilhouetteGeometry(errors, "Torso");
                break;

            case AssetCategory.FootShape:
                if (PresetId != "foot_shape" || PresetVersion != 1)
                    errors.Add("Foot replacements currently support foot_shape@1 only.");
                ValidateBuddyIdentity(errors, AssetFamily.BuddyStudio, "shoes.", "cosmetic.shoes.");
                ValidateRoundedSilhouetteGeometry(errors, "Foot");
                break;

            case AssetCategory.Lamp:
                if (PresetId != "lamp" || PresetVersion != 1)
                    errors.Add("Lamps currently support lamp@1 only.");
                if (AssetFamily != AssetFamily.Environment) errors.Add("Lamp must use the Environment asset family.");
                if (!StableId(AssetId) || !AssetId.StartsWith("decoration.lamp.", StringComparison.Ordinal))
                    errors.Add("Lamp AssetId must be a stable lowercase decoration.lamp.* ID.");
                if (!string.IsNullOrEmpty(FeatureId) || !string.IsNullOrEmpty(ContentId))
                    errors.Add("Environment recipes do not use Buddy FeatureId/ContentId.");
                ValidateRoundedSilhouetteGeometry(errors, "Lamp");
                ValidateEnvironment(errors);
                ValidateLight(errors);
                break;

            default:
                errors.Add($"Asset category {Category} has a template contract but its generator is not implemented yet.");
                break;
        }

        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 80) errors.Add("DisplayName must contain 1-80 characters.");
        if (!string.Equals(SourceFile, "source.png", StringComparison.Ordinal)) errors.Add("Recipe source must be source.png.");
        if (PriceCredits <= 0 || PriceCredits > 100000) errors.Add("PriceCredits must be within 1-100000.");
        if (SortOrder < 0 || SortOrder > 100000) errors.Add("SortOrder must be within 0-100000.");
        if (!FiniteRange(LightingLevel, 0, 1)) errors.Add("LightingLevel must be within 0-1.");
        if (Geometry.GeometryResolution is < 32 or > 512 || 1024 % Geometry.GeometryResolution != 0) errors.Add("GeometryResolution must be a 32-512 divisor of 1024.");
        if (Geometry.RuntimeTextureResolution is < 64 or > 1024 || 1024 % Geometry.RuntimeTextureResolution != 0) errors.Add("RuntimeTextureResolution must be a 64-1024 divisor of 1024.");
        if (!FiniteRange(Geometry.AlphaThreshold, 0.01, 0.99)) errors.Add("AlphaThreshold must be within 0.01-0.99.");
        if (Geometry.ThicknessBiasPixels is < -8 or > 8) errors.Add("ThicknessBiasPixels must be within -8..8.");
        double depthMaximum = Category is AssetCategory.TorsoShape or AssetCategory.FootShape ? 4.0 : 1.5;
        if (!FiniteRange(Geometry.Depth, 0.01, depthMaximum)) errors.Add($"Depth must be within 0.01-{depthMaximum:0.##}.");
        if (!FiniteRange(Geometry.Roundness, 0, 1)) errors.Add("Roundness must be within 0-1.");
        double smoothnessMaximum = Category is AssetCategory.TorsoShape or AssetCategory.FootShape ? 3.0 : 1.0;
        if (!FiniteRange(Geometry.SurfaceSmoothness, 0, smoothnessMaximum))
            errors.Add($"SurfaceSmoothness must be within 0-{smoothnessMaximum:0.#} for {Category}.");
        if (!FiniteRange(Thumbnail.Padding, 0, 0.45)) errors.Add("Thumbnail padding must be within 0-0.45.");
        return errors;
    }

    private void ValidateBuddyIdentity(List<string> errors, AssetFamily family, string featurePrefix, string contentPrefix)
    {
        if (AssetFamily != family) errors.Add($"{Category} must use asset family {family}.");
        if (!string.IsNullOrEmpty(AssetId)) errors.Add("Buddy recipes must not declare Environment AssetId.");
        if (!StableId(FeatureId) || !FeatureId.StartsWith(featurePrefix, StringComparison.Ordinal))
            errors.Add($"FeatureId must be a stable lowercase {featurePrefix}* ID.");
        if (!StableId(ContentId) || !ContentId.StartsWith(contentPrefix, StringComparison.Ordinal))
            errors.Add($"ContentId must be a stable lowercase {contentPrefix}* ID.");
    }

    private void ValidateRoundedSilhouetteGeometry(List<string> errors, string label)
    {
        if (Geometry.ShapeMode is not ShapeMode.RoundedExtrusion and not ShapeMode.InflatedSolid and not ShapeMode.Relief)
            errors.Add($"{label} presets support RoundedExtrusion, InflatedSolid or Relief shape modes.");
        if (Geometry.BridgeThicknessBiasPixels != 0)
            errors.Add("BridgeThicknessBiasPixels is glasses-only and must be zero for non-glasses silhouettes.");
    }

    private void ValidateEnvironment(List<string> errors)
    {
        if (!FiniteRange(Environment.LogicalHeight, 32, 600)) errors.Add("Environment LogicalHeight must be within 32-600 visual units.");
        if (Environment.RotationStepDegrees is < 1 or > 180) errors.Add("Environment RotationStepDegrees must be within 1-180.");
        if (!FiniteRange(Environment.PivotX, 0, 1) || !FiniteRange(Environment.PivotY, 0, 1)) errors.Add("Environment pivot coordinates must be normalized 0-1 values.");
    }

    private void ValidateLight(List<string> errors)
    {
        if (!FiniteRange(Light.EmissionStrength, 0, 8)) errors.Add("Light emission strength must be within 0-8.");
        if (!FiniteRange(Light.Brightness, 0, 16)) errors.Add("Light brightness must be within 0-16.");
        if (!FiniteRange(Light.Range, 1, 1000)) errors.Add("Light range must be within 1-1000.");
        if (!FiniteRange(Light.EmitterX, 0, 1) || !FiniteRange(Light.EmitterY, 0, 1)) errors.Add("Light emitter coordinates must be normalized 0-1 values.");
    }

    private static bool FiniteRange(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;

    private static bool StableId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96) return false;
        foreach (char character in value)
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-'))
                return false;
        return true;
    }
}

public static class RecipeCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string WriteCanonical(AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return JsonSerializer.Serialize(recipe, Options).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static AssetRecipe Read(string json)
    {
        AssetRecipe? recipe = JsonSerializer.Deserialize<AssetRecipe>(json, Options);
        if (recipe is null) throw new FormatException("Recipe JSON did not contain an Asset Forge recipe.");
        return recipe;
    }

    public static string Hash(AssetRecipe recipe) =>
        Hashing.Sha256Hex(Encoding.UTF8.GetBytes(WriteCanonical(recipe)));
}

public static class Hashing
{
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
