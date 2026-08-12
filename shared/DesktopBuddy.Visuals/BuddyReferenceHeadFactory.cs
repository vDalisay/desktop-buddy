using Godot;

namespace DesktopBuddy.Buddy.Presentation3D.Shared;

public sealed record BuddyReferenceHead(Node3D Root, Node3D EyeGroup, float Radius);

/// <summary>
/// Builds the exact primitive head geometry used by the current Buddy presentation: a sphere,
/// the accepted soft-toon material, the accepted inverted-hull outline, and the same EyeGroup
/// depth convention used by glasses. Asset Forge supplies values parsed from the synchronized
/// trusted Buddy resources; no physics authority or save state is created.
/// </summary>
public static class BuddyReferenceHeadFactory
{
    public static BuddyReferenceHead Build(
        Node3D parent,
        float radius,
        float faceDepthEpsilon,
        Color baseColor,
        in BuddySharedLook look)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(faceDepthEpsilon) || faceDepthEpsilon <= 0) throw new ArgumentOutOfRangeException(nameof(faceDepthEpsilon));

        var root = new Node3D { Name = "BuddyReferenceHead" };
        parent.AddChild(root);
        var sphere = new SphereMesh { Radius = radius, Height = radius * 2f };
        root.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = sphere,
            MaterialOverride = BuddySharedMaterialFactory.CreateLitMaterial(look, baseColor),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        root.AddChild(new MeshInstance3D
        {
            Name = "Outline",
            Mesh = sphere,
            MaterialOverride = BuddySharedMaterialFactory.CreateOutlineMaterial(look),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        var eyeGroup = new Node3D
        {
            Name = "EyeGroup",
            Position = new Vector3(0, 0, radius + faceDepthEpsilon * 3f),
        };
        root.AddChild(eyeGroup);
        return new BuddyReferenceHead(root, eyeGroup, radius);
    }
}
