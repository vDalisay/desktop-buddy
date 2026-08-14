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

        // This handler runs after the legacy preview wheel handler. Keep an independent camera-size
        // accumulator so the legacy 0.7x/8x clamp cannot reassert itself on the next wheel event.
        float factor = button.ButtonIndex == MouseButton.WheelUp ? 0.9f : 1.1f;
        if (!float.IsFinite(_unboundedCameraSize) || _unboundedCameraSize <= 0f)
            _unboundedCameraSize = _camera.Size;
        _unboundedCameraSize = Mathf.Max(0.001f, _unboundedCameraSize * factor);
        _camera.Size = _unboundedCameraSize;
    }

    private void SyncUnboundedNavigationToReset()
    {
        if (GodotObject.IsInstanceValid(_camera)) _unboundedCameraSize = _camera.Size;
    }
}
