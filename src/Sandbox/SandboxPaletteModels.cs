using System;
using DesktopBuddy.Domain.Sandbox;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>A 3D model for the Build palette and the world-space size it needs framed.</summary>
public readonly record struct PaletteModel(Node3D Node, Vector2 Extent);

/// <summary>
/// The Build palette's 3D pictures (owner note 2026-09-11: previews and icons are the 3D parts, not
/// drawings). Parts are their own <see cref="SandboxPartLook"/> models; a link or wire is shown
/// doing its job between real part models — a block hanging on a rope, two beams on a hinge pin,
/// two blocks welded, a Button wired to a Lamp.
/// </summary>
public static class SandboxPaletteModels
{
    private static readonly Color RopeColor = new("8b5a2b");
    private static readonly Color PinMetal = new("d0d4d8");
    private static readonly Color DarkMetal = new("59636f");
    private static readonly Color SpoolWood = new("7a5424");

    public static PaletteModel ForPart(SandboxPartDefinition definition) =>
        new(SandboxPartLook.Build(definition),
            new Vector2(definition.Width, definition.Height + (definition.Device == SandboxDeviceKind.Button ? 16.0f : 0.0f)));

    public static PaletteModel ForLink(SandboxLinkKind kind, float strength, float elasticity, float stiffness) => kind switch
    {
        SandboxLinkKind.Rope => Rope(strength, elasticity),
        SandboxLinkKind.Hinge => Hinge(stiffness),
        _ => Weld(),
    };

    /// <summary>A Button wired to a Lamp, the wire in its colour.</summary>
    public static PaletteModel ForWire(SandboxWireColor color)
    {
        var node = new Node3D();
        Place(node, SandboxPartLook.Build(Definition(SandboxPartCatalogue.Button)), new Vector3(-46.0f, -8.0f, 0.0f));
        Place(node, SandboxPartLook.Build(Definition(SandboxPartCatalogue.Lamp)), new Vector3(46.0f, 0.0f, 0.0f));
        Bar(node, new Vector3(-30.0f, -8.0f, 14.0f), new Vector3(34.0f, -10.0f, 14.0f), 2.6f,
            SandboxPartLook.Material(SandboxLinkView.WireColorOf(color), 0.45f));
        return new PaletteModel(node, new Vector2(128.0f, 44.0f));
    }

    /// <summary>A reel of wire, for the Wires category.</summary>
    public static PaletteModel Spool()
    {
        var node = new Node3D();
        var core = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 10.0f, BottomRadius = 10.0f, Height = 22.0f, RadialSegments = 24, Rings = 1 },
            MaterialOverride = SandboxPartLook.Material(SandboxLinkView.WireColorOf(SandboxWireColor.Red), 0.55f),
            RotationDegrees = new Vector3(0.0f, 0.0f, 90.0f),
        };
        node.AddChild(core);
        foreach (float side in new[] { -12.5f, 12.5f })
        {
            node.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 15.0f, BottomRadius = 15.0f, Height = 3.0f, RadialSegments = 24, Rings = 1 },
                MaterialOverride = SandboxPartLook.Material(SpoolWood, 0.8f),
                RotationDegrees = new Vector3(0.0f, 0.0f, 90.0f),
                Position = new Vector3(side, 0.0f, 0.0f),
            });
        }
        node.RotationDegrees = new Vector3(18.0f, -25.0f, 0.0f);   // turned a little, so it reads as a reel
        return new PaletteModel(node, new Vector2(40.0f, 36.0f));
    }

    /// <summary>A block hanging from a beam: a stronger rope is thicker; a stretchier one hangs lower, in segments.</summary>
    private static PaletteModel Rope(float strength, float elasticity)
    {
        var node = new Node3D();
        AddBox(node, 110.0f, 10.0f, 20.0f, 4.0f, SandboxPartLook.FillFor(SandboxPartMaterial.Wood), 0.8f, new Vector3(0.0f, 38.0f, 0.0f));
        float blockY = -Mathf.Lerp(8.0f, 34.0f, Mathf.Clamp(elasticity, 0.0f, 1.0f));
        AddBox(node, 30.0f, 22.0f, 24.0f, 6.0f, SandboxPartLook.FillFor(SandboxPartMaterial.Metal), 0.45f, new Vector3(0.0f, blockY, 0.0f), 0.25f);

        var top = new Vector3(0.0f, 33.0f, 0.0f);
        var bottom = new Vector3(0.0f, blockY + 11.0f, 0.0f);
        float radius = 0.8f + 1.6f * Mathf.Clamp(strength, 0.0f, 1.0f);
        Material rope = SandboxPartLook.Material(RopeColor, 0.9f);
        if (elasticity < 0.5f)
        {
            Bar(node, top, bottom, radius, rope);
        }
        else
        {
            // A bungee's cord, in segments like the room draws it.
            const int Segments = 6;
            for (int index = 0; index < Segments; index++)
            {
                float from = index / (float)Segments;
                float to = (index + 0.6f) / Segments;
                Bar(node, top.Lerp(bottom, from), top.Lerp(bottom, to), radius, rope);
            }
        }
        return new PaletteModel(node, new Vector2(118.0f, 44.0f - blockY + 11.0f + 4.0f));
    }

    /// <summary>Two beams on one pin; a stiff hinge carries a spring ring around its pin.</summary>
    private static PaletteModel Hinge(float stiffness)
    {
        var node = new Node3D();
        Color wood = SandboxPartLook.FillFor(SandboxPartMaterial.Wood);
        AddBox(node, 80.0f, 12.0f, 20.0f, 4.0f, wood, 0.8f, new Vector3(-34.0f, 0.0f, 0.0f));
        var arm = new Node3D { RotationDegrees = new Vector3(0.0f, 0.0f, 35.0f) };
        AddBox(arm, 80.0f, 12.0f, 20.0f, 4.0f, wood, 0.8f, new Vector3(34.0f, 0.0f, 2.0f));
        node.AddChild(arm);
        node.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 5.0f, BottomRadius = 5.0f, Height = 30.0f, RadialSegments = 20, Rings = 1 },
            MaterialOverride = SandboxPartLook.Material(PinMetal, 0.3f, 0.6f),
            RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f),
        });
        if (stiffness > 0.0f)
        {
            float thickness = 1.5f + 2.5f * Mathf.Clamp(stiffness, 0.0f, 1.0f);
            node.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 7.0f, OuterRadius = 7.0f + thickness, Rings = 24, RingSegments = 8 },
                MaterialOverride = SandboxPartLook.Material(DarkMetal, 0.4f, 0.5f),
                RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f),
                Position = new Vector3(0.0f, 0.0f, 12.0f),
            });
        }
        return new PaletteModel(node, new Vector2(160.0f, 70.0f));
    }

    /// <summary>Two metal blocks overlapping, and the weld plate across the seam.</summary>
    private static PaletteModel Weld()
    {
        var node = new Node3D();
        Color metal = SandboxPartLook.FillFor(SandboxPartMaterial.Metal);
        AddBox(node, 46.0f, 30.0f, 24.0f, 6.0f, metal, 0.45f, new Vector3(-17.0f, 6.0f, 0.0f), 0.25f);
        AddBox(node, 46.0f, 30.0f, 24.0f, 6.0f, metal, 0.45f, new Vector3(17.0f, -6.0f, 2.0f), 0.25f);
        AddBox(node, 16.0f, 16.0f, 4.0f, 1.5f, DarkMetal, 0.5f, new Vector3(0.0f, 0.0f, 14.5f), 0.4f);
        return new PaletteModel(node, new Vector2(84.0f, 44.0f));
    }

    private static SandboxPartDefinition Definition(Domain.Content.SemanticDefinitionId id)
    {
        SandboxPartCatalogue.TryGet(id, out SandboxPartDefinition definition);
        return definition;
    }

    private static void Place(Node3D parent, Node3D child, Vector3 at)
    {
        child.Position = at;
        parent.AddChild(child);
    }

    private static void AddBox(Node3D parent, float width, float height, float depth, float radius, Color color,
        float roughness, Vector3 at, float metallic = 0.0f) =>
        parent.AddChild(new MeshInstance3D
        {
            Mesh = SandboxPartLook.RoundedBox(width, height, depth, radius),
            MaterialOverride = SandboxPartLook.Material(color, roughness, metallic),
            Position = at,
        });

    /// <summary>A round bar from one point to another, in the model's own space.</summary>
    private static void Bar(Node3D parent, Vector3 from, Vector3 to, float radius, Material material)
    {
        Vector3 span = to - from;
        float length = span.Length();
        if (length < 0.01f)
            return;
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = length, RadialSegments = 12, Rings = 1 },
            MaterialOverride = material,
            Position = (from + to) * 0.5f,
            // A cylinder stands along Y; turn it onto the span.
            Rotation = new Vector3(0.0f, 0.0f, MathF.Atan2(span.Y, span.X) - Mathf.Pi * 0.5f),
        });
    }
}
