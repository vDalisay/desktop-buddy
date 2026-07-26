using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Presentation3D;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Semantic coverage for M3 face, chirp, fear resistance, and money HUD.</summary>
public sealed class M3PresentationScenario : IScenario
{
    public string Id => "m3_presentation";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(tree, 10.0f);
        if (lab is null)
        {
            checks.Add(new StartupCheck("presentation_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        bool visualProfileDrivesLegacyColors = lab.Buddy.VisualProfile.Validate().Count == 0;
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            BuddyPartId id = (BuddyPartId)index;
            visualProfileDrivesLegacyColors &=
                lab.Buddy.VisualProfile.FindPart(id)?.Color == lab.Buddy.Rig.GetPart(id).FillColor;
        }

        checks.Add(new StartupCheck("visual_profile_drives_legacy_part_colors",
            visualProfileDrivesLegacyColors,
            $"parts={PuppetRigProfile.RequiredPartCount}"));

        var visualPresenter = new BuddyVisualPresenter
        {
            Name = "Task4BuddyVisualPresenter",
            Buddy = lab.Buddy,
            Profile = lab.Buddy.VisualProfile,
        };
        lab.AddChild(visualPresenter);
        visualPresenter.Initialize();
        visualPresenter.CaptureTickSnapshot();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool socketHierarchyBuilt =
            visualPresenter.PartVisualCount == PuppetRigProfile.RequiredPartCount &&
            visualPresenter.ConnectorVisualCount == lab.Buddy.VisualProfile.Connectors.Count &&
            GodotObject.IsInstanceValid(visualPresenter.FaceLabel) &&
            visualPresenter.GetPartSocket(BuddyPartId.Head).GetParent() == visualPresenter.BodyYaw;
        checks.Add(new StartupCheck("task4_presenter_socket_hierarchy_built",
            socketHierarchyBuilt,
            $"parts={visualPresenter.PartVisualCount} connectors={visualPresenter.ConnectorVisualCount}"));

        // The settled torso/hand circles overlap, so their connector is intentionally
        // clamped to the midpoint nub. The torso/foot gap is open and exercises the
        // unequal-radius surface-to-surface centering formula directly.
        int unequalRadiusConnector = FindConnector(lab.Buddy.VisualProfile, BuddyPartId.LeftFoot);
        ConnectorVisualDefinition connectorDefinition =
            lab.Buddy.VisualProfile.Connectors[unequalRadiusConnector];
        Vector3 connectorCenter = visualPresenter.GetConnectorVisual(unequalRadiusConnector).GlobalPosition;
        Vector3 partACenter3D = visualPresenter.GetPartSocket(connectorDefinition.PartA).GlobalPosition;
        Vector3 partBCenter3D = visualPresenter.GetPartSocket(connectorDefinition.PartB).GlobalPosition;
        var partACenter = new Vector2(partACenter3D.X, partACenter3D.Y);
        var partBCenter = new Vector2(partBCenter3D.X, partBCenter3D.Y);
        Vector2 connectorOffset = partBCenter - partACenter;
        float connectorSeparation = connectorOffset.Length();
        float radiusA = VisualRadius(lab, connectorDefinition.PartA);
        float radiusB = VisualRadius(lab, connectorDefinition.PartB);
        float surfaceGap = connectorSeparation - radiusA - radiusB;
        Vector2 expectedConnectorCenter = partACenter +
            connectorOffset / connectorSeparation * (radiusA + surfaceGap * 0.5f);
        float connectorCenterError = expectedConnectorCenter.DistanceTo(
            new Vector2(connectorCenter.X, connectorCenter.Y));
        checks.Add(new StartupCheck("task4_connector_spans_unequal_radius_surface_gap",
            surfaceGap > lab.Buddy.VisualProfile.ConnectorMinimumLength && connectorCenterError < 0.01f,
            $"connector={unequalRadiusConnector} center_error={connectorCenterError:F4}"));

        AcceptedImpact? impact = await ScenarioSteps.StrikePart(tree, lab, lab.Buddy.Rig.Head);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("pain_face_has_priority",
            impact is not null && lab.Reactions.CurrentFace == ">_<",
            $"face={lab.Reactions.CurrentFace}"));
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        checks.Add(new StartupCheck("task4_presenter_face_tracks_semantic_face",
            visualPresenter.FaceLabel.Text == lab.Reactions.CurrentFace,
            $"label={visualPresenter.FaceLabel.Text} semantic={lab.Reactions.CurrentFace}"));
        checks.Add(new StartupCheck("pain_chirp_generated",
            lab.ReactionAudio.GetNode<AudioStreamPlayer>("AudioStreamPlayer").Stream is AudioStreamWav,
            "semantic impact produced original PCM chirp"));
        checks.Add(new StartupCheck("ordinary_glove_hit_has_feedback_without_hit_stop",
            lab.ImpactFeedback.FeedbackCount == 1 && lab.ImpactFeedback.HitStopTriggerCount == 0,
            $"feedback={lab.ImpactFeedback.FeedbackCount} hitStops={lab.ImpactFeedback.HitStopTriggerCount}"));
        checks.Add(new StartupCheck("money_hud_uses_whole_credits",
            lab.MoneyHud.BalanceLabel.Text == "$12",
            $"text={lab.MoneyHud.BalanceLabel.Text}"));

        for (int tick = 0; tick < 40 && !lab.MoneyHud.RewardLabel.Visible; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("reward_feedback_is_coalesced_and_visible",
            lab.MoneyHud.RewardLabel.Visible && lab.MoneyHud.RewardLabel.Text == "+$12.0",
            $"visible={lab.MoneyHud.RewardLabel.Visible} text={lab.MoneyHud.RewardLabel.Text}"));

        var torso = lab.Buddy.Rig.Torso;
        lab.Grab.TryGrab(torso, torso.GlobalPosition);
        lab.Grab.MoveCursor(torso.GlobalPosition + Vector2.Right * 70.0f);
        for (int tick = 0; tick < 3; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("acute_fear_drives_physical_grab_resistance",
            lab.Reactions.CurrentFear > 0.0f && lab.Buddy.GrabResistance.Intent.Active,
            $"fear={lab.Reactions.CurrentFear:F2} active={lab.Buddy.GrabResistance.Intent.Active}"));
        lab.Grab.Release();

        for (int tick = 0; tick < Engine.PhysicsTicksPerSecond + 5; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        lab.Pipeline.SelectTool(ToolId.BoxingGlove);
        lab.Grab.TryGrab(torso, torso.GlobalPosition);
        lab.Grab.MoveCursor(torso.GlobalPosition + Vector2.Right * 70.0f);
        for (int tick = 0; tick < 3; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("harmful_tool_history_drives_physical_grab_resistance",
            lab.Pipeline.IsToolHarmful(ToolId.BoxingGlove) &&
            lab.Reactions.CurrentFear > 0.0f && lab.Buddy.GrabResistance.Intent.Active,
            $"remembered={lab.Pipeline.IsToolHarmful(ToolId.BoxingGlove)} " +
            $"fear={lab.Reactions.CurrentFear:F2} active={lab.Buddy.GrabResistance.Intent.Active}"));
        lab.Grab.Release();

        // Exercise the lifecycle edge directly: a presenter that leaves and re-enters
        // the tree must restore its recovery subscription despite Initialize being
        // intentionally idempotent.
        lab.RemoveChild(visualPresenter);
        lab.AddChild(visualPresenter);
        int recoveriesBefore = lab.Buddy.Recovery.HardRecoveryCount;
        lab.Buddy.Rig.Head.GlobalPosition = new Vector2(-100.0f, -100.0f);
        lab.Buddy.Recovery.PhysicsTick(conscious: true);
        Vector3 expectedRecoveredHead = WorldPlaneMapping.To3D(lab.Buddy.Rig.Head.GlobalPosition);
        expectedRecoveredHead.Z = lab.Buddy.VisualProfile.FindPart(BuddyPartId.Head)!.DepthOffset;
        float recoveredSnapError = visualPresenter.GetPartSocket(BuddyPartId.Head)
            .GlobalPosition.DistanceTo(expectedRecoveredHead);
        checks.Add(new StartupCheck("task4_reentry_restores_hard_recovery_snap",
            lab.Buddy.Recovery.HardRecoveryCount == recoveriesBefore + 1 && recoveredSnapError < 0.01f,
            $"recoveries={lab.Buddy.Recovery.HardRecoveryCount - recoveriesBefore} " +
            $"snap_error={recoveredSnapError:F4}"));

        messages.Add($"face={lab.Reactions.CurrentFace} balance={lab.Pipeline.BalanceMilliCredits}");
        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static int FindConnector(BuddyVisualProfile profile, BuddyPartId endpoint)
    {
        for (int index = 0; index < profile.Connectors.Count; index++)
        {
            ConnectorVisualDefinition connector = profile.Connectors[index];
            if (connector.PartA == endpoint || connector.PartB == endpoint)
            {
                return index;
            }
        }

        throw new System.InvalidOperationException($"No visual connector contains {endpoint}.");
    }

    private static float VisualRadius(BuddyLab lab, BuddyPartId partId)
    {
        PartVisualDefinition definition = lab.Buddy.VisualProfile.FindPart(partId)!;
        return lab.Buddy.Rig.GetPart(partId).Radius * definition.MeshRadiusScale;
    }
}
