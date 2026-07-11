using System;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// One authoritative circular rigid body. It owns only body configuration and
/// direct shape rendering; behavior and structural forces live in components.
/// </summary>
[GlobalClass]
public partial class PuppetPartBody : RigidBody2D
{
    private const int CircleSegments = 40;
    private const float OutlineWidth = 2.0f;
    private static readonly Color OutlineColor = new("183042");

    [Export] public BuddyPartId PartId { get; set; }
    [Export] public CollisionShape2D Collider { get; set; } = null!;

    public float Radius { get; private set; } = 16.0f;
    public Color FillColor { get; private set; } = new("7ac7ff");
    public bool HasSupportContact { get; private set; }
    public int SupportContactCount { get; private set; }

    public void Configure(PuppetPartDefinition definition, Vector2 globalOrigin)
    {
        if (definition.PartId != PartId)
        {
            throw new InvalidOperationException($"Body {PartId} received definition {definition.PartId}.");
        }

        if (!GodotObject.IsInstanceValid(Collider) || Collider.Shape is not CircleShape2D circle)
        {
            throw new InvalidOperationException($"Body {PartId} requires an injected CircleShape2D collider.");
        }

        Radius = definition.Radius;
        FillColor = definition.FillColor;
        Mass = definition.Mass;
        LinearDamp = definition.LinearDamp;
        AngularDamp = definition.AngularDamp;
        GlobalPosition = globalOrigin + definition.RestPosition;
        GlobalRotation = 0.0f;
        LinearVelocity = Vector2.Zero;
        AngularVelocity = 0.0f;

        circle.Radius = Radius;
        CollisionLayer = CollisionLayers.BuddyParts;
        CollisionMask = CollisionLayers.MaskBuddyParts;
        CanSleep = false;
        ContactMonitor = true;
        MaxContactsReported = 8;
        QueueRedraw();
    }

    public bool HasFiniteState() =>
        GlobalPosition.IsFinite() &&
        LinearVelocity.IsFinite() &&
        float.IsFinite(GlobalRotation) &&
        float.IsFinite(AngularVelocity);

    public override void _IntegrateForces(PhysicsDirectBodyState2D state)
    {
        HasSupportContact = false;
        SupportContactCount = 0;
        int contactCount = state.GetContactCount();
        for (int index = 0; index < contactCount; index++)
        {
            GodotObject? colliderObject = state.GetContactColliderObject(index);
            if (colliderObject is not CollisionObject2D collider ||
                (collider.CollisionLayer & CollisionLayers.RoomBounds) == 0)
            {
                continue;
            }

            Vector2 worldNormal = state.GetContactLocalNormal(index).Rotated(GlobalRotation);
            if (Mathf.Abs(worldNormal.Y) > 0.45f)
            {
                HasSupportContact = true;
                SupportContactCount++;
            }
        }
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, FillColor, true, -1.0f, true);
        DrawArc(Vector2.Zero, Radius, 0.0f, Mathf.Tau, CircleSegments, OutlineColor, OutlineWidth, true);
    }
}
