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
/// two blocks welded, a Button wired to a Lamp. The moving bits are named ("Part", "Block",
/// "Cord", "Arm", "Button", "Lamp") so <see cref="SandboxPartPreview"/> can play with them.
/// </summary>
public static class SandboxPaletteModels
{
    /// <summary>Where a rope preview's cord is tied to its beam.</summary>
    public static readonly Vector2 RopeAnchor = new(0.0f, 33.0f);

    /// <summary>Half a rope preview's block height: the cord is tied to its top.</summary>
    public const float RopeBlockHalfHeight = 11.0f;

    /// <summary>The angle a hinge preview's arm is built at, above level.</summary>
    public const float HingeRestDegrees = 35.0f;

    /// <summary>Where a hinge preview's arm sits along itself, from the pin: its centre, and its far end.</summary>
    public const float HingeArmCentre = 34.0f;
    public const float HingeArmEnd = 74.0f;

    private static readonly Color RopeColor = new("8b5a2b");
    private static readonly Color PinMetal = new("d0d4d8");
    private static readonly Color DarkMetal = new("59636f");
    private static readonly Color SpoolWood = new("7a5424");

    /// <summary>
    /// A part on its own. A Piston stands a little low in a frame tall enough for its head's full
    /// reach, so the preview can push it out without leaving the view.
    /// </summary>
    public static PaletteModel ForPart(SandboxPartDefinition definition)
    {
        Node3D model = SandboxPartLook.Build(definition);
        if (definition.Device != SandboxDeviceKind.Piston)
        {
            return new PaletteModel(model,
                new Vector2(definition.Width, definition.Height + (definition.Device == SandboxDeviceKind.Button ? 16.0f : 0.0f)));
        }
        var frame = new Node3D();
        model.Name = "Part";
        Place(frame, model, new Vector3(0.0f, -SandboxPartLook.PistonReach * 0.5f, 0.0f));
        return new PaletteModel(frame, new Vector2(definition.Width, definition.Height + SandboxPartLook.PistonReach));
    }

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
        Node3D button = SandboxPartLook.Build(Definition(SandboxPartCatalogue.Button));
        button.Name = "Button";
        Place(node, button, new Vector3(-46.0f, -8.0f, 0.0f));
        Node3D lamp = SandboxPartLook.Build(Definition(SandboxPartCatalogue.Lamp));
        lamp.Name = "Lamp";
        Place(node, lamp, new Vector3(46.0f, 0.0f, 0.0f));
        Span(Bar(node, 2.6f, SandboxPartLook.Material(SandboxLinkView.WireColorOf(color), 0.45f)),
            new Vector3(-30.0f, -8.0f, 14.0f), new Vector3(34.0f, -10.0f, 14.0f));
        return new PaletteModel(node, new Vector2(128.0f, 44.0f));
    }

    public static SandboxPartDefinition ButtonDefinition => Definition(SandboxPartCatalogue.Button);
    public static SandboxPartDefinition LampDefinition => Definition(SandboxPartCatalogue.Lamp);

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

    /// <summary>How far below its beam a rope preview's block hangs at rest: a stretchier rope sags more.</summary>
    public static float RopeHang(float elasticity) => Mathf.Lerp(8.0f, 34.0f, Mathf.Clamp(elasticity, 0.0f, 1.0f));

    /// <summary>
    /// Lays a rope preview's cord from the beam to <paramref name="end"/>: one strand, or a bungee's
    /// segments, as the room draws them.
    /// </summary>
    public static void LayCord(Node3D cord, Vector2 end)
    {
        var from = new Vector3(RopeAnchor.X, RopeAnchor.Y, 0.0f);
        var to = new Vector3(end.X, end.Y, 0.0f);
        int segments = cord.GetChildCount();
        for (int index = 0; index < segments; index++)
        {
            float start = index / (float)segments;
            float stop = segments == 1 ? 1.0f : (index + 0.6f) / segments;
            Span(cord.GetChild<MeshInstance3D>(index), from.Lerp(to, start), from.Lerp(to, stop));
        }
    }

    /// <summary>A block hanging from a beam: a stronger rope is thicker; a stretchier one hangs lower, in segments.</summary>
    private static PaletteModel Rope(float strength, float elasticity)
    {
        var node = new Node3D();
        AddBox(node, 110.0f, 10.0f, 20.0f, 4.0f, SandboxPartLook.FillFor(SandboxPartMaterial.Wood), 0.8f, new Vector3(0.0f, 38.0f, 0.0f));
        float blockY = -RopeHang(elasticity);
        AddBox(node, 30.0f, RopeBlockHalfHeight * 2.0f, 24.0f, 6.0f, SandboxPartLook.FillFor(SandboxPartMaterial.Metal), 0.45f,
            new Vector3(0.0f, blockY, 0.0f), 0.25f).Name = "Block";

        var cord = new Node3D { Name = "Cord" };
        node.AddChild(cord);
        float radius = 0.8f + 1.6f * Mathf.Clamp(strength, 0.0f, 1.0f);
        Material rope = SandboxPartLook.Material(RopeColor, 0.9f);
        int segments = elasticity < 0.5f ? 1 : 6;   // a bungee's cord comes in segments
        for (int index = 0; index < segments; index++)
            Bar(cord, radius, rope);
        LayCord(cord, new Vector2(0.0f, blockY + RopeBlockHalfHeight));
        return new PaletteModel(node, new Vector2(118.0f, 44.0f - blockY + RopeBlockHalfHeight + 4.0f));
    }

    /// <summary>Two beams on one pin; a stiff hinge carries a spring ring around its pin.</summary>
    private static PaletteModel Hinge(float stiffness)
    {
        var node = new Node3D();
        Color wood = SandboxPartLook.FillFor(SandboxPartMaterial.Wood);
        AddBox(node, 80.0f, 12.0f, 20.0f, 4.0f, wood, 0.8f, new Vector3(-34.0f, 0.0f, 0.0f));
        var arm = new Node3D { Name = "Arm", RotationDegrees = new Vector3(0.0f, 0.0f, HingeRestDegrees) };
        AddBox(arm, 80.0f, 12.0f, 20.0f, 4.0f, wood, 0.8f, new Vector3(HingeArmCentre, 0.0f, 2.0f));
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

    private static MeshInstance3D AddBox(Node3D parent, float width, float height, float depth, float radius, Color color,
        float roughness, Vector3 at, float metallic = 0.0f)
    {
        var box = new MeshInstance3D
        {
            Mesh = SandboxPartLook.RoundedBox(width, height, depth, radius),
            MaterialOverride = SandboxPartLook.Material(color, roughness, metallic),
            Position = at,
        };
        parent.AddChild(box);
        return box;
    }

    /// <summary>A round bar one unit long; <see cref="Span"/> lays it between two points.</summary>
    private static MeshInstance3D Bar(Node3D parent, float radius, Material material)
    {
        var bar = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 1.0f, RadialSegments = 12, Rings = 1 },
            MaterialOverride = material,
        };
        parent.AddChild(bar);
        return bar;
    }

    private static void Span(MeshInstance3D bar, Vector3 from, Vector3 to)
    {
        Vector3 span = to - from;
        float length = span.Length();
        bar.Visible = length > 0.01f;
        bar.Position = (from + to) * 0.5f;
        // A cylinder stands along Y; turn it onto the span and stretch it to length.
        bar.Rotation = new Vector3(0.0f, 0.0f, MathF.Atan2(span.Y, span.X) - Mathf.Pi * 0.5f);
        bar.Scale = new Vector3(1.0f, Mathf.Max(0.01f, length), 1.0f);
    }
}
