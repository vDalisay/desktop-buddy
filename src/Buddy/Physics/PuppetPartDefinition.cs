using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>Static physics data for one puppet circle.</summary>
[GlobalClass]
public partial class PuppetPartDefinition : Resource
{
    [Export] public BuddyPartId PartId { get; set; }
    [Export(PropertyHint.Range, "1,128,0.1,or_greater")] public float Radius { get; set; } = 16.0f;
    [Export(PropertyHint.Range, "0.01,100,0.01,or_greater")] public float Mass { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,100,0.01,or_greater")] public float LinearDamp { get; set; } = 2.0f;
    [Export(PropertyHint.Range, "0,100,0.01,or_greater")] public float AngularDamp { get; set; } = 4.0f;
    [Export] public Vector2 RestPosition { get; set; }
}
