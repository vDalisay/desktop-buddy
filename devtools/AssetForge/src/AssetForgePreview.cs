using DesktopBuddy.AssetForge.Core;
using DesktopBuddy.Buddy.Presentation3D.Shared;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgePreview : Control
{
    private const float ThumbnailCropFraction = 0.78f;
    private const float FootPairOffsetRadii = 1.16f;

    private SubViewport _viewport = null!;
    private Node3D _orbit = null!;
    private Camera3D _camera = null!;
    private BuddyReferenceHead _headReference = null!;
    private MeshInstance3D _partReference = null!;
    private MeshInstance3D _partReferenceSecondary = null!;
    private Node3D _environmentReference = null!;
    private Node3D? _asset;
    private StandardMaterial3D? _generatedMaterial;
    private MeshInstance3D? _lampEmitterGizmo;
    private OmniLight3D? _lampPreviewLight;
    private TrustedBuddyPreviewProfile _profile;
    private AssetCategory _category = AssetCategory.Glasses;
    private EnvironmentGeneratedBounds _environmentBounds;
    private AssetRecipe? _environmentRecipe;
    private double _environmentLogicalHeight = 150;
    private DecorationLightSettings _lampSettings = new();
    private bool _referenceVisible = true;
    private bool _rotating;
    private bool _panning;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        GuiInput += OnPreviewInput;
        var container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(container);
        _viewport = new SubViewport
        {
            Name = "PreviewViewport",
            TransparentBg = true,
            Size = new Vector2I(720, 640),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        container.AddChild(_viewport);
        var world = new Node3D { Name = "World" };
        _viewport.AddChild(world);
        _orbit = new Node3D { Name = "Orbit" };
        world.AddChild(_orbit);

        _profile = TrustedBuddyProfileReader.Load();
        _headReference = BuddyReferenceHeadFactory.Build(
            _orbit, _profile.HeadRadius, _profile.FaceDepthEpsilon, _profile.HeadColor, _profile.Look);
        _partReference = CreatePartReference("PartReference");
        _partReferenceSecondary = CreatePartReference("PartReferenceSecondary");
        _orbit.AddChild(_partReference);
        _orbit.AddChild(_partReferenceSecondary);
        _environmentReference = new Node3D { Name = "EnvironmentScaleReference", Visible = false };
        _orbit.AddChild(_environmentReference);
        BuildEnvironmentReference();

        world.AddChild(BuddySharedMaterialFactory.CreateDirectionalLight(
            "KeyLight", _profile.Look.KeyColor, _profile.Look.KeyEnergy, _profile.Look.KeyEulerDegrees));
        world.AddChild(BuddySharedMaterialFactory.CreateDirectionalLight(
            "FillLight", _profile.Look.FillColor, _profile.Look.FillEnergy, _profile.Look.FillEulerDegrees));
        _camera = new Camera3D
        {
            Name = "Camera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = _profile.HeadRadius * 3.2f,
            Position = new Vector3(0, 0, _profile.HeadRadius * 5f),
            Current = true,
        };
        world.AddChild(_camera);
        SetCategory(AssetCategory.Glasses);
    }

    private static MeshInstance3D CreatePartReference(string name) => new()
    {
        Name = name,
        Visible = false,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private void BuildEnvironmentReference()
    {
        var lineMaterial = new StandardMaterial3D
        {
            ResourceName = "AssetForgeFloorReference",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(.36f, .70f, .42f, .55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _environmentReference.AddChild(new MeshInstance3D
        {
            Name = "FloorLine",
            Mesh = new BoxMesh { Size = new Vector3(320, 1.5f, 1.5f) },
            MaterialOverride = lineMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        AddEnvironmentBuddyReference("BuddyTorso", new Vector3(-105, 58, -3), _profile.TorsoRadius, _profile.TorsoColor);
        AddEnvironmentBuddyReference("BuddyHead", new Vector3(-105, 120, -3), _profile.HeadRadius, _profile.HeadColor);
    }

    private void AddEnvironmentBuddyReference(string name, Vector3 position, float radius, Color color)
    {
        var material = new StandardMaterial3D
        {
            ResourceName = name + "Material",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(color.R, color.G, color.B, .18f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _environmentReference.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2 },
            Position = position,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public void SetCategory(AssetCategory category)
    {
        _category = category;
        if (!GodotObject.IsInstanceValid(_partReference) ||
            !GodotObject.IsInstanceValid(_partReferenceSecondary) ||
            !GodotObject.IsInstanceValid(_headReference.Root)) return;

        bool environment = category is AssetCategory.Lamp or AssetCategory.Sofa;
        _environmentReference.Visible = environment && _referenceVisible;
        if (category == AssetCategory.Glasses)
        {
            _partReference.Visible = false;
            _partReferenceSecondary.Visible = false;
            _headReference.Root.Visible = _referenceVisible;
        }
        else if (environment)
        {
            _headReference.Root.Visible = false;
            _partReference.Visible = false;
            _partReferenceSecondary.Visible = false;
        }
        else
        {
            float radius = ReferenceRadius();
            Color color = category == AssetCategory.TorsoShape ? _profile.TorsoColor : _profile.FootColor;
            ConfigureReferenceMesh(_partReference, radius, color);
            ConfigureReferenceMesh(_partReferenceSecondary, radius, color);
            if (category == AssetCategory.FootShape)
            {
                _partReference.Position = new Vector3(-radius * FootPairOffsetRadii, 0, 0);
                _partReferenceSecondary.Position = new Vector3(radius * FootPairOffsetRadii, 0, 0);
                _partReference.Visible = _referenceVisible;
                _partReferenceSecondary.Visible = _referenceVisible;
            }
            else
            {
                _partReference.Position = Vector3.Zero;
                _partReferenceSecondary.Visible = false;
                _partReference.Visible = _referenceVisible;
            }
            _headReference.Root.Visible = false;
        }
        ResetView();
    }

    private void ConfigureReferenceMesh(MeshInstance3D instance, float radius, Color color)
    {
        instance.Mesh = new SphereMesh { Radius = radius, Height = radius * 2f };
        instance.MaterialOverride = new StandardMaterial3D
        {
            ResourceName = "AssetForgePartReference",
            AlbedoColor = new Color(color.R, color.G, color.B, 0.20f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            DiffuseMode = _profile.Look.DiffuseMode,
            SpecularMode = _profile.Look.SpecularMode,
            Roughness = _profile.Look.Roughness,
        };
    }

    public void ClearGenerated()
    {
        if (GodotObject.IsInstanceValid(_asset))
        {
            _asset!.GetParent()?.RemoveChild(_asset);
            _asset.QueueFree();
        }
        _asset = null;
        _generatedMaterial = null;
        _lampEmitterGizmo = null;
        _lampPreviewLight = null;
        _environmentBounds = default;
        _environmentRecipe = null;
    }

    public void ShowGenerated(GeneratedAsset generated, string sourcePath)
    {
        _ = sourcePath;
        ClearGenerated();
        SetCategory(generated.Recipe.Category);

        _asset = new Node3D { Name = "GeneratedAsset" };
        if (_category == AssetCategory.Glasses) _headReference.EyeGroup.AddChild(_asset);
        else _orbit.AddChild(_asset);
        ArrayMesh mesh = ToGodotMesh(generated.Mesh);

        RgbaImage runtime = PngCodec.DecodeRgba8(generated.AlbedoPng);
        Image source = Image.CreateFromData(runtime.Width, runtime.Height, false, Image.Format.Rgba8, runtime.Pixels);
        Texture2D texture = ImageTexture.CreateFromImage(source);
        _generatedMaterial = BuddySharedMaterialFactory.CreateGeneratedAssetMaterial(_profile.Look, texture, Colors.White);
        _generatedMaterial.AlbedoTextureForceSrgb = true;
        SetLightingLevel((float)generated.Recipe.LightingLevel);

        if (_category == AssetCategory.FootShape)
        {
            float radius = ReferenceRadius();
            AddGeneratedPreviewMesh(_asset, mesh, radius, new Vector3(-radius * FootPairOffsetRadii, 0, 0), mirror: true, outline: true, "LeftFoot");
            AddGeneratedPreviewMesh(_asset, mesh, radius, new Vector3(radius * FootPairOffsetRadii, 0, 0), mirror: false, outline: true, "RightFoot");
        }
        else if (_category == AssetCategory.TorsoShape)
        {
            AddGeneratedPreviewMesh(_asset, mesh, ReferenceRadius(), Vector3.Zero, mirror: false, outline: true, "Mesh");
        }
        else if (_category is AssetCategory.Lamp or AssetCategory.Sofa)
        {
            _environmentBounds = EnvironmentGeneratedBounds.Analyze(generated.Mesh);
            _environmentRecipe = generated.Recipe;
            _environmentLogicalHeight = generated.Recipe.Environment.LogicalHeight;
            AddGeneratedPreviewMesh(_asset, mesh, 1f, Vector3.Zero, mirror: false, outline: false,
                _category == AssetCategory.Lamp ? "LampMesh" : "SofaMesh");
            if (_category == AssetCategory.Lamp)
            {
                _lampSettings = generated.Recipe.Light;
                UpdateLampGizmo();
            }
        }
        else
        {
            AddGeneratedPreviewMesh(_asset, mesh, ReferenceRadius(), Vector3.Zero, mirror: false, outline: false, "Mesh");
        }
        ResetView();
    }

    private void AddGeneratedPreviewMesh(Node3D parent, ArrayMesh mesh, float scale, Vector3 position, bool mirror, bool outline, string name)
    {
        var root = new Node3D
        {
            Name = name,
            Position = position,
            Scale = Vector3.One * scale,
            RotationDegrees = mirror ? new Vector3(0, 180f, 0) : Vector3.Zero,
        };
        parent.AddChild(root);
        root.AddChild(new MeshInstance3D
        {
            Name = "Surface",
            Mesh = mesh,
            MaterialOverride = _generatedMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        if (outline)
        {
            root.AddChild(new MeshInstance3D
            {
                Name = "Outline",
                Mesh = mesh,
                MaterialOverride = BuddySharedMaterialFactory.CreateOutlineMaterial(_profile.Look),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    public void SetLampPreviewSettings(double logicalHeight, DecorationLightSettings settings)
    {
        _environmentLogicalHeight = logicalHeight;
        _lampSettings = settings;
        if (_environmentRecipe is not null && _environmentRecipe.Category == AssetCategory.Lamp)
            _environmentRecipe = _environmentRecipe with { Environment = _environmentRecipe.Environment with { LogicalHeight = logicalHeight }, Light = settings };
        UpdateLampGizmo();
        if (_category == AssetCategory.Lamp) ResetView();
    }

    private void UpdateLampGizmo()
    {
        if (_category != AssetCategory.Lamp || !GodotObject.IsInstanceValid(_asset) ||
            _environmentBounds.Width <= 0 || _environmentBounds.Height <= 0) return;
        if (GodotObject.IsInstanceValid(_lampEmitterGizmo)) _lampEmitterGizmo!.QueueFree();
        if (GodotObject.IsInstanceValid(_lampPreviewLight)) _lampPreviewLight!.QueueFree();

        Vector2 emitter2;
        if (_environmentRecipe is not null && EnvironmentTemplateMapping.UsesLiteralTemplateSpace(_environmentRecipe))
        {
            NumericsVector2 mapped = EnvironmentTemplateMapping.SourcePixelToWorld(
                _lampSettings.EmitterX * EnvironmentTemplateSpace.CanvasSize,
                _lampSettings.EmitterY * EnvironmentTemplateSpace.CanvasSize,
                _environmentRecipe);
            emitter2 = new Vector2(mapped.X, mapped.Y);
        }
        else
        {
            emitter2 = new Vector2(
                (float)(_lampSettings.EmitterX - .5) * _environmentBounds.Width,
                -(float)(1.0 - _lampSettings.EmitterY) * _environmentBounds.Height);
        }
        Vector3 position = new(
            emitter2.X,
            emitter2.Y,
            MathF.Max(2f, _environmentBounds.Depth * .65f));
        float radius = Math.Clamp(MathF.Min(_environmentBounds.Width, _environmentBounds.Height) * .045f, 2.5f, 12f);
        Color color = Color.Color8(_lampSettings.Red, _lampSettings.Green, _lampSettings.Blue);
        var material = new StandardMaterial3D
        {
            ResourceName = "LampEmitterGizmoMaterial",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = (float)_lampSettings.EmissionStrength,
        };
        _lampEmitterGizmo = new MeshInstance3D
        {
            Name = "LampEmitterGizmo",
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f },
            Position = position,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _asset!.AddChild(_lampEmitterGizmo);
        if (_lampSettings.LightEnabled)
        {
            _lampPreviewLight = new OmniLight3D
            {
                Name = "LampPreviewLight",
                Position = position,
                LightColor = color,
                LightEnergy = (float)_lampSettings.Brightness,
                OmniRange = (float)_lampSettings.Range,
                ShadowEnabled = false,
            };
            _asset.AddChild(_lampPreviewLight);
        }
    }

    public void SetLightingLevel(float value)
    {
        if (GodotObject.IsInstanceValid(_generatedMaterial))
            _generatedMaterial!.EmissionEnergyMultiplier = Mathf.Clamp(value, 0f, 1f);
    }

    public void SetReferenceVisible(bool visible)
    {
        _referenceVisible = visible;
        if (_category == AssetCategory.Glasses)
        {
            if (GodotObject.IsInstanceValid(_headReference.Root)) _headReference.Root.Visible = visible;
        }
        else if (_category == AssetCategory.FootShape)
        {
            if (GodotObject.IsInstanceValid(_partReference)) _partReference.Visible = visible;
            if (GodotObject.IsInstanceValid(_partReferenceSecondary)) _partReferenceSecondary.Visible = visible;
        }
        else if (_category is AssetCategory.Lamp or AssetCategory.Sofa)
        {
            if (GodotObject.IsInstanceValid(_environmentReference)) _environmentReference.Visible = visible;
        }
        else if (GodotObject.IsInstanceValid(_partReference)) _partReference.Visible = visible;
    }

    public byte[] CaptureThumbnailPng()
    {
        Image image = _viewport.GetTexture().GetImage();
        if (image.IsEmpty()) throw new InvalidOperationException("Preview has no rendered image yet.");
        int sourceSide = Math.Min(image.GetWidth(), image.GetHeight());
        int cropSide = Math.Clamp((int)MathF.Round(sourceSide * ThumbnailCropFraction), 1, sourceSide);
        int cropX = Math.Max(0, (image.GetWidth() - cropSide) / 2);
        int cropY = Math.Max(0, (image.GetHeight() - cropSide) / 2);
        Image cropped = image.GetRegion(new Rect2I(cropX, cropY, cropSide, cropSide));
        cropped.Resize(256, 256, Image.Interpolation.Lanczos);
        return cropped.SavePngToBuffer();
    }

    public void ResetView()
    {
        bool environment = _category is AssetCategory.Lamp or AssetCategory.Sofa;
        _orbit.RotationDegrees = Vector3.Zero;
        _orbit.Position = environment
            ? new Vector3(0, (float)_environmentLogicalHeight * -.5f, 0)
            : Vector3.Zero;
        if (!GodotObject.IsInstanceValid(_camera)) return;
        _camera.Size = _category switch
        {
            AssetCategory.FootShape => ReferenceRadius() * 5.2f,
            AssetCategory.TorsoShape => ReferenceRadius() * 3.4f,
            AssetCategory.Lamp or AssetCategory.Sofa => (float)_environmentLogicalHeight * 1.35f,
            _ => ReferenceRadius() * 3.2f,
        };
        if (environment)
            _camera.Position = new Vector3(0, 0, MathF.Max(400f, (float)_environmentLogicalHeight * 4f));
        else
            _camera.Position = new Vector3(0, 0, ReferenceRadius() * 5f);
    }

    private float ReferenceRadius() => _category switch
    {
        AssetCategory.TorsoShape => _profile.TorsoRadius,
        AssetCategory.FootShape => _profile.FootRadius,
        _ => _profile.HeadRadius,
    };

    private float PreviewScaleBase() => _category is AssetCategory.Lamp or AssetCategory.Sofa
        ? MathF.Max(32f, (float)_environmentLogicalHeight)
        : ReferenceRadius();

    private static ArrayMesh ToGodotMesh(CanonicalMesh source)
    {
        if (source.Indices.Count % 3 != 0)
            throw new InvalidOperationException("Canonical mesh triangle index count is invalid.");
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int triangle = 0; triangle < source.Indices.Count; triangle += 3)
        {
            AddPreviewVertex(source, surface, source.Indices[triangle]);
            AddPreviewVertex(source, surface, source.Indices[triangle + 2]);
            AddPreviewVertex(source, surface, source.Indices[triangle + 1]);
        }
        return surface.Commit();
    }

    private static void AddPreviewVertex(CanonicalMesh source, SurfaceTool surface, uint rawIndex)
    {
        int index = checked((int)rawIndex);
        NumericsVector3 p = source.Positions[index];
        NumericsVector3 n = source.Normals[index];
        NumericsVector2 uv = source.Uvs[index];
        surface.SetNormal(new Vector3(n.X, n.Y, n.Z));
        surface.SetUV(new Vector2(uv.X, uv.Y));
        surface.AddVertex(new Vector3(p.X, p.Y, p.Z));
    }

    private void OnPreviewInput(InputEvent input)
    {
        float basis = PreviewScaleBase();
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left) _rotating = button.Pressed;
            else if (button.ButtonIndex == MouseButton.Middle) _panning = button.Pressed;
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp) _camera.Size = Mathf.Max(basis * .7f, _camera.Size * .9f);
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown) _camera.Size = Mathf.Min(basis * 8f, _camera.Size * 1.1f);
            AcceptEvent();
            return;
        }
        if (input is not InputEventMouseMotion motion) return;
        if (_rotating)
        {
            Vector3 rotation = _orbit.RotationDegrees;
            rotation.Y += motion.Relative.X * .35f;
            rotation.X = Mathf.Clamp(rotation.X + motion.Relative.Y * .25f, -55f, 55f);
            _orbit.RotationDegrees = rotation;
            AcceptEvent();
        }
        else if (_panning)
        {
            float scale = _camera.Size / Math.Max(1f, Size.Y);
            _orbit.Position += new Vector3(motion.Relative.X * scale, -motion.Relative.Y * scale, 0);
            AcceptEvent();
        }
    }
}
