using System;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Builds the shipping soft-toon materials from a <see cref="BuddyLookProfile"/>. Owns no
/// scene nodes: the presenter asks for one lit material per part/connector mesh and a single
/// shared unshaded ink outline material at initialization, then reuses those references every
/// frame. Every mesh gets its own lit material instance so per-part tinting and scorch never
/// bleed across parts.
/// </summary>
public sealed class BuddyLookMaterialLibrary
{
    private readonly BuddyLookProfile _look;
    private StandardMaterial3D? _outline;

    public BuddyLookMaterialLibrary(BuddyLookProfile look)
    {
        if (!GodotObject.IsInstanceValid(look))
        {
            throw new InvalidOperationException(
                "BuddyLookMaterialLibrary requires a valid look profile.");
        }

        _look = look;
    }

    /// <summary>
    /// Returns a new soft-toon material. The neutral fabric texture is multiplied by the
    /// supplied albedo, preserving character-editor recolouring while giving every body part
    /// the same woven-cloth surface language.
    /// </summary>
    public StandardMaterial3D CreateLitMaterial(Color albedo) => new()
    {
        ResourceName = "BuddyLookLitMaterial",
        AlbedoColor = albedo,
        AlbedoTexture = _look.FabricTexture,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        DiffuseMode = _look.DiffuseMode,
        SpecularMode = _look.SpecularMode,
        MetallicSpecular = _look.Specular,
        Roughness = _look.Roughness,
        Metallic = 0.0f,
    };

    /// <summary>
    /// World units the paint shell grows clear of its body mesh. It must stay below the
    /// trusted face/accent plate epsilon so decals keep rendering above paint.
    /// </summary>
    public const float PaintShellGrowAmount = 0.05f;

    /// <summary>
    /// Returns a new paint-shell material. It deliberately clears the trusted fabric texture:
    /// paint pixels are already authored RGBA colour and blank pixels must reveal the fabric
    /// base underneath.
    /// </summary>
    public StandardMaterial3D CreatePaintMaterial()
    {
        StandardMaterial3D material = CreateLitMaterial(Colors.White);
        material.ResourceName = "BuddyLookPaintMaterial";
        material.AlbedoTexture = null;
        material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
        material.AlphaScissorThreshold = 0.5f;
        material.Grow = true;
        material.GrowAmount = PaintShellGrowAmount;
        return material;
    }

    /// <summary>
    /// Returns a new seam shell material. The transparent stitches render above player paint
    /// but below the trusted face and torso-accent plates.
    /// </summary>
    public StandardMaterial3D CreateSeamMaterial()
    {
        StandardMaterial3D material = CreateLitMaterial(Colors.White);
        material.ResourceName = "BuddyLookSeamMaterial";
        material.AlbedoTexture = _look.SeamTexture;
        material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
        material.AlphaScissorThreshold = 0.2f;
        material.Grow = true;
        material.GrowAmount = _look.SeamGrowAmount;
        return material;
    }

    /// <summary>The one shared unshaded ink material used by every inverted-hull outline shell.</summary>
    public StandardMaterial3D OutlineMaterial => _outline ??= CreateOutlineMaterial();

    private StandardMaterial3D CreateOutlineMaterial() => new()
    {
        ResourceName = "BuddyLookOutlineMaterial",
        AlbedoColor = _look.OutlineColor,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Front,
        Grow = true,
        GrowAmount = _look.OutlineGrowAmount,
    };
}
