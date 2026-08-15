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
    /// Replacement/environment smoothing strength. Zero intentionally preserves the original v1
    /// replacement Manhattan depth field so old exported Buddy recipes can regenerate byte-for-byte.
    /// Replacement authoring supports values above 1 for additional deterministic relaxation passes.
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
    /// <summary>
    /// Logical in-room reference height. Legacy Lamp@1 applies it to the visible silhouette;
    /// literal template-space Environment presets apply it to their category reference envelope.
    /// </summary>
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
    /// <summary>Generic stable ID for Environment assets. Buddy v1 uses FeatureId/ContentId.</summary>
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

    /// <summary>
    /// New Lamp recipes use lamp@3. lamp@1 keeps legacy visible-bounds auto-fit, lamp@2 keeps the
    /// accepted v0.1 literal-template mesh behavior, and lamp@3 adds full-resolution rim projection
    /// plus bounded fairing without silently changing already-authored recipes.
    /// </summary>
    public static AssetRecipe LampDefaults() => new()
    {
        PresetId = "lamp",
        PresetVersion = 3,
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
            Roundness = 0.90,
            SurfaceSmoothness = 1.0,
            ShapeMode = ShapeMode.InflatedSolid,
            SymmetryMode = SymmetryMode.Off,
        },
        Environment = FloorEnvironment(150),
        Light = new DecorationLightSettings
        {
            EmitterX = EnvironmentTemplateSpace.LampEmitterX / (double)EnvironmentTemplateSpace.CanvasSize,
            EmitterY = EnvironmentTemplateSpace.LampEmitterY / (double)EnvironmentTemplateSpace.CanvasSize,
        },
        Thumbnail = new ThumbnailSettings { YawDegrees = 10, PitchDegrees = -6, Padding = .10 },
    };

    /// <summary>
    /// sofa@1 remains the accepted v0.1 literal-template generator. sofa@2 keeps the same authored
    /// coordinate contract and adds the shared full-resolution Environment silhouette polisher.
    /// </summary>
    public static AssetRecipe SofaDefaults() => new()
    {
        PresetId = "sofa",
        PresetVersion = 2,
        AssetFamily = AssetFamily.Environment,
        Category = AssetCategory.Sofa,
        AssetId = "decoration.sofa.new_asset",
        FeatureId = string.Empty,
        ContentId = string.Empty,
        DisplayName = "New Sofa",
        PriceCredits = 180,
        Geometry = new GeometrySettings
        {
            GeometryResolution = 256,
            RuntimeTextureResolution = 512,
            Depth = 0.34,
            Roundness = 0.92,
            SurfaceSmoothness = 0.90,
            ShapeMode = ShapeMode.InflatedSolid,
            SymmetryMode = SymmetryMode.Off,
        },
        Environment = FloorEnvironment(105),
        Light = DisabledLight(),
        Thumbnail = new ThumbnailSettings { YawDegrees = 10, PitchDegrees = -8, Padding = .10 },
    };

    public static AssetRecipe TableDefaults() => new()
    {
        PresetId = "table",
        PresetVersion = 1,
        AssetFamily = AssetFamily.Environment,
        Category = AssetCategory.Table,
        AssetId = "decoration.table.new_asset",
        FeatureId = string.Empty,
        ContentId = string.Empty,
        DisplayName = "New Table",
        PriceCredits = 150,
        Geometry = new GeometrySettings
        {
            GeometryResolution = 256,
            RuntimeTextureResolution = 512,
            Depth = 0.24,
            Roundness = 0.72,
            SurfaceSmoothness = 0.90,
            ShapeMode = ShapeMode.RoundedExtrusion,
            SymmetryMode = SymmetryMode.Off,
        },
        Environment = FloorEnvironment(100),
        Light = DisabledLight(),
        Thumbnail = new ThumbnailSettings { YawDegrees = 12, PitchDegrees = -8, Padding = .11 },
    };

    public static AssetRecipe PlantDefaults() => new()
    {
        PresetId = "plant",
        PresetVersion = 1,
        AssetFamily = AssetFamily.Environment,
        Category = AssetCategory.Plant,
        AssetId = "decoration.plant.new_asset",
        FeatureId = string.Empty,
        ContentId = string.Empty,
        DisplayName = "New Plant",
        PriceCredits = 110,
        Geometry = new GeometrySettings
        {
            GeometryResolution = 256,
            RuntimeTextureResolution = 512,
            Depth = 0.26,
            Roundness = 0.92,
            SurfaceSmoothness = 1.0,
            ShapeMode = ShapeMode.InflatedSolid,
            SymmetryMode = SymmetryMode.Off,
        },
        Environment = FloorEnvironment(120),
        Light = DisabledLight(),
        Thumbnail = new ThumbnailSettings { YawDegrees = 10, PitchDegrees = -7, Padding = .11 },
    };

    public static AssetRecipe PaintingDefaults() => new()
    {
        PresetId = "painting",
        PresetVersion = 1,
        AssetFamily = AssetFamily.Environment,
        Category = AssetCategory.Painting,
        AssetId = "decoration.painting.new_asset",
        FeatureId = string.Empty,
        ContentId = string.Empty,
        DisplayName = "New Painting",
        PriceCredits = 90,
        Geometry = new GeometrySettings
        {
            GeometryResolution = 256,
            RuntimeTextureResolution = 1024,
            Depth = 0.045,
            Roundness = 0,
            SurfaceSmoothness = 0.75,
            ShapeMode = ShapeMode.FlatExtrusion,
            SymmetryMode = SymmetryMode.Off,
        },
        Environment = new EnvironmentAssetSettings
        {
            LogicalHeight = 95,
            Anchor = EnvironmentAnchorMode.Wall,
            RenderMode = EnvironmentRenderMode.WallDecoration,
            AllowsRotation = false,
            RotationStepDegrees = 0,
            PivotX = 0.5,
            PivotY = 0.5,
        },
        Light = DisabledLight(),
        Thumbnail = new ThumbnailSettings { YawDegrees = 5, PitchDegrees = 0, Padding = .08 },
    };

    private static EnvironmentAssetSettings FloorEnvironment(double logicalHeight) => new()
    {
        LogicalHeight = logicalHeight,
        Anchor = EnvironmentAnchorMode.Floor,
        RenderMode = EnvironmentRenderMode.BehindBuddyFloor,
        AllowsRotation = true,
        RotationStepDegrees = 15,
        PivotX = 0.5,
        PivotY = 1.0,
    };

    private static DecorationLightSettings DisabledLight() => new()
    {
        Enabled = false,
        EmissionStrength = 0,
        LightEnabled = false,
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
                if (PresetId != "lamp" || PresetVersion is not 1 and not 2 and not 3)
                    errors.Add("Lamp recipes support lamp@1 legacy auto-fit, lamp@2 literal v0.1 generation and lamp@3 smoothed literal generation.");
                ValidateEnvironmentIdentity(errors, "Lamp", "decoration.lamp.");
                ValidateRoundedSilhouetteGeometry(errors, "Lamp");
                ValidateEnvironment(errors);
                ValidateLight(errors);
                break;

            case AssetCategory.Sofa:
                if (PresetId != "sofa" || PresetVersion is not 1 and not 2)
                    errors.Add("Sofa recipes support sofa@1 v0.1 literal generation and sofa@2 smoothed literal generation.");
                ValidateEnvironmentIdentity(errors, "Sofa", "decoration.sofa.");
                ValidateEnvironment(errors);
                ValidateRoundedSilhouetteGeometry(errors, "Sofa");
                break;

            case AssetCategory.Table:
                ValidateEnvironmentPreset(errors, "Table", "table", "decoration.table.");
                ValidateRoundedSilhouetteGeometry(errors, "Table");
                break;

            case AssetCategory.Plant:
                ValidateEnvironmentPreset(errors, "Plant", "plant", "decoration.plant.");
                ValidateRoundedSilhouetteGeometry(errors, "Plant");
                break;

            case AssetCategory.Painting:
                ValidateEnvironmentPreset(errors, "Painting", "painting", "decoration.painting.");
                if (Geometry.ShapeMode is not ShapeMode.FlatExtrusion and not ShapeMode.RoundedExtrusion and not ShapeMode.Relief)
                    errors.Add("Painting presets support FlatExtrusion, RoundedExtrusion or Relief shape modes.");
                if (Geometry.BridgeThicknessBiasPixels != 0)
                    errors.Add("BridgeThicknessBiasPixels is glasses-only and must be zero for Painting.");
                break;

            default:
                errors.Add($"Asset category {Category} has a template contract but its generator is not implemented yet.");
                break;
        }

        if (Category is AssetCategory.Sofa or AssetCategory.Table or AssetCategory.Plant or AssetCategory.Painting)
            ValidateNonLightEnvironment(errors);

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

    private void ValidateEnvironmentPreset(List<string> errors, string label, string presetId, string idPrefix)
    {
        if (PresetId != presetId || PresetVersion != 1)
            errors.Add($"{label} currently supports {presetId}@1 only.");
        ValidateEnvironmentIdentity(errors, label, idPrefix);
        ValidateEnvironment(errors);
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

    private void ValidateEnvironmentIdentity(List<string> errors, string label, string idPrefix)
    {
        if (AssetFamily != AssetFamily.Environment) errors.Add($"{label} must use the Environment asset family.");
        if (!StableId(AssetId) || !AssetId.StartsWith(idPrefix, StringComparison.Ordinal))
            errors.Add($"{label} AssetId must be a stable lowercase {idPrefix}* ID.");
        if (!string.IsNullOrEmpty(FeatureId) || !string.IsNullOrEmpty(ContentId))
            errors.Add("Environment recipes do not use Buddy FeatureId/ContentId.");
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
        if (!Enum.IsDefined(Environment.Anchor)) errors.Add("Environment anchor is invalid.");
        if (!Enum.IsDefined(Environment.RenderMode)) errors.Add("Environment render mode is invalid.");
        if (Environment.AllowsRotation)
        {
            if (Environment.RotationStepDegrees <= 0 || Environment.RotationStepDegrees >= 360 || 360 % Environment.RotationStepDegrees != 0)
                errors.Add("Environment rotation step must divide 360 degrees exactly.");
        }
        else if (Environment.RotationStepDegrees != 0) errors.Add("Fixed Environment assets must use a zero rotation step.");
        if (!FiniteRange(Environment.PivotX, 0, 1) || !FiniteRange(Environment.PivotY, 0, 1)) errors.Add("Environment pivot must use normalized 0..1 values.");

        if (Category == AssetCategory.Lamp && PresetVersion == 1 &&
            (Environment.Anchor != EnvironmentAnchorMode.Floor || Math.Abs(Environment.PivotY - 1.0) > .000001))
            errors.Add("Lamp@1 requires a floor anchor and bottom floor pivot.");

        if ((Category == AssetCategory.Lamp && PresetVersion >= 2) ||
            Category is AssetCategory.Sofa or AssetCategory.Table or AssetCategory.Plant)
        {
            if (Environment.Anchor != EnvironmentAnchorMode.Floor ||
                Math.Abs(Environment.PivotX - 0.5) > .000001 ||
                Math.Abs(Environment.PivotY - 1.0) > .000001)
                errors.Add($"{PresetId}@{PresetVersion} requires the literal template bottom-centre floor pivot.");
        }

        if (Category == AssetCategory.Painting)
        {
            if (Environment.Anchor != EnvironmentAnchorMode.Wall ||
                Environment.RenderMode != EnvironmentRenderMode.WallDecoration ||
                Math.Abs(Environment.PivotX - 0.5) > .000001 ||
                Math.Abs(Environment.PivotY - 0.5) > .000001)
                errors.Add("painting@1 requires the literal template centre wall pivot and WallDecoration render mode.");
        }
    }

    private void ValidateNonLightEnvironment(List<string> errors)
    {
        if (Light.Enabled || Light.LightEnabled || Math.Abs(Light.EmissionStrength) > .000001)
            errors.Add($"{PresetId}@{PresetVersion} is non-emissive and may not create a local light.");
    }

    private void ValidateLight(List<string> errors)
    {
        if (!FiniteRange(Light.EmissionStrength, 0, 8)) errors.Add("Lamp emission strength must be within 0-8.");
        if (!FiniteRange(Light.Brightness, 0, 16)) errors.Add("Lamp brightness must be within 0-16.");
        if (!FiniteRange(Light.Range, 1, 1024)) errors.Add("Lamp light range must be within 1-1024.");
        if (!FiniteRange(Light.EmitterX, 0, 1) || !FiniteRange(Light.EmitterY, 0, 1)) errors.Add("Lamp emitter position must use normalized 0..1 values.");
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
            if (!string.IsNullOrEmpty(recipe.AssetId)) writer.WriteString("assetId", recipe.AssetId);
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
            if (g.BridgeThicknessBiasPixels != 0)
                writer.WriteNumber("bridgeThicknessBiasPixels", g.BridgeThicknessBiasPixels);
            writer.WriteNumber("depth", g.Depth);
            writer.WriteNumber("roundness", g.Roundness);
            if (g.SurfaceSmoothness != 0)
                writer.WriteNumber("surfaceSmoothness", g.SurfaceSmoothness);
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

            if (recipe.AssetFamily == AssetFamily.Environment)
            {
                writer.WritePropertyName("environment");
                writer.WriteStartObject();
                writer.WriteNumber("logicalHeight", recipe.Environment.LogicalHeight);
                writer.WriteNumber("anchor", (int)recipe.Environment.Anchor);
                writer.WriteNumber("renderMode", (int)recipe.Environment.RenderMode);
                writer.WriteBoolean("allowsRotation", recipe.Environment.AllowsRotation);
                writer.WriteNumber("rotationStepDegrees", recipe.Environment.RotationStepDegrees);
                writer.WriteNumber("pivotX", recipe.Environment.PivotX);
                writer.WriteNumber("pivotY", recipe.Environment.PivotY);
                writer.WriteEndObject();

                writer.WritePropertyName("light");
                writer.WriteStartObject();
                writer.WriteBoolean("enabled", recipe.Light.Enabled);
                writer.WriteNumber("emissionStrength", recipe.Light.EmissionStrength);
                writer.WriteBoolean("lightEnabled", recipe.Light.LightEnabled);
                writer.WriteNumber("brightness", recipe.Light.Brightness);
                writer.WriteNumber("range", recipe.Light.Range);
                writer.WriteNumber("red", recipe.Light.Red);
                writer.WriteNumber("green", recipe.Light.Green);
                writer.WriteNumber("blue", recipe.Light.Blue);
                writer.WriteNumber("emitterX", recipe.Light.EmitterX);
                writer.WriteNumber("emitterY", recipe.Light.EmitterY);
                writer.WriteEndObject();
            }
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
