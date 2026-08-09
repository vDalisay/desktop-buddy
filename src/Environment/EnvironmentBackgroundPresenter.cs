using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>
/// Opaque painted room backdrop behind the z=0 buddy/gameplay plane. One room-sized quad shows the
/// <see cref="EnvironmentCanvas"/>, whose canonical 0..1 space maps straight onto the quad's UVs.
/// </summary>
public partial class EnvironmentBackgroundPresenter : Node3D
{
    public const float BackdropZ = -100f;
    private MeshInstance3D _quad = null!;
    private StandardMaterial3D _material = null!;
    private ImageTexture _texture = null!;
    private BoundaryController? _boundaries;
    private long _uploadedRevision = -1;

    /// <summary>The painted room background. The editor draws on it; this node shows it.</summary>
    public EnvironmentCanvas Canvas { get; } = new();

    public void Configure(BoundaryController boundaries)
    {
        _boundaries = boundaries;
        if (IsInsideTree()) BindBoundaries();
    }

    public override void _Ready()
    {
        _material = new StandardMaterial3D
        {
            ResourceName = "EnvironmentBackgroundMaterial",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        };
        _quad = new MeshInstance3D
        {
            Name = "EnvironmentBackgroundQuad",
            Mesh = new QuadMesh { Material = _material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_quad);
        Upload();
        BindBoundaries();
    }

    public override void _Process(double delta)
    {
        if (Canvas.Revision != _uploadedRevision) Upload();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_boundaries)) _boundaries!.LayoutApplied -= OnLayoutApplied;
    }

    private void Upload()
    {
        _uploadedRevision = Canvas.Revision;
        Image image = Image.CreateFromData(
            EnvironmentCanvasPolicy.Size, EnvironmentCanvasPolicy.Size, false, Image.Format.Rgba8, Canvas.ClonePixels());
        if (GodotObject.IsInstanceValid(_texture)) _texture.Update(image);
        else _texture = ImageTexture.CreateFromImage(image);
        _material.AlbedoTexture = _texture;
    }

    private void BindBoundaries()
    {
        if (!GodotObject.IsInstanceValid(_boundaries))
        {
            Layout(480, 360);
            return;
        }
        _boundaries!.LayoutApplied -= OnLayoutApplied;
        _boundaries.LayoutApplied += OnLayoutApplied;
        if (_boundaries.IsInitialized)
            Layout((float)_boundaries.CurrentLayout.RoomWidth, (float)_boundaries.CurrentLayout.RoomHeight);
    }

    private void OnLayoutApplied(DesktopBuddy.Domain.Physics.RoomLayout layout, Rect2 innerBounds) =>
        Layout((float)layout.RoomWidth, (float)layout.RoomHeight);

    private void Layout(float width, float height)
    {
        if (!GodotObject.IsInstanceValid(_quad) || width <= 0 || height <= 0) return;
        ((QuadMesh)_quad.Mesh).Size = new Vector2(width, height);
        _quad.Position = new Vector3(width * .5f, -height * .5f, BackdropZ);
    }
}
