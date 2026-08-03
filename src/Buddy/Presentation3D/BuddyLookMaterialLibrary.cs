using System;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Builds the shipping soft-toon materials from a <see cref="BuddyLookProfile"/>. Owns no
/// scene nodes: the presenter asks for one lit material per part/connector mesh and a single
/// shared unshaded ink outline material at initialization, then reuses those references every
/// frame. Nothing here is called from <c>_Process</c>, so the render path never creates or
/// mutates a material Resource (M3_5_MATERIALS_AND_LOOK_PLAN.md L2, global constraint 4 /
/// ARCHITECTURE §23 allocation policy).
///
/// Every mesh gets its OWN lit material instance even when albedos match: a future per-part
/// mutation (damage flash, character-editor recolor preview) must never recolor an unrelated
/// part or connector through a shared instance. Only the outline ink material is shared —
/// outline shells are never tinted per part.
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
    /// Returns a NEW soft-toon <see cref="StandardMaterial3D"/> whose albedo is
    /// <paramref name="albedo"/>. Called once per mesh at build time; each mesh owns its
    /// instance so per-part tinting can never bleed across parts or connectors.
    /// </summary>
    public StandardMaterial3D CreateLitMaterial(Color albedo) => new()
    {
        ResourceName = "BuddyLookLitMaterial",
        AlbedoColor = albedo,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        DiffuseMode = _look.DiffuseMode,
        SpecularMode = _look.SpecularMode,
        MetallicSpecular = _look.Specular,
        Roughness = _look.Roughness,
        Metallic = 0.0f,
    };

    /// <summary>
    /// World units the paint shell grows clear of its body mesh. It must stay below the
    /// trusted face/accent plate epsilon so decals keep rendering above paint, and above zero
    /// so the shell never z-fights the body. Both cameras are orthographic, so depth is linear
    /// and this margin is ample — raise it only if the shell shows through a plate.
    /// </summary>
    public const float PaintShellGrowAmount = 0.05f;

    /// <summary>
    /// Returns a NEW paint-shell material. Albedo stays white so the painted pixels keep the
    /// colour the player picked; the scissor discards blank pixels so the base colour shows
    /// through. Painted pixels are fully opaque by construction, so no alpha sorting is needed.
    /// </summary>
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
