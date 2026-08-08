using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>Opaque 3D room backdrop placed behind the z=0 buddy/gameplay plane.</summary>
public partial class EnvironmentBackgroundPresenter : Node3D
{
    public const float BackdropZ = -100f;
    private MeshInstance3D _wall = null!;
    private MeshInstance3D _floor = null!;
    private StandardMaterial3D _wallMaterial = null!;
    private StandardMaterial3D _floorMaterial = null!;
    private BoundaryController? _boundaries;
    private EnvironmentBackground _background = EnvironmentBackground.Default;

    public EnvironmentBackground Current => _background;

    public void Configure(BoundaryController boundaries)
    {
        _boundaries = boundaries;
        if (IsInsideTree()) BindBoundaries();
    }

    public override void _Ready()
    {
        _wallMaterial = Material("EnvironmentWallMaterial");
        _floorMaterial = Material("EnvironmentFloorMaterial");
        _wall = Quad("EnvironmentWall", _wallMaterial);
        _floor = Quad("EnvironmentFloor", _floorMaterial);
        AddChild(_wall);
        AddChild(_floor);
        Apply(_background);
        BindBoundaries();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_boundaries)) _boundaries!.LayoutApplied -= OnLayoutApplied;
    }

    public void Apply(in EnvironmentBackground background)
    {
        _background = background;
        if (!GodotObject.IsInstanceValid(_wallMaterial)) return;
        _wallMaterial.AlbedoColor = ToGodot(background.Wall);
        _floorMaterial.AlbedoColor = ToGodot(background.Floor);
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
        if (!GodotObject.IsInstanceValid(_wall) || width <= 0 || height <= 0) return;
        float split = height * .72f;
        float floorHeight = height - split;
        ((QuadMesh)_wall.Mesh).Size = new Vector2(width, split);
        _wall.Position = new Vector3(width * .5f, -split * .5f, BackdropZ);
        ((QuadMesh)_floor.Mesh).Size = new Vector2(width, floorHeight);
        _floor.Position = new Vector3(width * .5f, -(split + floorHeight * .5f), BackdropZ);
    }

    private static MeshInstance3D Quad(string name, Material material) => new()
    {
        Name = name,
        Mesh = new QuadMesh { Material = material },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private static StandardMaterial3D Material(string name) => new()
    {
        ResourceName = name,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static Color ToGodot(EnvironmentColor color) =>
        Color.Color8(color.Red, color.Green, color.Blue, color.Alpha);
}
