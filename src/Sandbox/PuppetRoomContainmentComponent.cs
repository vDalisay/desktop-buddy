using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Physics;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// Keeps the six buddy bodies inside rebuilt room boundaries and updates the
/// centralized recovery seam. Forced corrections remove only outward velocity.
/// </summary>
[GlobalClass]
public partial class PuppetRoomContainmentComponent : Node
{
    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public RecoveryComponent Recovery { get; set; } = null!;
    [Export] public BoundaryProfile Profile { get; set; } = null!;

    public int LastCorrectionCount { get; private set; }
    public int TotalCorrectionCount { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Rig))
        {
            throw new InvalidOperationException("PuppetRoomContainmentComponent requires an injected rig.");
        }

        if (!Rig.IsInitialized)
        {
            throw new InvalidOperationException("PuppetRoomContainmentComponent requires the rig to be initialized.");
        }

        if (!GodotObject.IsInstanceValid(Recovery))
        {
            throw new InvalidOperationException("PuppetRoomContainmentComponent requires injected recovery.");
        }

        if (!Recovery.IsInitialized)
        {
            throw new InvalidOperationException("PuppetRoomContainmentComponent requires recovery to be initialized.");
        }

        if (!GodotObject.IsInstanceValid(Profile))
        {
            throw new InvalidOperationException("PuppetRoomContainmentComponent requires an injected boundary profile.");
        }

        IsInitialized = true;
    }

    public void ApplyLayout(RoomLayout _, Rect2 innerBounds)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("PuppetRoomContainmentComponent is not initialized.");
        }

        Recovery.SafeBounds = innerBounds;
        Recovery.SafePoseOrigin = CalculateSafePoseOrigin(innerBounds);
        LastCorrectionCount = 0;

        foreach (PuppetPartBody body in Rig.Parts)
        {
            if (CorrectBody(body, innerBounds))
            {
                LastCorrectionCount++;
                TotalCorrectionCount++;
            }
        }
    }

    private Vector2 CalculateSafePoseOrigin(Rect2 innerBounds)
    {
        float lowestRestExtent = float.NegativeInfinity;
        foreach (PuppetPartBody body in Rig.Parts)
        {
            PuppetPartDefinition definition = Rig.Profile.FindPart(body.PartId)
                ?? throw new InvalidOperationException($"Missing definition for {body.PartId}.");
            lowestRestExtent = Mathf.Max(lowestRestExtent, definition.RestPosition.Y + definition.Radius);
        }

        return new Vector2(
            innerBounds.GetCenter().X,
            innerBounds.End.Y - lowestRestExtent - Profile.SafePoseFloorClearance);
    }

    private static bool CorrectBody(PuppetPartBody body, Rect2 bounds)
    {
        float minimumX = bounds.Position.X + body.Radius;
        float maximumX = bounds.End.X - body.Radius;
        float minimumY = bounds.Position.Y + body.Radius;
        float maximumY = bounds.End.Y - body.Radius;
        Vector2 before = body.GlobalPosition;
        Vector2 after = new(
            Mathf.Clamp(before.X, minimumX, maximumX),
            Mathf.Clamp(before.Y, minimumY, maximumY));

        if (after.IsEqualApprox(before))
        {
            return false;
        }

        Vector2 velocity = body.LinearVelocity;
        if ((after.X > before.X && velocity.X < 0.0f) || (after.X < before.X && velocity.X > 0.0f))
        {
            velocity.X = 0.0f;
        }

        if ((after.Y > before.Y && velocity.Y < 0.0f) || (after.Y < before.Y && velocity.Y > 0.0f))
        {
            velocity.Y = 0.0f;
        }

        body.GlobalPosition = after;
        body.LinearVelocity = velocity;
        body.Sleeping = false;
        body.ResetPhysicsInterpolation();
        return true;
    }
}
