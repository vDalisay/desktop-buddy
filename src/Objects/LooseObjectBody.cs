using DesktopBuddy.App;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Objects;

/// <summary>
/// Minimal loose physics-object prototype (ROADMAP.md Milestone 1: "loose-object
/// prototype"). The full registry, 24-object cap/eviction, and per-tool presets
/// arrive with the tool catalogue (Milestone 5); this is only enough to prove the
/// grab tether acquires a non-buddy body through the same contract. Impacts
/// attribute to the generic loose-object source until originating-throw
/// attribution lands with the registry (RAGDOLL §7.1).
/// </summary>
[GlobalClass]
public partial class LooseObjectBody : RigidBody2D, IImpactSource
{
    private const float OutlineWidth = 2.0f;
    private static readonly Color OutlineColor = new("183042");
    private static readonly Color FillColor = new("ffd27a");

    public float Radius { get; private set; } = 12.0f;

    public int InteractionId { get; } = InteractionIds.Next();

    public int ContentId => ImpactContent.LooseObject;

    public void Configure(float radius, float mass, float linearDamp, float angularDamp)
    {
        Radius = radius;
        Mass = mass;
        LinearDamp = linearDamp;
        AngularDamp = angularDamp;
        LinearDampMode = DampMode.Replace;
        AngularDampMode = DampMode.Replace;
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = radius } });
        CollisionLayer = CollisionLayers.LooseObjects;
        CollisionMask = CollisionLayers.MaskLooseObjects;
        CanSleep = true;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, FillColor, true, -1.0f, true);
        DrawArc(Vector2.Zero, Radius, 0.0f, Mathf.Tau, 32, OutlineColor, OutlineWidth, true);
    }
}
