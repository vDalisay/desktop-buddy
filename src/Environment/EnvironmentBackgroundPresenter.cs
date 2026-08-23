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

    /// <summary>
    /// The paint layer sits between the wallpaper band (-90) and wall decorations (-50): paint goes
    /// ON a wallpaper, furniture goes on the paint. Unpainted canvas is transparent, so the
    /// wallpaper — or the opaque base quad when there is none — shows through.
    /// </summary>
    public const float PaintZ = -70f;
    private MeshInstance3D _quad = null!;
    private MeshInstance3D _base = null!;
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
        EnvironmentColor baseColor = EnvironmentCanvasPolicy.DefaultColor;
        _base = new MeshInstance3D
        {
            Name = "EnvironmentBackgroundBaseQuad",
            Mesh = new QuadMesh
            {
                Material = new StandardMaterial3D
                {
                    ResourceName = "EnvironmentBackgroundBaseMaterial",
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    AlbedoColor = Color.Color8(baseColor.Red, baseColor.Green, baseColor.Blue),
                },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_base);

        _material = new StandardMaterial3D
        {
            ResourceName = "EnvironmentBackgroundMaterial",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
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

    /// <summary>
    /// The backdrop quad's on-screen rect. Paint input must map against this, not the whole
    /// viewport: the room sits between the menu bar and the status bar, so using the viewport
    /// stretches the canonical Y axis and offsets every stamp away from the cursor.
    /// </summary>
    public bool TryGetScreenRect(out Rect2 rect)
    {
        rect = default;
        if (!GodotObject.IsInstanceValid(_quad) || GetViewport()?.GetCamera3D() is not Camera3D camera) return false;
        Vector2 size = ((QuadMesh)_quad.Mesh).Size;
        if (size.X <= 0 || size.Y <= 0) return false;

        Vector3 center = _quad.GlobalPosition;
        Vector2 topLeft = camera.UnprojectPosition(center + new Vector3(-size.X * .5f, size.Y * .5f, 0));
        Vector2 bottomRight = camera.UnprojectPosition(center + new Vector3(size.X * .5f, -size.Y * .5f, 0));
        rect = new Rect2(topLeft, bottomRight - topLeft);
        return rect.Size.X > 0 && rect.Size.Y > 0;
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
            Layout(480, 360, new Rect2(0, 0, 480, 360));
            return;
        }
        _boundaries!.LayoutApplied -= OnLayoutApplied;
        _boundaries.LayoutApplied += OnLayoutApplied;
        if (_boundaries.IsInitialized)
        {
            Layout(
                (float)_boundaries.CurrentLayout.RoomWidth,
                (float)_boundaries.CurrentLayout.RoomHeight,
                _boundaries.InnerBounds);
        }
    }

    private void OnLayoutApplied(DesktopBuddy.Domain.Physics.RoomLayout layout, Rect2 innerBounds) =>
        Layout((float)layout.RoomWidth, (float)layout.RoomHeight, innerBounds);

    /// <summary>
    /// The base quad fills the whole room; the paint quad only fills the playable inside of it.
    /// The strip between the wall line and the window edge therefore stays the plain backdrop
    /// instead of taking paint, which is the grey border the room is framed by (owner
    /// instruction 2026-08-22). Painting keeps mapping onto the quad it is drawn on, so the
    /// canonical 0..1 canvas now covers exactly the room the buddy can reach.
    /// </summary>
    private void Layout(float width, float height, Rect2 inner)
    {
        if (!GodotObject.IsInstanceValid(_quad) || width <= 0 || height <= 0) return;
        ((QuadMesh)_base.Mesh).Size = new Vector2(width, height);
        _base.Position = new Vector3(width * .5f, -height * .5f, BackdropZ);

        bool usable = inner.Size.X > 0.0f && inner.Size.Y > 0.0f;
        Vector2 paintSize = usable ? inner.Size : new Vector2(width, height);
        Vector2 paintCentre = usable
            ? inner.Position + (inner.Size * 0.5f)
            : new Vector2(width, height) * 0.5f;
        ((QuadMesh)_quad.Mesh).Size = paintSize;
        _quad.Position = new Vector3(paintCentre.X, -paintCentre.Y, PaintZ);
    }
}
