using System;
using DesktopBuddy.Buddy.Presentation3D.Shared;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Builds the shipping soft-toon materials from a <see cref="BuddyLookProfile"/>. Construction
/// delegates to DesktopBuddy.Visuals so the game and developer Asset Forge preview cannot drift.
/// </summary>
public sealed class BuddyLookMaterialLibrary
{
    private const float GeneratedCosmeticEmissionFloor = 0.36f;
    private const string GeneratedCosmeticLightingMetadataKey = "desktop_buddy_asset_forge_lighting_level";
    private readonly BuddySharedLook _look;
    private StandardMaterial3D? _outline;

    public BuddyLookMaterialLibrary(BuddyLookProfile look)
    {
        if (!GodotObject.IsInstanceValid(look))
            throw new InvalidOperationException("BuddyLookMaterialLibrary requires a valid look profile.");

        _look = new BuddySharedLook(
            look.DiffuseMode,
            look.SpecularMode,
            look.Specular,
            look.Roughness,
            look.KeyColor,
            look.KeyEnergy,
            look.KeyEulerDegrees,
            look.FillColor,
            look.FillEnergy,
            look.FillEulerDegrees,
            look.ShadowsEnabled,
            look.OutlineColor,
            look.OutlineGrowAmount);
    }

    public StandardMaterial3D CreateLitMaterial(Color albedo) =>
        BuddySharedMaterialFactory.CreateLitMaterial(_look, albedo);

    public StandardMaterial3D CreateLitTexturedMaterial(Texture2D albedo, Color modulation)
    {
        StandardMaterial3D material =
            BuddySharedMaterialFactory.CreateGeneratedAssetMaterial(_look, albedo, modulation);
        material.AlbedoTextureForceSrgb = true;

        float lightingLevel = GeneratedCosmeticEmissionFloor;
        if (albedo.HasMeta(GeneratedCosmeticLightingMetadataKey))
        {
            double authored = albedo.GetMeta(GeneratedCosmeticLightingMetadataKey).AsDouble();
            if (double.IsFinite(authored))
                lightingLevel = Math.Clamp((float)authored, 0f, 1f);
        }
        material.EmissionEnergyMultiplier = lightingLevel;
        return material;
    }

    public const float PaintShellGrowAmount = 0.05f;

    public StandardMaterial3D CreatePaintMaterial()
    {
        StandardMaterial3D material = CreateLitMaterial(Colors.White);
        material.ResourceName = "BuddyLookPaintMaterial";
        material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
        material.AlphaScissorThreshold = 0.5f;
        material.Grow = true;
        material.GrowAmount = PaintShellGrowAmount;
        return material;
    }

    /// <summary>
    /// Generated replacements use a uniformly enlarged back-face outline shell instead of normal-
    /// based material Grow. The latter self-intersects on pixel-derived sidewalls and shows up as
    /// dark horizontal stripes. Uniform shell scaling preserves the outer Buddy silhouette without
    /// creating internal sidewall bands.
    /// </summary>
    public StandardMaterial3D CreateScaledOutlineMaterial(float meshScale)
    {
        _ = SafeMeshScale(meshScale);
        StandardMaterial3D material = BuddySharedMaterialFactory.CreateOutlineMaterial(_look);
        material.ResourceName = "BuddyLookScaledOutlineMaterial";
        material.Grow = false;
        material.GrowAmount = 0f;
        return material;
    }

    public float ReplacementOutlineScale(float meshScale)
    {
        float scale = SafeMeshScale(meshScale);
        return 1f + (_look.OutlineGrowAmount / scale);
    }

    private static float SafeMeshScale(float meshScale)
    {
        if (!float.IsFinite(meshScale) || meshScale <= 0.0001f)
            throw new ArgumentOutOfRangeException(nameof(meshScale), meshScale, "Generated mesh scale must be finite and positive.");
        return meshScale;
    }

    public StandardMaterial3D OutlineMaterial =>
        _outline ??= BuddySharedMaterialFactory.CreateOutlineMaterial(_look);
}
