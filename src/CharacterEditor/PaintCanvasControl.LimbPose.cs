using System;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class PaintCanvasControl
{
    private PaintPoint[]? _limbPoseOriginalCenters;

    /// <summary>
    /// Editor-only accessibility pose used by the Show limbs checkbox. It spreads hands and feet
    /// while keeping the paint mapper aligned with the visible preview. Gameplay physics and saved
    /// character data are untouched.
    /// </summary>
    public bool ExpandedLimbPose { get; private set; }

    public void SetExpandedLimbPose(bool enabled)
    {
        if (ExpandedLimbPose == enabled)
            return;

        CancelCurve();
        if (_painting)
        {
            Workspace.EndGesture();
            _painting = false;
            _sprayPulseAccumulator = 0;
            Input.UseAccumulatedInput = true;
        }

        if (_mapper.Shapes is not PaintPartShape[] shapes)
            throw new InvalidOperationException("The trusted paint mapper no longer exposes its authored shape array.");

        _limbPoseOriginalCenters ??= CaptureCenters(shapes);
        for (int index = 0; index < shapes.Length; index++)
        {
            PaintPoint center = _limbPoseOriginalCenters[index];
            if (enabled)
                center += LimbPoseOffsetFor(shapes[index].Part);
            shapes[index] = shapes[index] with { Center = center };
        }

        ExpandedLimbPose = enabled;
        SetHover(null);
        WorkspaceChanged?.Invoke();
        QueueRedraw();
    }

    public static PaintPoint LimbPoseOffsetFor(PaintPart part) => part switch
    {
        PaintPart.LeftHand => new PaintPoint(-40.0, 0.0),
        PaintPart.RightHand => new PaintPoint(40.0, 0.0),
        PaintPart.LeftFoot => new PaintPoint(-18.0, 0.0),
        PaintPart.RightFoot => new PaintPoint(18.0, 0.0),
        _ => default,
    };

    private static PaintPoint[] CaptureCenters(PaintPartShape[] shapes)
    {
        var centers = new PaintPoint[shapes.Length];
        for (int index = 0; index < shapes.Length; index++)
            centers[index] = shapes[index].Center;
        return centers;
    }
}
