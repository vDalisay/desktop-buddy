using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    private bool _paintAtlasSamplingGuardApplied;

    /// <summary>
    /// Hand/foot endpoints and their torso connectors share one texture in two half-width atlas
    /// lanes. Sampling exactly at U=0.5 lets linear filtering read the neighbouring lane, which
    /// shows up as an unpaintable vertical stripe on an endpoint seam. Keep each rendered lane on
    /// texel centres instead: [0.5..255.5] for the endpoint and [256.5..511.5] for the connector.
    /// Head remains a full-width paint surface; its neck connector owns its guarded connector
    /// sampling separately so established head painting stays unchanged.
    /// </summary>
    private void EnsurePaintAtlasSamplingGuard()
    {
        if (_paintAtlasSamplingGuardApplied || !IsInitialized)
            return;

        const float laneStart = 0.5f;
        float halfTexel = 0.5f / PaintPolicy.SurfaceSize;
        float guardedLaneWidth = laneStart - (1.0f / PaintPolicy.SurfaceSize);
        Vector3 guardedScale = new(guardedLaneWidth, 1.0f, 1.0f);

        foreach (BuddyPartId part in new[]
        {
            BuddyPartId.LeftHand,
            BuddyPartId.RightHand,
            BuddyPartId.LeftFoot,
            BuddyPartId.RightFoot,
        })
        {
            int index = (int)part;
            if (_paintLayers[index]?.MaterialOverride is not StandardMaterial3D material)
                continue;
            material.Uv1Scale = guardedScale;
            material.Uv1Offset = new Vector3(halfTexel, 0.0f, 0.0f);
        }

        for (int index = 0; index < _connectorPaintLayers.Length; index++)
        {
            if (_connectorPaintLayers[index]?.MaterialOverride is not StandardMaterial3D material)
                continue;
            material.Uv1Scale = guardedScale;
            material.Uv1Offset = new Vector3(laneStart + halfTexel, 0.0f, 0.0f);
        }

        _paintAtlasSamplingGuardApplied = true;
    }

    internal bool PaintAtlasSamplingGuardAppliedForTest => _paintAtlasSamplingGuardApplied;
}
