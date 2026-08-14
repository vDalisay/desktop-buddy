using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>
/// Visual-only factory for trusted Environment definitions. Existing launch content stays on the
/// bounded semantic/procedural path. Asset Forge content uses imported project-owned GLB/texture
/// resources and is rejected if it contains scripts, physics, collision or other gameplay nodes.
/// </summary>
public static class EnvironmentDecorationVisualFactory
{
    private enum ColorRole { Primary, Secondary, Light, Dark }
    private readonly record struct Part(float X, float Y, float Width, float Height, ColorRole Color, float Depth = 0);

    public static void Populate3D(Node3D root, EnvironmentDecorationResource definition, Vector2? sizeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.VisualSource == EnvironmentDecorationVisualSource.GeneratedMesh)
        {
            PopulateGenerated3D(root, definition);
            return;
        }

        Vector2 size = sizeOverride ?? definition.VisualSize;
        int index = 0;
        foreach (Part part in Parts(definition.VisualKind))
        {
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = ResolveColor(definition, part.Color),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            var mesh = new MeshInstance3D
            {
                Name = $"DecorationPart{index++}",
                Mesh = new QuadMesh
                {
                    Size = new Vector2(size.X * part.Width, size.Y * part.Height),
                    Material = material,
                },
                Position = new Vector3(size.X * part.X, -size.Y * part.Y, .02f + part.Depth),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            root.AddChild(mesh);
        }
    }

    private static void PopulateGenerated3D(Node3D root, EnvironmentDecorationResource definition)
    {
        if (!GodotObject.IsInstanceValid(definition.GeneratedMesh) ||
            !GodotObject.IsInstanceValid(definition.GeneratedAlbedo))
            throw new InvalidOperationException($"Generated Environment definition '{definition.DefinitionId}' is missing imported visual resources.");

        Node instance = definition.GeneratedMesh!.Instantiate();
        if (instance is not Node3D generated)
        {
            instance.QueueFree();
            throw new InvalidOperationException($"Generated Environment definition '{definition.DefinitionId}' must instantiate a Node3D root.");
        }
        try
        {
            ValidateGeneratedVisualTree(generated, definition.DefinitionId);
            List<MeshInstance3D> meshes = GeneratedMeshes(generated);
            if (meshes.Count != 1)
                throw new InvalidOperationException($"Generated Environment definition '{definition.DefinitionId}' must contain exactly one authored mesh; found {meshes.Count}.");

            generated.Name = "GeneratedDecorationMesh";
            generated.Scale = Vector3.One * definition.DefaultScale;
            root.AddChild(generated);

            var material = new StandardMaterial3D
            {
                ResourceName = $"GeneratedEnvironment_{definition.DefinitionId}",
                AlbedoColor = Colors.White,
                AlbedoTexture = definition.GeneratedAlbedo,
                AlbedoTextureForceSrgb = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Burley,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
                Roughness = .82f,
                CullMode = BaseMaterial3D.CullModeEnum.Back,
            };
            meshes[0].MaterialOverride = material;
            meshes[0].CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

            if (GodotObject.IsInstanceValid(definition.LightProfile) && definition.LightProfile!.Enabled)
                AddLampPresentation(root, definition, material);
        }
        catch
        {
            if (GodotObject.IsInstanceValid(generated) && generated.GetParent() is null)
                generated.QueueFree();
            throw;
        }
    }

    private static void AddLampPresentation(
        Node3D root,
        EnvironmentDecorationResource definition,
        StandardMaterial3D bodyMaterial)
    {
        DecorationLightProfileResource profile = definition.LightProfile!;
        Color lightColor = profile.Color;
        Vector2 size = definition.VisualSize * definition.DefaultScale;
        Vector3 emitter = new(
            (profile.EmitterPosition.X - .5f) * size.X,
            -(1f - profile.EmitterPosition.Y) * size.Y,
            MathF.Max(1f, MathF.Min(size.X, size.Y) * .08f));

        // Keep emissive bulb presentation separate from the authored body texture so the complete
        // lamp does not glow uniformly when only the bulb/source region should read as bright.
        float markerRadius = Math.Clamp(MathF.Min(size.X, size.Y) * .055f, 2f, 16f);
        var glowMaterial = new StandardMaterial3D
        {
            ResourceName = $"GeneratedEnvironmentLampGlow_{definition.DefinitionId}",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = lightColor,
            EmissionEnabled = true,
            Emission = lightColor,
            EmissionEnergyMultiplier = profile.EmissionStrength,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        root.AddChild(new MeshInstance3D
        {
            Name = "GeneratedLampEmitterVisual",
            Mesh = new SphereMesh { Radius = markerRadius, Height = markerRadius * 2f },
            Position = emitter,
            MaterialOverride = glowMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        if (profile.LightEnabled)
        {
            root.AddChild(new OmniLight3D
            {
                Name = "GeneratedLampLocalLight",
                Position = emitter,
                LightColor = lightColor,
                LightEnergy = profile.Brightness,
                OmniRange = profile.Range,
                ShadowEnabled = false,
            });
        }

        _ = bodyMaterial;
    }

    private static void ValidateGeneratedVisualTree(Node root, string id)
    {
        if (root.GetScript().Obj is not null)
            throw new InvalidOperationException($"Generated Environment visual '{id}' may not contain scripts.");
        if (root is CollisionObject2D or CollisionObject3D or CollisionShape2D or CollisionShape3D or
            CollisionPolygon2D or CollisionPolygon3D or Joint2D or Joint3D or PhysicsBody2D or PhysicsBody3D)
            throw new InvalidOperationException($"Generated Environment visual '{id}' may not contain physics or collision nodes.");
        foreach (Node child in root.GetChildren()) ValidateGeneratedVisualTree(child, id);
    }

    private static List<MeshInstance3D> GeneratedMeshes(Node3D root)
    {
        var meshes = new List<MeshInstance3D>();
        if (root is MeshInstance3D rootMesh) meshes.Add(rootMesh);
        meshes.AddRange(root.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>());
        return meshes;
    }

    public static Texture2D CreatePreview(EnvironmentDecorationResource definition, int pixels = 48)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.VisualSource == EnvironmentDecorationVisualSource.GeneratedMesh)
        {
            if (!GodotObject.IsInstanceValid(definition.Thumbnail))
                throw new InvalidOperationException($"Generated Environment definition '{definition.DefinitionId}' has no trusted thumbnail.");
            return definition.Thumbnail!;
        }

        pixels = Math.Clamp(pixels, 16, 256);
        Image image = Image.CreateEmpty(pixels, pixels, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        const float margin = .06f;
        float usable = 1f - (margin * 2f);
        foreach (Part part in Parts(definition.VisualKind))
        {
            int x0 = (int)Math.Floor((margin + ((part.X - (part.Width * .5f)) + .5f) * usable) * pixels);
            int y0 = (int)Math.Floor((margin + ((part.Y - (part.Height * .5f)) + .5f) * usable) * pixels);
            int x1 = (int)Math.Ceiling((margin + ((part.X + (part.Width * .5f)) + .5f) * usable) * pixels) - 1;
            int y1 = (int)Math.Ceiling((margin + ((part.Y + (part.Height * .5f)) + .5f) * usable) * pixels) - 1;
            FillRect(image, x0, y0, x1, y1, ResolveColor(definition, part.Color));
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static Color ResolveColor(EnvironmentDecorationResource definition, ColorRole role) => role switch
    {
        ColorRole.Primary => definition.PrimaryColor,
        ColorRole.Secondary => definition.SecondaryColor,
        ColorRole.Light => definition.PrimaryColor.Lightened(.28f),
        ColorRole.Dark => definition.SecondaryColor.Darkened(.22f),
        _ => definition.PrimaryColor,
    };

    private static Part[] Parts(EnvironmentDecorationVisualKind kind) => kind switch
    {
        EnvironmentDecorationVisualKind.FloorLamp =>
        [
            new(0, .42f, .62f, .10f, ColorRole.Secondary),
            new(0, .04f, .10f, .68f, ColorRole.Secondary),
            new(0, -.30f, .72f, .24f, ColorRole.Primary, .02f),
            new(0, -.27f, .48f, .08f, ColorRole.Light, .04f),
        ],
        EnvironmentDecorationVisualKind.ArcLamp =>
        [
            new(-.30f, .42f, .54f, .10f, ColorRole.Secondary),
            new(-.34f, .06f, .09f, .68f, ColorRole.Secondary),
            new(-.05f, -.26f, .62f, .08f, ColorRole.Secondary),
            new(.28f, -.18f, .34f, .25f, ColorRole.Primary, .03f),
            new(.28f, -.12f, .22f, .07f, ColorRole.Light, .04f),
        ],
        EnvironmentDecorationVisualKind.Sofa =>
        [
            new(0, -.08f, .78f, .50f, ColorRole.Primary),
            new(0, .20f, .72f, .26f, ColorRole.Light, .03f),
            new(-.43f, .10f, .14f, .48f, ColorRole.Secondary, .02f),
            new(.43f, .10f, .14f, .48f, ColorRole.Secondary, .02f),
            new(-.28f, .43f, .08f, .14f, ColorRole.Dark),
            new(.28f, .43f, .08f, .14f, ColorRole.Dark),
        ],
        EnvironmentDecorationVisualKind.LoungeSofa =>
        [
            new(-.08f, -.08f, .86f, .42f, ColorRole.Primary),
            new(-.12f, .20f, .78f, .24f, ColorRole.Light, .03f),
            new(-.46f, .12f, .12f, .44f, ColorRole.Secondary, .02f),
            new(.31f, .24f, .34f, .25f, ColorRole.Primary, .02f),
            new(.45f, .33f, .08f, .22f, ColorRole.Secondary),
            new(-.30f, .43f, .07f, .13f, ColorRole.Dark),
            new(.30f, .43f, .07f, .13f, ColorRole.Dark),
        ],
        EnvironmentDecorationVisualKind.Painting =>
        [
            new(0, 0, 1f, 1f, ColorRole.Secondary),
            new(0, 0, .88f, .82f, ColorRole.Light, .02f),
            new(0, .20f, .84f, .38f, ColorRole.Primary, .04f),
            new(-.26f, -.18f, .20f, .20f, ColorRole.Primary, .04f),
        ],
        EnvironmentDecorationVisualKind.GeometricPainting =>
        [
            new(0, 0, 1f, 1f, ColorRole.Secondary),
            new(0, 0, .86f, .86f, ColorRole.Light, .02f),
            new(-.22f, -.20f, .34f, .34f, ColorRole.Primary, .04f),
            new(.20f, .18f, .38f, .38f, ColorRole.Dark, .04f),
            new(.24f, -.24f, .20f, .20f, ColorRole.Primary, .05f),
            new(-.25f, .25f, .16f, .16f, ColorRole.Secondary, .05f),
        ],
        EnvironmentDecorationVisualKind.Wallpaper =>
        [
            new(0, 0, 1f, 1f, ColorRole.Primary),
            new(-.36f, 0, .07f, 1f, ColorRole.Secondary, .01f),
            new(-.12f, 0, .07f, 1f, ColorRole.Secondary, .01f),
            new(.12f, 0, .07f, 1f, ColorRole.Secondary, .01f),
            new(.36f, 0, .07f, 1f, ColorRole.Secondary, .01f),
        ],
        EnvironmentDecorationVisualKind.GridWallpaper =>
        [
            new(0, 0, 1f, 1f, ColorRole.Primary),
            new(-.25f, 0, .035f, 1f, ColorRole.Secondary, .01f),
            new(.25f, 0, .035f, 1f, ColorRole.Secondary, .01f),
            new(0, -.25f, 1f, .035f, ColorRole.Secondary, .01f),
            new(0, .25f, 1f, .035f, ColorRole.Secondary, .01f),
        ],
        EnvironmentDecorationVisualKind.Plant =>
        [
            new(0, .31f, .48f, .34f, ColorRole.Secondary),
            new(0, .06f, .08f, .40f, ColorRole.Dark),
            new(-.20f, -.10f, .28f, .42f, ColorRole.Primary, .02f),
            new(.20f, -.14f, .28f, .45f, ColorRole.Light, .02f),
            new(0, -.28f, .30f, .42f, ColorRole.Primary, .03f),
        ],
        EnvironmentDecorationVisualKind.LeafyPlant =>
        [
            new(0, .34f, .54f, .30f, ColorRole.Secondary),
            new(0, .06f, .08f, .46f, ColorRole.Dark),
            new(-.28f, -.08f, .27f, .42f, ColorRole.Primary, .02f),
            new(.28f, -.06f, .27f, .42f, ColorRole.Primary, .02f),
            new(-.12f, -.28f, .30f, .40f, ColorRole.Light, .03f),
            new(.12f, -.31f, .30f, .40f, ColorRole.Primary, .03f),
        ],
        EnvironmentDecorationVisualKind.Table =>
        [
            new(0, -.10f, .94f, .20f, ColorRole.Primary, .02f),
            new(-.34f, .22f, .10f, .58f, ColorRole.Secondary),
            new(.34f, .22f, .10f, .58f, ColorRole.Secondary),
        ],
        EnvironmentDecorationVisualKind.DiningTable =>
        [
            new(0, -.12f, 1f, .22f, ColorRole.Primary, .03f),
            new(0, -.01f, .94f, .08f, ColorRole.Light, .04f),
            new(-.36f, .23f, .10f, .55f, ColorRole.Secondary),
            new(.36f, .23f, .10f, .55f, ColorRole.Secondary),
            new(0, .23f, .10f, .55f, ColorRole.Dark),
        ],
        _ => [new(0, 0, 1f, 1f, ColorRole.Primary)],
    };

    private static void FillRect(Image image, int x0, int y0, int x1, int y1, Color color)
    {
        int width = image.GetWidth();
        int height = image.GetHeight();
        x0 = Math.Clamp(x0, 0, width - 1);
        x1 = Math.Clamp(x1, 0, width - 1);
        y0 = Math.Clamp(y0, 0, height - 1);
        y1 = Math.Clamp(y1, 0, height - 1);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            image.SetPixel(x, y, color);
    }
}
