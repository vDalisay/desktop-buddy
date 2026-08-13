using Godot;

namespace DesktopBuddy.Buddy.Presentation3D.Shared;

/// <summary>
/// Engine-facing immutable snapshot of the accepted Buddy look. The game projects its trusted
/// BuddyLookProfile into this value; Asset Forge reads the same authored profile values from the
/// synced developer copy. Rendering construction lives here so the two executables cannot drift.
/// </summary>
public readonly record struct BuddySharedLook(
    BaseMaterial3D.DiffuseModeEnum DiffuseMode,
    BaseMaterial3D.SpecularModeEnum SpecularMode,
    float Specular,
    float Roughness,
    Color KeyColor,
    float KeyEnergy,
    Vector3 KeyEulerDegrees,
    Color FillColor,
    float FillEnergy,
    Vector3 FillEulerDegrees,
    bool ShadowsEnabled,
    Color OutlineColor,
    float OutlineGrowAmount);

public static class BuddySharedMaterialFactory
{
    /// <summary>
    /// Low texture-coloured light floor used only by generated opaque assets. Their narrow rounded
    /// surfaces can point almost perpendicular to both Buddy directional lights; keeping a small
    /// authored-colour contribution prevents those valid surfaces collapsing to black while normal
    /// per-pixel diffuse/specular shading still supplies the 3D form.
    /// </summary>
    public const float GeneratedAssetEmissionFloor = 0.16f;

    public static StandardMaterial3D CreateLitMaterial(in BuddySharedLook look, Color albedo) => new()
    {
        ResourceName = "BuddyLookLitMaterial",
        AlbedoColor = albedo,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        DiffuseMode = look.DiffuseMode,
        SpecularMode = look.SpecularMode,
        MetallicSpecular = look.Specular,
        Roughness = look.Roughness,
        Metallic = 0.0f,
    };

    public static StandardMaterial3D CreateLitTexturedMaterial(
        in BuddySharedLook look,
        Texture2D albedo,
        Color modulation)
    {
        StandardMaterial3D material = CreateLitMaterial(look, modulation);
        material.ResourceName = "BuddyLookTexturedMaterial";
        material.AlbedoTexture = albedo;
        // This generic textured seam preserves source alpha for callers whose visible surface is
        // still texture-defined. Generated solid assets use CreateGeneratedAssetMaterial instead.
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return material;
    }

    /// <summary>
    /// Material contract for deterministic Asset Forge geometry. Alpha has already been converted
    /// into mesh silhouette (including real lens holes), and the exported albedo has safe colour
    /// bleed for every UV sample. The material must therefore stay opaque: re-enabling alpha
    /// blending would make the render contract disagree with the generated geometry.
    /// </summary>
    public static StandardMaterial3D CreateGeneratedAssetMaterial(
        in BuddySharedLook look,
        Texture2D albedo,
        Color modulation)
    {
        StandardMaterial3D material = CreateLitMaterial(look, modulation);
        material.ResourceName = "BuddyLookGeneratedAssetMaterial";
        material.AlbedoTexture = albedo;

        // Generated assets are intentionally solid geometry. StandardMaterial3D is opaque by
        // default, so do not opt into a transparency mode here.
        // Use the authored texture as a small emission contribution rather than making the mesh
        // unshaded. Emission defaults to black/additive, so the texture alone supplies the floor.
        material.EmissionEnabled = true;
        material.Emission = Colors.Black;
        material.EmissionTexture = albedo;
        material.EmissionEnergyMultiplier = GeneratedAssetEmissionFloor;
        return material;
    }

    public static StandardMaterial3D CreateOutlineMaterial(in BuddySharedLook look) => new()
    {
        ResourceName = "BuddyLookOutlineMaterial",
        AlbedoColor = look.OutlineColor,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Front,
        Grow = true,
        GrowAmount = look.OutlineGrowAmount,
    };

    public static DirectionalLight3D CreateDirectionalLight(
        string name,
        Color color,
        float energy,
        Vector3 eulerDegrees) => new()
    {
        Name = name,
        LightColor = color,
        LightEnergy = energy,
        ShadowEnabled = false,
        RotationDegrees = eulerDegrees,
    };
}
