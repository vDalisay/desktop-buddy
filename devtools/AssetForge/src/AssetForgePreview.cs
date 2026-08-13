using DesktopBuddy.AssetForge.Core;
using DesktopBuddy.Buddy.Presentation3D.Shared;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgePreview : Control
{
    private const float ThumbnailCropFraction = 0.78f;

    private SubViewport _viewport = null!;
    private Node3D _orbit = null!;
    private Camera3D _camera = null!;
    private BuddyReferenceHead _headReference = null!;
    private MeshInstance3D _partReference = null!;
    private Node3D? _asset;
    private StandardMaterial3D? _generatedMaterial;
    private TrustedBuddyPreviewProfile _profile;
    private AssetCategory _category = AssetCategory.Glasses;
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
        _partReference = new MeshInstance3D
        {
            Name = "PartReference",
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _orbit.AddChild(_partReference);
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

    public void SetCategory(AssetCategory category)
    {
        _category = category;
        if (!GodotObject.IsInstanceValid(_partReference) || !GodotObject.IsInstanceValid(_headReference.Root)) return;

        if (category == AssetCategory.Glasses)
        {
            _partReference.Visible = false;
            _headReference.Root.Visible = _referenceVisible;
        }
        else
        {
            float radius = ReferenceRadius();
            Color color = category == AssetCategory.TorsoShape ? _profile.TorsoColor : _profile.FootColor;
            _partReference.Mesh = new SphereMesh { Radius = radius, Height = radius * 2f };
            _partReference.MaterialOverride = new StandardMaterial3D
            {
                ResourceName = "AssetForgePartReference",
                AlbedoColor = new Color(color.R, color.G, color.B, 0.20f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                DiffuseMode = _profile.Look.DiffuseMode,
                SpecularMode = _profile.Look.SpecularMode,
                Specular = _profile.Look.Specular,
                Roughness = _profile.Look.Roughness,
            };
            _partReference.Visible = _referenceVisible;
            _headReference.Root.Visible = false;
        }
        ResetView();
    }

    public void ShowGenerated(GeneratedAsset generated, string sourcePath)
    {
        _ = sourcePath;
        SetCategory(generated.Recipe.Category);
        if (GodotObject.IsInstanceValid(_asset)) _asset!.QueueFree();
        _generatedMaterial = null;

        float targetRadius = ReferenceRadius();
        _asset = new Node3D { Name = "GeneratedAsset", Scale = Vector3.One * targetRadius };
        if (_category == AssetCategory.Glasses) _headReference.EyeGroup.AddChild(_asset);
        else _orbit.AddChild(_asset);
        ArrayMesh mesh = ToGodotMesh(generated.Mesh);

        RgbaImage runtime = PngCodec.DecodeRgba8(generated.AlbedoPng);
        Image source = Image.CreateFromData(runtime.Width, runtime.Height, false, Image.Format.Rgba8, runtime.Pixels);
        Texture2D texture = ImageTexture.CreateFromImage(source);
        _generatedMaterial = BuddySharedMaterialFactory.CreateGeneratedAssetMaterial(_profile.Look, texture, Colors.White);
        _generatedMaterial.AlbedoTextureForceSrgb = true;
        SetLightingLevel((float)generated.Recipe.LightingLevel);

        _asset.AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = mesh,
            MaterialOverride = _generatedMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        ResetView();
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
        _orbit.RotationDegrees = Vector3.Zero;
        _orbit.Position = Vector3.Zero;
        if (GodotObject.IsInstanceValid(_camera)) _camera.Size = ReferenceRadius() * 3.2f;
    }

    private float ReferenceRadius() => _category switch
    {
        AssetCategory.TorsoShape => _profile.TorsoRadius,
        AssetCategory.FootShape => _profile.FootRadius,
        _ => _profile.HeadRadius,
    };

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
        float radius = ReferenceRadius();
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left) _rotating = button.Pressed;
            else if (button.ButtonIndex == MouseButton.Middle) _panning = button.Pressed;
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp) _camera.Size = Mathf.Max(radius * 1.4f, _camera.Size * 0.9f);
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown) _camera.Size = Mathf.Min(radius * 7f, _camera.Size * 1.1f);
            AcceptEvent();
            return;
        }
        if (input is not InputEventMouseMotion motion) return;
        if (_rotating)
        {
            Vector3 rotation = _orbit.RotationDegrees;
            rotation.Y += motion.Relative.X * 0.35f;
            rotation.X = Mathf.Clamp(rotation.X + motion.Relative.Y * 0.25f, -55f, 55f);
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
