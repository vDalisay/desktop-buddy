using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Optional presentation-only deformation exposed by a tracked 2D body. The
/// 3D counterpart reads it without gaining any authority over the source body.
/// </summary>
public interface IBody2DVisualPulseSource
{
    Vector2 VisualScale2D { get; }
    float VisualRotation2D { get; }
}
