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

    /// <summary>
    /// Presentation-only displacement in the source body's local drawing plane.
    /// It never moves the tracked physics body or its collider.
    /// </summary>
    Vector2 VisualOffset2D { get; }

    /// <summary>Current one-shot tip-glimmer strength, from <c>0</c> to <c>1</c>.</summary>
    float VisualGlintStrength { get; }

    /// <summary>Authored full size of the glimmer in 2D world pixels.</summary>
    float VisualGlintSizePx { get; }

    /// <summary>Local 2D position of the glimmer on the source body.</summary>
    Vector2 VisualGlintLocalPosition { get; }
}
