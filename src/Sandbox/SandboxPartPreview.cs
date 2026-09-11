using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// A sunken well showing the part that is about to be placed, scaled to fit.
///
/// <para>It is the part as the room draws it: the same <see cref="SandboxPartLook"/> model, rendered
/// by a small camera in the room's own 3D world, so the room's lights light it. The model stands far
/// outside the room, where the room's camera never looks.</para>
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

        Resized += Restage;
        VisibilityChanged += () =>
        {
            if (_viewport is not null)
                _viewport.RenderTargetUpdateMode = IsVisibleInTree() ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        };
        Restage();
    }

    // Links and wires are drawn, not modelled, in the room too; the preview draws them the same way.
    private static readonly Color Wood = new("b4813f");
    private static readonly Color Metal = new("8f9bab");
    private static readonly Color Ink = new("2a2118");
    private static readonly Color Rope = new("8b5a2b");
    private static readonly Color Pin = new("f0f0f0");
    private static readonly Color CapRed = new("d0392b");
    private static readonly Color BulbOff = new("b3ad93");

    private enum Drawing
    {
        None = 0,
        Link,
        Wire,
    }

    private Drawing _drawing;
    private SandboxLinkKind _linkKind;
    private float _strength;
    private float _elasticity;
    private float _stiffness;
    private SandboxWireColor _wireColor;

    /// <summary>Shows a link as it will look with this tuning: a rope's thickness and stretch, a hinge's stiffness.</summary>
    public void ShowLink(SandboxLinkKind kind, float strength, float elasticity, float stiffness)
    {
        Show(null);
        _drawing = Drawing.Link;
        (_linkKind, _strength, _elasticity, _stiffness) = (kind, strength, elasticity, stiffness);
        QueueRedraw();
    }

    public void ShowWire(SandboxWireColor color)
    {
        Show(null);
        _drawing = Drawing.Wire;
        _wireColor = color;
        QueueRedraw();
    }

    public void Show(SandboxPartDefinition? definition)
    {
        _drawing = Drawing.None;
        QueueRedraw();
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
    }

    public override void _Draw()
    {
        DrawStyleBox(Win98ThemeFactory.Recessed(Win98ThemeFactory.Light, 2), new Rect2(Vector2.Zero, Size));
        Vector2 c = Size * 0.5f;
        switch (_drawing)
        {
            case Drawing.Link when _linkKind == SandboxLinkKind.Rope:
                // A block hanging from a beam: thicker for a stronger rope, dashed and longer for a bungee.
                var beam = new Rect2(c.X - 60.0f, 8.0f, 120.0f, 10.0f);
                Box(beam, Wood);
                float drop = Mathf.Lerp(c.Y - 4.0f, Size.Y - 34.0f, _elasticity);
                var from = new Vector2(c.X, beam.End.Y);
                var to = new Vector2(c.X, drop);
                float width = SandboxLinkView.RopeWidthFor(_strength);
                if (_elasticity >= 0.5f)
                    DrawDashedLine(from, to, Rope, width, 5.0f, true, true);
                else
                    DrawLine(from, to, Rope, width, true);
                Box(new Rect2(c.X - 14.0f, drop, 28.0f, 22.0f), Metal);
                break;
            case Drawing.Link when _linkKind == SandboxLinkKind.Hinge:
                // Two beams on one pin; a stiff hinge shows its spring.
                var pivot = new Vector2(c.X + 6.0f, c.Y);
                DrawSetTransform(pivot, 0.0f, Vector2.One);
                Box(new Rect2(-76.0f, -6.0f, 82.0f, 12.0f), Wood);
                DrawSetTransform(pivot, -0.6f, Vector2.One);
                Box(new Rect2(-6.0f, -6.0f, 70.0f, 12.0f), Wood);
                DrawSetTransform(Vector2.Zero);
                if (_stiffness > 0.0f)
                    DrawArc(pivot, 12.0f, -0.6f, 0.0f, 12, Ink, 1.0f + 2.0f * _stiffness, true);
                DrawCircle(pivot, 6.0f, Pin, true, -1.0f, true);
                DrawArc(pivot, 6.0f, 0.0f, Mathf.Tau, 20, Ink, 1.5f, true);
                DrawCircle(pivot, 1.8f, Ink, true, -1.0f, true);
                break;
            case Drawing.Link:
                // Two overlapping blocks and the weld plate between them.
                Box(new Rect2(c.X - 38.0f, c.Y - 18.0f, 44.0f, 30.0f), Metal);
                Box(new Rect2(c.X - 6.0f, c.Y - 8.0f, 44.0f, 30.0f), Metal);
                var plate = new Rect2(c.X - 6.0f, c.Y - 8.0f, 12.0f, 12.0f);
                DrawRect(plate, Metal.Darkened(0.2f), filled: true);
                DrawRect(plate, Ink, filled: false, 1.5f);
                DrawLine(plate.Position, plate.End, Ink, 1.0f, true);
                break;
            case Drawing.Wire:
                // A Button sending to a Lamp, in this wire's colour.
                var button = new Rect2(c.X - 76.0f, c.Y - 2.0f, 36.0f, 16.0f);
                Box(button, Metal);
                DrawRect(new Rect2(button.Position.X + 8.0f, button.Position.Y - 6.0f, 20.0f, 6.0f), CapRed, filled: true);
                Box(new Rect2(c.X + 44.0f, c.Y + 4.0f, 26.0f, 12.0f), Metal);
                var bulb = new Vector2(c.X + 57.0f, c.Y - 5.0f);
                DrawCircle(bulb, 9.0f, BulbOff, true, -1.0f, true);
                DrawArc(bulb, 9.0f, 0.0f, Mathf.Tau, 20, Ink, 1.5f, true);
                var start = new Vector2(button.End.X, button.GetCenter().Y);
                var end = new Vector2(c.X + 44.0f, c.Y + 6.0f);
                Color color = SandboxLinkView.WireColorOf(_wireColor);
                DrawLine(start, end, Ink, 3.5f, true);
                DrawLine(start, end, color, 2.0f, true);
                Vector2 along = (end - start).Normalized() * 6.0f;
                Vector2 middle = (start + end) * 0.5f;
                DrawColoredPolygon([middle + along, middle - along + along.Orthogonal() * 0.8f, middle - along - along.Orthogonal() * 0.8f], color);
                break;
        }
    }

    private void Box(Rect2 rect, Color fill)
    {
        DrawRect(rect, fill, filled: true);
        DrawRect(rect, Ink, filled: false, 1.5f);
    }
}
