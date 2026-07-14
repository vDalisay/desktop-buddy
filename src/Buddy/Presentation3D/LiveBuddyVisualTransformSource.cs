using System;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Default presentation source. It exposes live rig state read-only and never writes
/// transforms, velocities, forces, or gameplay state.
/// </summary>
public sealed class LiveBuddyVisualTransformSource : IBuddyVisualTransformSource
{
    private readonly PuppetRig _rig;

    public LiveBuddyVisualTransformSource(PuppetRig rig)
    {
        if (!GodotObject.IsInstanceValid(rig) || !rig.IsInitialized)
        {
            throw new ArgumentException("The live visual source requires an initialized rig.", nameof(rig));
        }

        _rig = rig;
    }

    public BuddyVisualTransform ReadTransform(BuddyPartId partId)
    {
        PuppetPartBody body = _rig.GetPart(partId);
        return new BuddyVisualTransform(body.GlobalPosition, body.GlobalRotation, body.LinearVelocity);
    }

    public float ReadRadius(BuddyPartId partId) => _rig.GetPart(partId).Radius;
    public string ReadFace() => _rig.Head.Face;
    public float ReadFaceDrawRotation() => _rig.Head.FaceDrawRotation;
}
