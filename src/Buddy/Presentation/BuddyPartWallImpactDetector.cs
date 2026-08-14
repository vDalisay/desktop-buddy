using System;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Buddy.Presentation;

/// <summary>Routes positive-pain room-boundary impacts for one physical Buddy part.</summary>
[GlobalClass]
public partial class BuddyPartWallImpactDetector : Node
{
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public BuddyPart TargetPart { get; set; }

    public event Action<AcceptedImpact>? ContactDetected;
    public int DetectionCount { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Pipeline))
            throw new InvalidOperationException("BuddyPartWallImpactDetector requires a pipeline.");

        Pipeline.ImpactAccepted += OnImpactAccepted;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Pipeline))
            Pipeline.ImpactAccepted -= OnImpactAccepted;
    }

    private void OnImpactAccepted(AcceptedImpact impact)
    {
        if (impact.ContentId != ContentIds.RoomBoundary || impact.Part != TargetPart ||
            impact.IsBuddyGrabbed)
            return;

        DetectionCount++;
        ContactDetected?.Invoke(impact);
    }
}
