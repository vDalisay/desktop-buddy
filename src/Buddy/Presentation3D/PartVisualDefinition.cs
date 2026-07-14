using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>Static presentation data for one of the six authoritative physics parts.</summary>
[GlobalClass]
public partial class PartVisualDefinition : Resource
{
    [Export] public BuddyPartId PartId { get; set; }
    [Export] public Color Color { get; set; } = new("7ac7ff");
    [Export(PropertyHint.Range, "0.01,4,0.01,or_greater")]
    public float MeshRadiusScale { get; set; } = 1.0f;
    [Export] public float DepthOffset { get; set; }
    [Export] public VisualRotationPolicy RotationPolicy { get; set; } = VisualRotationPolicy.Physics;
    [Export(PropertyHint.Range, "0.01,100,0.01,or_greater")]
    public float VelocitySmoothing { get; set; } = 12.0f;
    [Export(PropertyHint.Range, "0,1000,0.1,or_greater")]
    public float VelocitySpeedDeadband { get; set; } = 4.0f;
}
