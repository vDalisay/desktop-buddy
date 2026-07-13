using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Shared scripted steps for scenarios that compose the buddy laboratory.</summary>
internal static class ScenarioSteps
{
    private const int ControlledImpactTimeoutTicks = 120;
    private const float ControlledImpactSpeed = 400.0f;

    /// <summary>Runs physics until the standing detector reports stable, or times out.</summary>
    public static async Task<bool> WaitForStanding(SceneTree tree, BuddyLab lab, int timeoutTicks)
    {
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.Standing.Snapshot.IsStable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes room/gravity noise without freezing the six authoritative bodies,
    /// leaving only real PhysicalTools contacts for controlled integration probes.
    /// </summary>
    public static void IsolateControlledImpacts(BuddyLab lab)
    {
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
        {
            part.GravityScale = 0.0f;
            part.CollisionMask = CollisionLayers.PhysicalTools;
            part.LinearVelocity = Vector2.Zero;
            part.AngularVelocity = 0.0f;
        }
    }

    /// <summary>
    /// Strikes one requested part with a real RigidBody2D source and waits for the
    /// semantic accepted-impact event bearing that source's interaction ID.
    /// </summary>
    public static async Task<AcceptedImpact?> StrikePart(
        SceneTree tree,
        BuddyLab lab,
        PuppetPartBody target,
        ToolId tool = ToolId.BoxingGlove)
    {
        return await StrikePart(tree, lab, target, (int)tool, null, ControlledImpactSpeed);
    }

    /// <summary>
    /// Controlled strike with explicit attribution. Reusing an interaction ID
    /// across replacement probes models a separated contact episode for the same
    /// originating interaction without teleporting a live solver body.
    /// </summary>
    public static async Task<AcceptedImpact?> StrikePart(
        SceneTree tree,
        BuddyLab lab,
        PuppetPartBody target,
        int contentId,
        int interactionId)
    {
        return await StrikePart(tree, lab, target, contentId, (int?)interactionId, 2000.0f);
    }

    private static async Task<AcceptedImpact?> StrikePart(
        SceneTree tree,
        BuddyLab lab,
        PuppetPartBody target,
        int contentId,
        int? interactionId,
        float speed)
    {
        var source = new ScenarioImpactBody();
        source.Configure(contentId, interactionId: interactionId);

        Vector2 direction = target == lab.Buddy.Rig.Torso
            ? Vector2.Right
            : (lab.Buddy.Rig.Torso.GlobalPosition - target.GlobalPosition).Normalized();
        if (direction.IsZeroApprox())
        {
            direction = Vector2.Right;
        }

        const float sourceRadius = 8.0f;
        source.Position = target.GlobalPosition - direction * (target.Radius + sourceRadius + 2.0f);
        source.LinearVelocity = direction * speed;

        AcceptedImpact? accepted = null;
        void OnAccepted(AcceptedImpact impact)
        {
            if (impact.InteractionId == source.InteractionId && accepted is null)
            {
                accepted = impact;
            }
        }

        lab.Pipeline.ImpactAccepted += OnAccepted;
        lab.AddChild(source);
        for (int tick = 0; tick < ControlledImpactTimeoutTicks && accepted is null; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        lab.Pipeline.ImpactAccepted -= OnAccepted;
        source.QueueFree();
        return accepted;
    }

    /// <summary>
    /// Composes the production laboratory with a scenario-local typed pain
    /// profile, then isolates external contacts before the first physics frame.
    /// </summary>
    public static async Task<BuddyLab?> CreateControlledImpactLab(
        SceneTree tree,
        float maximumPain)
    {
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            return null;
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        lab.Pipeline.Profile = new PainConversionProfile
        {
            ResourceName = "ScenarioControlledPainConversion",
            ImpulseAnchors = new[] { 10.0f, 40.0f },
            PainAnchors = new[] { 0.0f, maximumPain },
            MinimumImpulse = 10.0f,
            CashPerPain = 1.0,
        };
        tree.Root.AddChild(lab);
        IsolateControlledImpacts(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return lab;
    }
}
