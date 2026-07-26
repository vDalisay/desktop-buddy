using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Development-only physical impact probe used by headless scenarios. It is a
/// real RigidBody2D collider on the PhysicalTools layer and enters production
/// contact capture through IImpactSource; it contains no synthetic damage path.
/// </summary>
internal sealed partial class ScenarioImpactBody : RigidBody2D, IImpactSource
{
    public int InteractionId { get; private set; } = InteractionIds.Next();

    public string ContentId { get; private set; } = ContentIds.LooseObject;

    public void Configure(
        string contentId,
        float radius = 8.0f,
        float mass = 0.25f,
        int? interactionId = null)
    {
        if (interactionId.HasValue)
        {
            InteractionId = interactionId.Value;
        }

        ContentId = contentId;
        Mass = mass;
        GravityScale = 0.0f;
        LinearDamp = 0.0f;
        AngularDamp = 0.0f;
        CanSleep = false;
        CollisionLayer = CollisionLayers.PhysicalTools;
        CollisionMask = CollisionLayers.BuddyParts;
        AddChild(new CollisionShape2D
        {
            Shape = new CircleShape2D { Radius = radius },
        });
    }
}
