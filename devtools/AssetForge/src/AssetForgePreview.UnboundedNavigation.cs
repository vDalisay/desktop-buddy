using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgePreview
{
    private bool _unboundedNavigationInstalled;
    private float _unboundedCameraSize;

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (_unboundedNavigationInstalled || !GodotObject.IsInstanceValid(_camera)) return;
        _unboundedCameraSize = _camera.Size;
        GuiInput += OnUnboundedPreviewWheel;
        _unboundedNavigationInstalled = true;
    }

    private void OnUnboundedPreviewWheel(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true } button ||
            button.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown) ||
            !GodotObject.IsInstanceValid(_camera)) return;

        // The legacy handler runs first and behaves normally inside its old range. Once it hits a
        // clamp boundary, continue from our independent size instead. If Reset View changed the
        // camera, the next in-range wheel event simply rebases this accumulator automatically.
        float basis = PreviewScaleBase();
        float legacyMin = basis * 0.7f;
        float legacyMax = basis * 8f;
        const float epsilon = 0.001f;
        if (button.ButtonIndex == MouseButton.WheelUp)
        {
            if (_camera.Size <= legacyMin + epsilon && _unboundedCameraSize <= legacyMin + epsilon)
            {
                _unboundedCameraSize = Mathf.Max(0.001f, _unboundedCameraSize * 0.9f);
                _camera.Size = _unboundedCameraSize;
            }
            else
            {
                _unboundedCameraSize = _camera.Size;
            }
        }
        else
        {
            if (_camera.Size >= legacyMax - epsilon && _unboundedCameraSize >= legacyMax - epsilon)
            {
                _unboundedCameraSize *= 1.1f;
                _camera.Size = _unboundedCameraSize;
            }
            else
            {
                _unboundedCameraSize = _camera.Size;
            }
        }
    }
}
