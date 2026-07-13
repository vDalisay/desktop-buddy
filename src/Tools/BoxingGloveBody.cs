using DesktopBuddy.App;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// The Boxing Glove's physical collider. It lives on the PhysicalTools layer so
/// it strikes buddy parts, loose objects, and room bounds but never projectiles
/// or other tools. Contacts attribute to the Boxing Glove tool for statistics
/// and harmful-history memory (RAGDOLL §7.1); pain comes only from the measured
/// impulse through the shared curve.
/// </summary>
[GlobalClass]
public partial class BoxingGloveBody : RigidBody2D, IImpactSource
{
    private const int CircleSegments = 32;
    private const float OutlineWidth = 2.0f;
    private static readonly Color OutlineColor = new("5c1a1a");
    private static readonly Color GloveColor = new("e05b4b");

    public float Radius { get; private set; } = 14.0f;

    public int InteractionId { get; } = InteractionIds.Next();

    public int ContentId => (int)ToolId.BoxingGlove;

    public void Configure(BoxingGloveProfile profile)
    {
        Radius = profile.Radius;
        Mass = profile.Mass;
        LinearDamp = profile.LinearDamp;
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = profile.Radius } });
        CollisionLayer = CollisionLayers.PhysicalTools;
        CollisionMask = CollisionLayers.MaskPhysicalTools;
        // The tether must never fight the sleep heuristic while the tool is held.
        CanSleep = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, GloveColor, true, -1.0f, true);
        DrawArc(Vector2.Zero, Radius, 0.0f, Mathf.Tau, CircleSegments, OutlineColor, OutlineWidth, true);
    }
}
