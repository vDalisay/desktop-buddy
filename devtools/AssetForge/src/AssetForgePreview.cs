using DesktopBuddy.AssetForge.Core;
using DesktopBuddy.Buddy.Presentation3D.Shared;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgePreview : Control
{
    private SubViewport _viewport = null!;
    private Node3D _orbit = null!;
    private Camera3D _camera = null!;
    private BuddyReferenceHead _reference = null!;
    private Node3D? _asset;
    private TrustedBuddyPreviewProfile _profile;
    private bool _rotating;
    private bool _panning;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        GuiInput += OnPreviewInput;
        var container = new SubViewportContainer
        {
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(container);
        _viewport = new SubViewport
        {
            Name = "PreviewViewport",
            // Blend the 3D reference/asset over the Asset Forge UI instead of showing the
            // SubViewport's default black clear rectangle. This is also the correct basis for
            // transparent catalogue thumbnails.
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
        _reference = BuddyReferenceHeadFactory.Build(
            _orbit, _profile.HeadRadius, _profile.FaceDepthEpsilon, _profile.HeadColor, _profile.Look);
        DirectionalLight3D key = BuddySharedMaterialFactory.CreateDirectionalLight(
            "KeyLight", _profile.Look.KeyColor, _profile.Look.KeyEnergy, _profile.Look.KeyEulerDegrees);
        DirectionalLight3D fill = BuddySharedMaterialFactory.CreateDirectionalLight(
            "FillLight", _profile.Look.FillColor, _profile.Look.FillEnergy, _profile.Look.FillEulerDegrees);
        world.AddChild(key);
        world.AddChild(fill);
        _camera = new Camera3D
        {
            Name = "Camera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = _profile.HeadRadius * 3.2f,
            Position = new Vector3(0, 0, _profile.HeadRadius * 5f),
            Current = true,
        };
        world.AddChild(_camera);
        ResetView();
    }

    public void ShowGenerated(GeneratedAsset generated, string sourcePath)
    {
        _ = sourcePath; // retained in the public signature for compatibility with the current UI caller.
        if (GodotObject.IsInstanceValid(_asset)) _asset!.QueueFree();
        _asset = new Node3D { Name = "GeneratedAsset", Scale = Vector3.One * _profile.HeadRadius };
        _reference.EyeGroup.AddChild(_asset);
        ArrayMesh mesh = ToGodotMesh(generated.Mesh);

        // Preview exactly the canonical albedo that will be exported. Loading the raw author PNG
        // here was wrong for opaque white-canvas art: the geometry used one interpretation while
        // the preview material still sampled the unprocessed background.
        RgbaImage runtime = PngCodec.DecodeRgba8(generated.AlbedoPng);
        Image source = Image.CreateFromData(
            runtime.Width,
            runtime.Height,
            false,
            Image.Format.Rgba8,
            runtime.Pixels);
        Texture2D texture = ImageTexture.CreateFromImage(source);
        var instance = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = mesh,
            // Generated alpha has already become silhouette/holes. Use the same opaque material
            // contract as the shipping cosmetic renderer so the Forge preview cannot drift.
            MaterialOverride = BuddySharedMaterialFactory.CreateGeneratedAssetMaterial(
                _profile.Look,
                texture,
                Colors.White),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _asset.AddChild(instance);
        ResetView();
    }

    public void SetReferenceVisible(bool visible) => _reference.Root.Visible = visible;

    public byte[] CaptureThumbnailPng()
    {
        Image image = _viewport.GetTexture().GetImage();
        if (image.IsEmpty()) throw new InvalidOperationException("Preview has no rendered image yet.");
        image.Resize(256, 256, Image.Interpolation.Lanczos);
        return image.SavePngToBuffer();
    }

    public void ResetView()
    {
        _orbit.RotationDegrees = Vector3.Zero;
        _orbit.Position = Vector3.Zero;
        if (GodotObject.IsInstanceValid(_camera)) _camera.Size = _profile.HeadRadius * 3.2f;
    }

    private static ArrayMesh ToGodotMesh(CanonicalMesh source)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach (uint rawIndex in source.Indices)
        {
            int index = checked((int)rawIndex);
            NumericsVector3 p = source.Positions[index];
            NumericsVector3 n = source.Normals[index];
            NumericsVector2 uv = source.Uvs[index];
            surface.SetNormal(new Vector3(n.X, n.Y, n.Z));
            surface.SetUV(new Vector2(uv.X, uv.Y));
            surface.AddVertex(new Vector3(p.X, p.Y, p.Z));
        }
        return surface.Commit();
    }

    private void OnPreviewInput(InputEvent input)
    {
        if (input is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left) _rotating = button.Pressed;
            else if (button.ButtonIndex == MouseButton.Middle) _panning = button.Pressed;
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp) _camera.Size = Mathf.Max(_profile.HeadRadius * 1.4f, _camera.Size * 0.9f);
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown) _camera.Size = Mathf.Min(_profile.HeadRadius * 7f, _camera.Size * 1.1f);
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
