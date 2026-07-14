using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>Static presentation data for one connector between two rendered parts.</summary>
[GlobalClass]
public partial class ConnectorVisualDefinition : Resource
{
    [Export] public BuddyPartId PartA { get; set; } = BuddyPartId.Torso;
    [Export] public BuddyPartId PartB { get; set; } = BuddyPartId.Head;
    [Export(PropertyHint.Range, "0.01,64,0.01,or_greater")]
    public float Radius { get; set; } = 5.0f;
    [Export] public Color Color { get; set; } = new("477f9f");
    [Export] public float DepthOffset { get; set; } = -20.0f;
}
