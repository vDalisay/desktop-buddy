using Godot;

namespace DesktopBuddy.Laboratory;

/// <summary>
/// Throwaway Windows transparency spike. Originally the M1 client-coordinate readout;
/// extended for M3.5 Task 1 (renderer spike) to prove an orthographic 3D pass composites
/// over the transparent desktop shell with color parity against the 2D pass.
///
/// Development-only, export-excluded. Not wired into any shipping scene.
///
/// Transparent-safe 3D configuration pinned here (the Task 1 recorded outcome):
///   * Viewport.TransparentBg = true and window per-pixel transparency allowed.
///   * NO WorldEnvironment / Camera3D.Environment anywhere — a sky or opaque clear color
///     paints over the desktop and kills the transparent shell.
///   * Camera3D orthographic, KeepAspect = Height, matching the Task 2 mapping
///     (x, y) -> (x, -y, 0), positioned at (W/2, -H/2, +CameraDistance) looking -Z.
///   * All materials StandardMaterial3D with ShadingMode = Unshaded so 3D albedo matches
///     the 2D fill under the gl_compatibility gamma pipeline.
///
/// Interactive validation matrix (drive on real Windows via the Godot MCP tier):
///   0 / 2 / 4 / 8 : set Msaa3D Off / 2x / 4x / 8x (mirrors onto Msaa2D too)
///   S            : toggle V-sync Enabled/Disabled
///   R            : toggle window size 480x360 <-> 700x520 (resize check)
/// </summary>
public partial class TransparentWindowSpike : Node2D
{
    // View-plumbing constants: provably invisible to the orthographic result.
    private const float CameraDistance = 500f;
    private const int DefaultWidth = 480;
    private const int DefaultHeight = 360;
    private const int ResizedWidth = 700;
    private const int ResizedHeight = 520;

    // Single reference color drawn as both a 2D rect and a 3D unshaded quad (parity check).
    private static readonly Color ReferenceColor = new(0.85f, 0.35f, 0.25f, 1f);

    private Label _readout = null!;
    private Camera3D _camera = null!;
    private Viewport.Msaa _msaa = Viewport.Msaa.Disabled;
    private bool _vsync = true;
    private bool _resized;

    public override void _Ready()
    {
        var window = GetWindow();
        window.Size = new Vector2I(DefaultWidth, DefaultHeight);
        window.Borderless = true;
        window.AlwaysOnTop = true;
        window.Transparent = true;
        GetViewport().TransparentBg = true;

        _readout = GetNode<Label>("Readout");

        BuildCamera();
        BuildMeshes();
        ApplyMsaa();
        ApplyVsync();
        QueueRedraw();
    }

    private void BuildCamera()
    {
        // Orthographic camera driven exactly like the future WorldCamera3D (Task 2):
        // maps world (x, y) -> 3D (x, -y, 0) so 3D content lands on the same screen pixels
        // as the 2D pass. No Environment assigned -> transparent clear survives.
        _camera = new Camera3D
        {
            Name = "SpikeCamera3D",
            Projection = Camera3D.ProjectionType.Orthogonal,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Size = DefaultHeight,
            Position = new Vector3(DefaultWidth / 2f, -DefaultHeight / 2f, CameraDistance),
            Current = true,
            // Presenter-driven nodes are positioned per rendered frame; layering engine
            // interpolation on top re-quantizes to tick boundaries (global constraint 6).
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        };
        // Identity look direction (-Z): camera basis is already identity, so it looks down -Z.
        AddChild(_camera);
    }

    private void BuildMeshes()
    {
        // Sphere + capsule near z = 0, plus the 3D half of the color-parity pair. All
        // unshaded so albedo prints straight through the gamma pipeline.
        AddMesh(new SphereMesh { Radius = 40f, Height = 80f },
            new Vector3(180f, -180f, 0f), new Color(0.30f, 0.75f, 1f, 1f));

        AddMesh(new CapsuleMesh { Radius = 26f, Height = 120f },
            new Vector3(300f, -180f, 0f), new Color(0.45f, 0.9f, 0.55f, 1f));

        // 3D unshaded quad in the reference color, placed to sit at screen (300, 70),
        // immediately right of the 2D reference rect drawn in _Draw.
        var quad = AddMesh(new QuadMesh { Size = new Vector2(56f, 56f) },
            new Vector3(300f, -70f, 0f), ReferenceColor);
        quad.Name = "ColorParityQuad3D";
    }

    private MeshInstance3D AddMesh(Mesh mesh, Vector3 position, Color color)
    {
        var instance = new MeshInstance3D
        {
            Mesh = mesh,
            Position = position,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = color,
            },
        };
        AddChild(instance);
        return instance;
    }

    private void ApplyMsaa()
    {
        // Set both passes: Msaa3D is the value under test; mirroring Msaa2D keeps the 2D
        // reference rect edges comparable in the same capture.
        GetViewport().Msaa3D = _msaa;
        GetViewport().Msaa2D = _msaa;
    }

    private void ApplyVsync()
    {
        DisplayServer.WindowSetVsyncMode(_vsync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Key0:
                _msaa = Viewport.Msaa.Disabled;
                ApplyMsaa();
                break;
            case Key.Key2:
                _msaa = Viewport.Msaa.Msaa2X;
                ApplyMsaa();
                break;
            case Key.Key4:
                _msaa = Viewport.Msaa.Msaa4X;
                ApplyMsaa();
                break;
            case Key.Key8:
                _msaa = Viewport.Msaa.Msaa8X;
                ApplyMsaa();
                break;
            case Key.S:
                _vsync = !_vsync;
                ApplyVsync();
                break;
            case Key.R:
                _resized = !_resized;
                GetWindow().Size = _resized
                    ? new Vector2I(ResizedWidth, ResizedHeight)
                    : new Vector2I(DefaultWidth, DefaultHeight);
                break;
        }
    }

    public override void _Process(double delta)
    {
        Vector2 client = GetViewport().GetMousePosition();
        Vector2I screen = DisplayServer.MouseGetPosition();
        float scale = DisplayServer.ScreenGetScale(DisplayServer.WindowGetCurrentScreen());
        _readout.Text =
            $"client {client.X:F1}, {client.Y:F1}\n" +
            $"screen {screen.X}, {screen.Y}\n" +
            $"DPI scale {scale:F2}\n" +
            $"MSAA {MsaaLabel(_msaa)}  V-sync {(_vsync ? "on" : "off")}";
    }

    private static string MsaaLabel(Viewport.Msaa msaa) => msaa switch
    {
        Viewport.Msaa.Msaa2X => "2x",
        Viewport.Msaa.Msaa4X => "4x",
        Viewport.Msaa.Msaa8X => "8x",
        _ => "off",
    };

    public override void _Draw()
    {
        DrawRect(new Rect2(12, 12, 456, 336), new Color(0.2f, 0.8f, 1, 0.9f), false, 2);
        // 2D half of the color-parity pair: same ReferenceColor, immediately left of the
        // 3D quad which lands at screen (300, 70). On gl_compatibility they must match.
        DrawRect(new Rect2(216, 42, 56, 56), ReferenceColor);
        DrawString(ThemeDB.FallbackFont, new Vector2(150, 120), "2D | 3D parity",
            HorizontalAlignment.Left, -1, 14, new Color(0.9f, 0.95f, 1f, 0.9f));
    }
}
