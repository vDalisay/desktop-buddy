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
        PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Inherit,
    };
}
