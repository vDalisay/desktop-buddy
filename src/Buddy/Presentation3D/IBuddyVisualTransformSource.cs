using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Read-only pose seam consumed by <see cref="BuddyVisualPresenter"/>. The live
/// implementation wraps the authoritative 2D rig; editor previews and expressive
/// presentation may provide alternate visual poses without gaining physics authority.
/// </summary>
public interface IBuddyVisualTransformSource
{
    BuddyVisualTransform ReadTransform(BuddyPartId partId);
    float ReadRadius(BuddyPartId partId);
    string ReadFace();
    float ReadFaceDrawRotation();
}

/// <summary>One allocation-free sample of a visual part's 2D source state.</summary>
public readonly record struct BuddyVisualTransform(
    Vector2 Position,
    float Rotation,
    Vector2 LinearVelocity);
