using System;
using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Builds and caches the shipping soft-toon materials from a <see cref="BuddyLookProfile"/>.
/// Owns no scene nodes: the presenter asks for a lit material per part/connector albedo and a
/// single shared unshaded ink outline material at initialization, then reuses those cached
/// references every frame. Nothing here is called from <c>_Process</c>, so the render path
/// never creates or mutates a material Resource (M3_5_MATERIALS_AND_LOOK_PLAN.md L2, global
/// constraint 4 / ARCHITECTURE §23 allocation policy).
/// </summary>
public sealed class BuddyLookMaterialLibrary
{
    private readonly BuddyLookProfile _look;
    private readonly Dictionary<Color, StandardMaterial3D> _litByColor = new();
    private StandardMaterial3D? _outline;

    public BuddyLookMaterialLibrary(BuddyLookProfile look)
    {
        ArgumentNullException.ThrowIfNull(look);
        if (!GodotObject.IsInstanceValid(look))
        {
            throw new InvalidOperationException(
                "BuddyLookMaterialLibrary requires a valid look profile.");
        }

        _look = look;
    }

    /// <summary>Distinct lit materials cached so far (parts and connectors share by colour).</summary>
    public int LitMaterialCount => _litByColor.Count;

    /// <summary>
    /// Returns a cached soft-toon <see cref="StandardMaterial3D"/> whose albedo is
    /// <paramref name="albedo"/>. Repeated calls with the same colour return the identical
    /// cached instance, so parts and connectors of the same colour share one material.
    /// </summary>
    public StandardMaterial3D GetLitMaterial(Color albedo)
    {
        if (!_litByColor.TryGetValue(albedo, out StandardMaterial3D? material))
        {
            material = CreateLitMaterial(albedo);
            _litByColor[albedo] = material;
        }

        return material;
    }

    /// <summary>The one shared unshaded ink material used by every inverted-hull outline shell.</summary>
    public StandardMaterial3D OutlineMaterial => _outline ??= CreateOutlineMaterial();

    private StandardMaterial3D CreateLitMaterial(Color albedo) => new()
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
