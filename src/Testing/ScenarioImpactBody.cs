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
internal sealed partial class ScenarioImpactBody :
    RigidBody2D,
    IImpactSource,
    ISwingImpactSource
{
    public int InteractionId { get; private set; } = InteractionIds.Next();

    public string ContentId { get; private set; } = ContentIds.LooseObject;

    public SwingImpactContext SwingContext { get; private set; } =
        SwingImpactContext.FreeSwing;

    public void SetSwingContext(SwingImpactContext context) => SwingContext = context;

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
        AddCircle(radius);
    }

    /// <summary>
    /// Configure the same real solver body as a passive loose-object target.
    /// It deliberately does not register with the production object registry:
    /// the Home-Run Bat laboratory only needs an isolated mass/collider whose
    /// physical travel can be measured without adding semantic object behavior.
    /// </summary>
    public void ConfigureLooseObject(float radius = 8.0f, float mass = 1.0f)
    {
        ContentId = ContentIds.LooseObject;
        Mass = mass;
        GravityScale = 0.0f;
        LinearDamp = 0.0f;
        AngularDamp = 0.0f;
        CanSleep = false;
        CollisionLayer = CollisionLayers.LooseObjects;
        CollisionMask = CollisionLayers.PhysicalTools;
        AddCircle(radius);
    }

    private void AddCircle(float radius)
    {
        AddChild(new CollisionShape2D
        {
            Shape = new CircleShape2D { Radius = radius },
        });
    }
}
