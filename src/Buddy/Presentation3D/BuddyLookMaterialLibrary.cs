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

    /// <summary>Returns a new accepted soft-toon material with the supplied solid albedo.</summary>
    public StandardMaterial3D CreateLitMaterial(Color albedo) =>
        BuddySharedMaterialFactory.CreateLitMaterial(_look, albedo);

    /// <summary>
    /// Generated cosmetics use this seam: authored PNG colour stays authoritative, alpha has
    /// already become geometry, and the same opaque generated-asset lighting contract used by
    /// Asset Forge is applied at runtime.
    /// </summary>
    public StandardMaterial3D CreateLitTexturedMaterial(Texture2D albedo, Color modulation) =>
        BuddySharedMaterialFactory.CreateGeneratedAssetMaterial(_look, albedo, modulation);

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

    public StandardMaterial3D OutlineMaterial =>
        _outline ??= BuddySharedMaterialFactory.CreateOutlineMaterial(_look);
}
