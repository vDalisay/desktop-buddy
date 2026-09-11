using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// A sunken well showing the part that is about to be placed, scaled to fit.
///
/// <para>It is the part as the room draws it: the same <see cref="SandboxPartLook"/> model, rendered
/// by a small camera in the room's own 3D world (so the room's lights light it), with the same flat
/// outline and device face on top. The model stands far outside the room, where the room's camera
/// never looks.</para>
/// </summary>
public partial class SandboxPartPreview : Control
{
    private const float Padding = 10.0f;
    private const float CameraDistance = 1000.0f;
    private static readonly Vector3 StageOrigin = new(-200000.0f, 200000.0f, 0.0f);

    private SandboxPartDefinition? _definition;
    private SubViewport? _viewport;
    private Camera3D? _camera;
    private Node3D? _model;
    private Control? _overlay;

    public override void _Ready()
    {
        var container = new SubViewportContainer
        {
            Name = "PreviewStage",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.OffsetLeft = container.OffsetTop = 2.0f;
        container.OffsetRight = container.OffsetBottom = -2.0f;
        AddChild(container);

        _viewport = new SubViewport
        {
            Name = "PreviewViewport",
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            ProcessMode = ProcessModeEnum.Always,
        };
        container.AddChild(_viewport);
        _camera = new Camera3D
        {
            Name = "PreviewCamera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Current = true,
            Position = StageOrigin + new Vector3(0.0f, 0.0f, CameraDistance),
            Far = CameraDistance * 2.0f,
        };
        _viewport.AddChild(_camera);

        _overlay = new Control { Name = "PreviewFace", MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _overlay.Draw += DrawFace;
        AddChild(_overlay);

        Resized += Restage;
        VisibilityChanged += () =>
        {
            if (_viewport is not null)
                _viewport.RenderTargetUpdateMode = IsVisibleInTree() ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        };
        Restage();
    }

    public void Show(SandboxPartDefinition? definition)
    {
        if (ReferenceEquals(definition, _definition))
            return;
        _definition = definition;
        _model?.QueueFree();
        _model = null;
        if (definition is not null && _viewport is not null)
        {
            _model = SandboxPartLook.Build(definition);
            _model.Name = "PreviewModel";
            _model.Position = StageOrigin;
            _viewport.AddChild(_model);
        }
        Restage();
    }

    /// <summary>One scale for both axes, so a Metal Plate still reads as long and thin next to a Wheel.</summary>
    private float FitScale()
    {
        if (_definition is not { } definition)
            return 1.0f;
        float scale = Mathf.Min(
            (Size.X - Padding * 2.0f) / definition.Width,
            (Size.Y - Padding * 2.0f) / definition.Height);
        return Mathf.Clamp(scale, 0.01f, 1.0f);
    }

    private void Restage()
    {
        if (_camera is not null && Size.Y > 4.0f)
            _camera.Size = (Size.Y - 4.0f) / FitScale();   // world units across the well's height
        QueueRedraw();
        _overlay?.QueueRedraw();
    }

    public override void _Draw() =>
        DrawStyleBox(Win98ThemeFactory.Recessed(Win98ThemeFactory.Light, 2), new Rect2(Vector2.Zero, Size));

    private void DrawFace()
    {
        if (_definition is not { } definition || _overlay is null)
            return;
        float scale = FitScale();
        _overlay.DrawSetTransform(Size * 0.5f, 0.0f, new Vector2(scale, scale));
        SandboxPartLook.Draw(_overlay, definition, drawsShape: false, lit: false, extension: 0.0f);
        _overlay.DrawSetTransform(Vector2.Zero);
    }
}
