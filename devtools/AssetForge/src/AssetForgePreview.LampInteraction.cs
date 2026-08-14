using DesktopBuddy.AssetForge.Core;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgePreview
{
    private const float LampEmitterHitRadiusPixels = 24f;
    private bool _lampEmitterInteractionInstalled;
    private bool _draggingLampEmitter;

    /// <summary>
    /// Raised while the literal Lamp@2 emitter is dragged in the frontal preview. Values are
    /// normalized coordinates in the fixed 1024x1024 authoring template and can be persisted into
    /// the same recipe fields used by the numeric controls.
    /// </summary>
    public event Action<double, double>? LampEmitterDragged;

    public void EnableLampEmitterInteraction()
    {
        if (_lampEmitterInteractionInstalled) return;
        // AssetForgePreview._Ready installs its normal orbit/pan handler first. This focused handler
        // runs afterward; when it captures the emitter it clears the orbit gesture that the normal
        // left-button handler tentatively started on the same press.
        GuiInput += OnLampEmitterInteraction;
        _lampEmitterInteractionInstalled = true;
    }

    private void OnLampEmitterInteraction(InputEvent input)
    {
        if (_category != AssetCategory.Lamp ||
            _environmentRecipe is null ||
            !EnvironmentTemplateMapping.UsesLiteralTemplateSpace(_environmentRecipe))
            return;

        if (input is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
        {
            if (button.Pressed)
            {
                if (!CanDragLampEmitter(button.Position)) return;
                _rotating = false;
                _draggingLampEmitter = true;
                MouseDefaultCursorShape = CursorShape.Drag;
                AcceptEvent();
                return;
            }

            if (_draggingLampEmitter)
            {
                _draggingLampEmitter = false;
                MouseDefaultCursorShape = CursorShape.Arrow;
                AcceptEvent();
            }
            return;
        }

        if (input is InputEventMouseMotion motion && _draggingLampEmitter)
        {
            DragLampEmitter(motion.Relative);
            AcceptEvent();
        }
    }

    private bool CanDragLampEmitter(Vector2 localPointer)
    {
        if (!GodotObject.IsInstanceValid(_lampEmitterGizmo) || !GodotObject.IsInstanceValid(_camera) ||
            !GodotObject.IsInstanceValid(_viewport) || Size.X <= 0f || Size.Y <= 0f)
            return false;

        // The emitter edits fixed front-template X/Y coordinates. When the user has orbited away
        // from the front, dragging would be ambiguous, so Reset View restores the editable plane.
        Vector3 rotation = _orbit.RotationDegrees;
        if (Mathf.Abs(rotation.X) > .1f || Mathf.Abs(rotation.Y) > .1f || Mathf.Abs(rotation.Z) > .1f)
            return false;

        Vector2 pointer = PreviewControlToViewport(localPointer);
        Vector2 emitter = _camera.UnprojectPosition(_lampEmitterGizmo!.GlobalPosition);
        return pointer.DistanceTo(emitter) <= LampEmitterHitRadiusPixels;
    }

    private void DragLampEmitter(Vector2 controlDelta)
    {
        if (_environmentRecipe is null || !GodotObject.IsInstanceValid(_lampEmitterGizmo) ||
            !GodotObject.IsInstanceValid(_viewport) || Size.X <= 0f || Size.Y <= 0f)
            return;

        Vector2 viewportDelta = new(
            controlDelta.X * (_viewport.Size.X / Size.X),
            controlDelta.Y * (_viewport.Size.Y / Size.Y));
        float worldPerPixel = _camera.Size / Math.Max(1, _viewport.Size.Y);
        Vector3 current = _lampEmitterGizmo!.Position;
        var movedWorld = new NumericsVector2(
            current.X + viewportDelta.X * worldPerPixel,
            current.Y - viewportDelta.Y * worldPerPixel);
        NumericsVector2 normalized = EnvironmentTemplateMapping.WorldToNormalizedSource(movedWorld, _environmentRecipe);
        double x = Math.Clamp(normalized.X, 0f, 1f);
        double y = Math.Clamp(normalized.Y, 0f, 1f);

        _lampSettings = _lampSettings with { EmitterX = x, EmitterY = y };
        _environmentRecipe = _environmentRecipe with { Light = _lampSettings };
        NumericsVector2 snappedWorld = EnvironmentTemplateMapping.SourcePixelToWorld(
            x * EnvironmentTemplateSpace.CanvasSize,
            y * EnvironmentTemplateSpace.CanvasSize,
            _environmentRecipe);
        Vector3 position = new(snappedWorld.X, snappedWorld.Y, current.Z);
        _lampEmitterGizmo.Position = position;
        if (GodotObject.IsInstanceValid(_lampPreviewLight))
            _lampPreviewLight!.Position = position;

        LampEmitterDragged?.Invoke(x, y);
    }

    private Vector2 PreviewControlToViewport(Vector2 local) => new(
        local.X * (_viewport.Size.X / Math.Max(1f, Size.X)),
        local.Y * (_viewport.Size.Y / Math.Max(1f, Size.Y)));
}
