using System;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class PaintCanvasControl
{
    private const double LimbConnectorRadius = 7.0;
    private PaintPoint[]? _limbPoseOriginalCenters;

    /// <summary>
    /// Editor-only accessibility pose used by the Show limbs checkbox. It spreads the head, hands
    /// and feet while keeping the paint mapper aligned with the visible preview. Gameplay physics
    /// and saved character data are untouched. Head/neck uses the same paired endpoint/connector
    /// atlas convention as the hand and foot connectors.
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
        // Screen/editor Y grows downward. Pulling the head upward creates a deliberate neck gap
        // while keeping the head's paint surface aligned with the preview rig.
        PaintPart.Head => new PaintPoint(0.0, -28.0),
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

    /// <summary>
    /// Maps a point in the gap between the torso and one limb onto that limb's connector lane.
    /// It serves both poses: the spread-out Show limbs pose and the ordinary one, where the
    /// connector is the visible neck or the join beside a hand.
    /// </summary>
    private bool TryMapLimbConnector(PaintPoint point, double yaw, out PaintHit hit)
    {
        PaintPartShape torso = default;
        bool hasTorso = false;
        foreach (PaintPartShape shape in _mapper.Shapes)
        {
            if (shape.Part != PaintPart.Torso) continue;
            torso = shape;
            hasTorso = true;
            break;
        }

        double cos = Math.Cos(yaw);
        PaintPoint projectedTorso = new(torso.Center.X * cos, torso.Center.Y);
        if (hasTorso)
        {
            foreach (PaintPartShape limb in _mapper.Shapes)
            {
                // Reuse the existing endpoint-owned connector lane for every torso connector,
                // including Head -> neck. No separate neck surface or save path is introduced.
                if (limb.Part is not (PaintPart.Head or PaintPart.LeftHand or PaintPart.RightHand or PaintPart.LeftFoot or PaintPart.RightFoot) ||
                    !IsPartVisible(limb.Part))
                    continue;

                PaintPoint projectedLimb = new(limb.Center.X * cos, limb.Center.Y);
                PaintPoint direction = projectedLimb - projectedTorso;
                double separation = direction.Length;
                if (separation <= torso.Radius + limb.Radius) continue;
                direction *= 1.0 / separation;
                PaintPoint start = projectedTorso + direction * torso.Radius;
                PaintPoint end = projectedLimb - direction * limb.Radius;
                PaintPoint segment = end - start;
                double lengthSquared = (segment.X * segment.X) + (segment.Y * segment.Y);
                if (lengthSquared <= 0.000001) continue;
                PaintPoint relative = point - start;
                double t = Math.Clamp(((relative.X * segment.X) + (relative.Y * segment.Y)) / lengthSquared, 0.0, 1.0);
                PaintPoint closest = start + segment * t;
                PaintPoint offset = point - closest;
                double distance = offset.Length;
                if (distance > LimbConnectorRadius) continue;

                double signedDistance = ((segment.X * offset.Y) - (segment.Y * offset.X)) /
                    Math.Sqrt(lengthSquared);
                double u = Wrap(Math.Asin(Math.Clamp(signedDistance / LimbConnectorRadius, -1.0, 1.0)) / Tau);
                hit = new PaintHit(
                    limb.Part,
                    PaintUvRegion.LimbConnector.MapLocal(new PaintPoint(u, 1.0 - t)),
                    -100.0,
                    IsConnector: true);
                return true;
            }
        }

        hit = default;
        return false;
    }

    private bool TryGetLimbConnectorAxis(PaintPart part, double yaw, out PaintPoint start, out PaintPoint end)
    {
        PaintPartShape torso = default;
        PaintPartShape limb = default;
        bool foundTorso = false;
        bool foundLimb = false;
        foreach (PaintPartShape shape in _mapper.Shapes)
        {
            if (shape.Part == PaintPart.Torso) { torso = shape; foundTorso = true; }
            if (shape.Part == part) { limb = shape; foundLimb = true; }
        }
        if (!foundTorso || !foundLimb)
        {
            start = end = default;
            return false;
        }

        double cos = Math.Cos(yaw);
        PaintPoint projectedTorso = new(torso.Center.X * cos, torso.Center.Y);
        PaintPoint projectedLimb = new(limb.Center.X * cos, limb.Center.Y);
        PaintPoint direction = projectedLimb - projectedTorso;
        double separation = direction.Length;
        if (separation <= torso.Radius + limb.Radius)
        {
            start = end = default;
            return false;
        }
        direction *= 1.0 / separation;
        start = projectedTorso + direction * torso.Radius;
        end = projectedLimb - direction * limb.Radius;
        return true;
    }
}