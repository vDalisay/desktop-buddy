using System;
using System.Linq;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

[GlobalClass]
public partial class GeneratedBuddyCosmeticResource : GameResource
{
    public const float DefaultLightingLevel = 0.36f;
    public const string LightingMetadataKey = "desktop_buddy_asset_forge_lighting_level";

    private Texture2D? _albedoTexture;
    private float _lightingLevel = DefaultLightingLevel;

    [Export] public string FeatureId { get; set; } = string.Empty;
    [Export] public string ContentId { get; set; } = string.Empty;
    [Export] public string DisplayName { get; set; } = string.Empty;
    [Export] public CharacterFeatureSlot Slot { get; set; } = CharacterFeatureSlot.Glasses;
    [Export(PropertyHint.Range, "0,100000,1")] public int SortOrder { get; set; }
    [Export] public PackedScene? MeshScene { get; set; }

    [Export]
    public Texture2D? AlbedoTexture
    {
        get => _albedoTexture;
        set
        {
            _albedoTexture = value;
            ApplyLightingMetadata();
        }
    }

    [Export] public Texture2D? Thumbnail { get; set; }

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float LightingLevel
    {
        get => _lightingLevel;
        set
        {
            _lightingLevel = value;
            ApplyLightingMetadata();
        }
    }

    [Export] public int GeneratorVersion { get; set; } = 1;
    [Export] public string CanonicalAssetHash { get; set; } = string.Empty;

    public CosmeticDefinition ToDefinition()
    {
        if (Slot != CharacterFeatureSlot.Glasses)
            throw new InvalidOperationException("Asset Forge v1 generated runtime supports Glasses only.");
        return new CosmeticDefinition(
            FeatureId,
            Slot,
            DisplayName,
            SortOrder,
            isFreeDefault: false,
            CosmeticTransformPolicy.MoveAndUniformScale,
            CosmeticTransformBounds.Standard,
            NormalizedFeatureTransform.Identity,
            colorChannels: [],
            CharacterFeatureCatalog.Shipped.GetDefaultId(Slot),
            ownershipContentId: ContentId);
    }

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        try { _ = ToDefinition(); }
        catch (Exception exception) { errors.Add(exception.Message); }
        if (!FeatureId.StartsWith("glasses.", StringComparison.Ordinal)) errors.Add("Generated glasses feature ID must use glasses.*.");
        if (!ContentId.StartsWith("cosmetic.glasses.", StringComparison.Ordinal)) errors.Add("Generated glasses content ID must use cosmetic.glasses.*.");
        if (string.IsNullOrWhiteSpace(DisplayName)) errors.Add("Generated cosmetic display name is required.");
        if (!GodotObject.IsInstanceValid(MeshScene)) errors.Add($"'{FeatureId}' is missing its generated GLB scene.");
        if (!GodotObject.IsInstanceValid(AlbedoTexture)) errors.Add($"'{FeatureId}' is missing its generated albedo.");
        if (!GodotObject.IsInstanceValid(Thumbnail)) errors.Add($"'{FeatureId}' is missing its generated thumbnail.");
        if (!float.IsFinite(LightingLevel) || LightingLevel is < 0f or > 1f)
            errors.Add($"'{FeatureId}' has invalid lighting level {LightingLevel}.");
        if (GeneratorVersion != 1) errors.Add($"'{FeatureId}' uses unsupported generator version {GeneratorVersion}.");
        if (CanonicalAssetHash.Length != 64 || CanonicalAssetHash.Any(static c => !Uri.IsHexDigit(c)))
            errors.Add($"'{FeatureId}' has an invalid canonical asset hash.");
        ApplyLightingMetadata();
        return errors;
    }

    private void ApplyLightingMetadata()
    {
        if (!GodotObject.IsInstanceValid(_albedoTexture)) return;
        float safe = float.IsFinite(_lightingLevel) ? Math.Clamp(_lightingLevel, 0f, 1f) : DefaultLightingLevel;
        _albedoTexture!.SetMeta(LightingMetadataKey, safe);
    }
}
