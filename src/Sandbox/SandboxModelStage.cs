using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// A small offscreen view of one <see cref="PaletteModel"/>, lit by the room itself: it shares the
/// room's 3D world, so the room's lights fall on it exactly as on a placed part, and each stage
/// stands at its own spot far outside the room where no other camera looks. The Build palette's
/// preview and its category icons are both stages.
/// </summary>
public partial class SandboxModelStage : SubViewport
{
    private const float CameraDistance = 1000.0f;
    private static int _stages;

    private readonly Vector3 _origin;
    private readonly Camera3D _camera;
    private Node3D? _model;
    private Vector2 _extent = Vector2.One;

    public SandboxModelStage()
        : this(0.18f, 2.5f)
    {
    }

    /// <param name="maximumZoom">How far a small model may be enlarged to fill the view.</param>
    public SandboxModelStage(float padding, float maximumZoom)
    {
        Padding = padding;
        MaximumZoom = maximumZoom;
        _origin = new Vector3(-200000.0f - 2000.0f * _stages++, 200000.0f, 0.0f);
        TransparentBg = true;
        ProcessMode = ProcessModeEnum.Always;
        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Current = true,
            Position = _origin + new Vector3(0.0f, 0.0f, CameraDistance),
            Far = CameraDistance * 2.0f,
        };
        AddChild(_camera);
        SizeChanged += Frame;
    }

    public float Padding { get; }
    public float MaximumZoom { get; }

    public void Show(PaletteModel? model)
    {
        _model?.QueueFree();
        _model = null;
        if (model is { } shown)
        {
            _model = shown.Node;
            _model.Position += _origin;
            _extent = new Vector2(Mathf.Max(1.0f, shown.Extent.X), Mathf.Max(1.0f, shown.Extent.Y));
            AddChild(_model);
        }
        Frame();
    }

    /// <summary>Fits the model, both axes at one scale, never enlarged past <see cref="MaximumZoom"/>.</summary>
    private void Frame()
    {
        if (Size.X <= 0 || Size.Y <= 0)
            return;
        float padded = 1.0f + Padding * 2.0f;
        float worldHeight = Mathf.Max(_extent.Y, _extent.X * Size.Y / Size.X) * padded;
        _camera.Size = Mathf.Max(worldHeight, Size.Y / MaximumZoom);
        if (RenderTargetUpdateMode != UpdateMode.Always)
            RenderTargetUpdateMode = UpdateMode.Once;
    }
}
