using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>Static local-anchor and force-limit data for one structural link.</summary>
[GlobalClass]
public partial class PuppetLinkDefinition : Resource
{
    [Export] public StringName LinkId { get; set; } = new();
    [Export] public BuddyPartId PartA { get; set; } = BuddyPartId.Torso;
    [Export] public BuddyPartId PartB { get; set; } = BuddyPartId.Head;
    [Export] public Vector2 LocalAnchorA { get; set; }
    [Export] public Vector2 LocalAnchorB { get; set; }
    [Export] public Vector2 RestOffset { get; set; }
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float Stiffness { get; set; } = 100.0f;
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")] public float Damping { get; set; } = 10.0f;
    [Export(PropertyHint.Range, "0.1,512,0.1,or_greater")] public float MaximumDistance { get; set; } = 64.0f;
    [Export(PropertyHint.Range, "0,50000,0.1,or_greater")] public float LimitStiffness { get; set; } = 500.0f;
    [Export(PropertyHint.Range, "0.1,100000,0.1,or_greater")] public float MaximumForce { get; set; } = 20_000.0f;
}
