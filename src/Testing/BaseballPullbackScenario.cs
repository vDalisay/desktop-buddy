using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M5 Baseball gate: key 5 only spawns/replaces the ball, the normal Grab
/// tether owns pickup, secondary hold activates pullback aiming, and release
/// launches fast enough to score pain and impart visible buddy motion.
/// </summary>
public sealed class BaseballPullbackScenario : IScenario
{
    public string Id => "baseball_pullback";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("baseball_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        Vector2 firstSpawn = new(340.0f, 110.0f);
        await M4ObjectScenarioSupport.MovePointer(tree, lab, firstSpawn, 0);
        await M4ObjectScenarioSupport.SendKey(tree, Key.Key5);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Launcher.HasLaunchable && lab.Objects.Count == 1,
            10);
        LooseObjectBody? first = lab.Launcher.CurrentLaunchable;
        int firstId = first?.RuntimeId ?? 0;
        checks.Add(new StartupCheck(
            "key_5_only_spawns_one_baseball_at_cursor",
            lab.Progress.IsToolUnlocked(ContentIds.ToolBaseball) &&
            lab.Pipeline.SelectedTool == ToolId.Grab &&
            firstId != 0 &&
            first?.SemanticContentId == ContentIds.ToolBaseball &&
            first.GlobalPosition.DistanceTo(firstSpawn) <= 6.0f &&
            !lab.Grab.IsGrabbing &&
            lab.Objects.Count == 1,
            $"owned={lab.Progress.IsToolUnlocked(ContentIds.ToolBaseball)} " +
            $"selected={lab.Pipeline.SelectedTool} id={firstId} " +
            $"position={first?.GlobalPosition} count={lab.Objects.Count}"));

        Vector2 replacementSpawn = new(180.0f, 90.0f);
        await M4ObjectScenarioSupport.MovePointer(tree, lab, replacementSpawn, 0);
        await M4ObjectScenarioSupport.SendKey(tree, Key.Key5);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Launcher.SpawnCount == 2 &&
                  lab.Launcher.CurrentLaunchable != first &&
                  lab.Objects.Count == 1,
            10);
        LooseObjectBody? ball = lab.Launcher.CurrentLaunchable;
        int ballId = ball?.RuntimeId ?? 0;
        checks.Add(new StartupCheck(
            "repeated_key_5_replaces_without_selecting_a_tool",
            first?.RuntimeId == 0 &&
            ballId != 0 &&
            ball?.GlobalPosition.DistanceTo(replacementSpawn) <= 6.0f &&
            lab.Pipeline.SelectedTool == ToolId.Grab &&
            lab.Objects.Count == 1,
            $"first_id={first?.RuntimeId} replacement_id={ballId} " +
            $"selected={lab.Pipeline.SelectedTool} count={lab.Objects.Count}"));

        Vector2 pickPoint = ball?.GlobalPosition ?? replacementSpawn;
        await M4ObjectScenarioSupport.MovePointer(tree, lab, pickPoint, 0);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pickPoint, MouseButton.Left, pressed: true, MouseButtonMask.Left);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Grab.IsGrabbing && lab.Grab.CurrentGrab.Target == ball,
            20);
        bool playerHeld =
            ballId != 0 &&
            lab.Objects.TryGetSnapshot(ballId, out LooseObjectSnapshot heldSnapshot) &&
            heldSnapshot.PlayerHeld;
        checks.Add(new StartupCheck(
            "baseball_pickup_uses_normal_grab_tether",
            lab.Grab.IsGrabbing &&
            lab.Grab.CurrentGrab.Target == ball &&
            playerHeld &&
            !ball!.Freeze &&
            !lab.Launcher.IsAiming,
            $"grabbed={lab.Grab.IsGrabbing} player_held={playerHeld} " +
            $"frozen={ball?.Freeze} aiming={lab.Launcher.IsAiming}"));

        Vector2 nearBuddy = lab.Buddy.Rig.Torso.GlobalPosition +
                            new Vector2(45.0f, -4.0f);
        await M4ObjectScenarioSupport.MovePointer(tree, lab, nearBuddy, MouseButtonMask.Left);
        for (int tick = 0; tick < 45; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool stillPlayerOwned =
            lab.Objects.TryGetSnapshot(ballId, out LooseObjectSnapshot nearSnapshot) &&
            nearSnapshot.PlayerHeld &&
            !nearSnapshot.BuddyHeld;
        checks.Add(new StartupCheck(
            "buddy_cannot_take_item_from_player_grab",
            lab.Grab.IsGrabbing &&
            lab.Grab.CurrentGrab.Target == ball &&
            !lab.Buddy.ObjectInteraction.IsHolding &&
            stillPlayerOwned,
            $"grabbed={lab.Grab.IsGrabbing} buddy_holding={lab.Buddy.ObjectInteraction.IsHolding} " +
            $"player_held={nearSnapshot.PlayerHeld} buddy_held={nearSnapshot.BuddyHeld} " +
            $"guard_rejections={lab.Buddy.ObjectInteraction.PlayerHeldPickupRejectionCount}"));

        await M4ObjectScenarioSupport.SetButton(
            tree,
            lab,
            nearBuddy,
            MouseButton.Right,
            pressed: true,
            MouseButtonMask.Left | MouseButtonMask.Right);
        await M4ObjectScenarioSupport.WaitFor(tree, () => lab.Launcher.IsAiming, 20);
        await M4ObjectScenarioSupport.SetButton(
            tree,
            lab,
            nearBuddy,
            MouseButton.Right,
            pressed: false,
            MouseButtonMask.Left);
        await M4ObjectScenarioSupport.WaitFor(tree, () => !lab.Launcher.IsAiming, 20);
        checks.Add(new StartupCheck(
            "secondary_tap_without_pull_resumes_normal_grab",
            lab.Grab.IsGrabbing &&
            lab.Grab.CurrentGrab.Target == ball &&
            lab.Launcher.CancelCount == 1 &&
            lab.Launcher.LaunchCount == 0,
            $"grabbed={lab.Grab.IsGrabbing} aiming={lab.Launcher.IsAiming} " +
            $"cancels={lab.Launcher.CancelCount} launches={lab.Launcher.LaunchCount}"));

        Rect2 bounds = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        float side = torso.X <= bounds.GetCenter().X ? 1.0f : -1.0f;
        Vector2 aimAnchor = new(
            Mathf.Clamp(
                torso.X + side * 120.0f,
                bounds.Position.X + 125.0f,
                bounds.End.X - 125.0f),
            Mathf.Clamp(
                torso.Y,
                bounds.Position.Y + 40.0f,
                bounds.End.Y - 40.0f));
        await M4ObjectScenarioSupport.MovePointer(tree, lab, aimAnchor, MouseButtonMask.Left);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => GodotObject.IsInstanceValid(ball) &&
                  ball!.GlobalPosition.DistanceTo(aimAnchor) <= 18.0f,
            180);

        Vector2 settledAnchor = ball?.GlobalPosition ?? aimAnchor;
        await M4ObjectScenarioSupport.MovePointer(tree, lab, settledAnchor, MouseButtonMask.Left);
        await M4ObjectScenarioSupport.SetButton(
            tree,
            lab,
            settledAnchor,
            MouseButton.Right,
            pressed: true,
            MouseButtonMask.Left | MouseButtonMask.Right);
        await M4ObjectScenarioSupport.WaitFor(tree, () => lab.Launcher.IsAiming, 20);
        bool protectedAim =
            lab.Objects.TryGetSnapshot(ballId, out LooseObjectSnapshot aimedSnapshot) &&
            aimedSnapshot.PlayerHeld &&
            aimedSnapshot.Protected &&
            ball!.Freeze;

        Vector2 pull = new(
            Mathf.Clamp(
                settledAnchor.X + side * 105.0f,
                bounds.Position.X + ball!.Radius,
                bounds.End.X - ball.Radius),
            settledAnchor.Y);
        await M4ObjectScenarioSupport.MovePointer(
            tree,
            lab,
            pull,
            MouseButtonMask.Left | MouseButtonMask.Right);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => ball.GlobalPosition.DistanceTo(pull) < 0.2f,
            20);
        Vector2 predicted = lab.Launcher.PredictAimedWorldPosition(0.1f);
        checks.Add(new StartupCheck(
            "right_hold_on_grabbed_baseball_activates_trajectory",
            lab.Launcher.IsAiming &&
            lab.Launcher.AimedBody == ball &&
            lab.Grab.IsGrabbing &&
            protectedAim &&
            predicted.DistanceTo(ball.GlobalPosition) > 40.0f,
            $"aiming={lab.Launcher.IsAiming} grab={lab.Grab.IsGrabbing} " +
            $"protected={protectedAim} predicted={predicted}"));

        AcceptedImpact? baseballImpact = null;
        void OnImpact(AcceptedImpact impact)
        {
            if (impact.InteractionId == ball.InteractionId && baseballImpact is null)
                baseballImpact = impact;
        }
        lab.Pipeline.ImpactAccepted += OnImpact;

        Vector2 buddyBefore = CenterOfMass(lab);
        await M4ObjectScenarioSupport.SetButton(
            tree,
            lab,
            pull,
            MouseButton.Right,
            pressed: false,
            MouseButtonMask.Left);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Launcher.LaunchCount == 1 && !lab.Grab.IsGrabbing,
            20);
        await M4ObjectScenarioSupport.SetButton(
            tree,
            lab,
            pull,
            MouseButton.Left,
            pressed: false,
            0);
        float minimumSurfaceGap = float.MaxValue;
        for (int tick = 0; tick < 180 && baseballImpact is null; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            foreach (var part in lab.Buddy.Rig.Parts)
            {
                float gap = ball.GlobalPosition.DistanceTo(part.GlobalPosition) -
                            ball.Radius - part.Radius;
                minimumSurfaceGap = Mathf.Min(minimumSurfaceGap, gap);
            }
        }
        Vector2 velocityAtImpact = lab.Buddy.Rig.Torso.LinearVelocity;
        for (int tick = 0; tick < 24; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        Vector2 buddyAfter = CenterOfMass(lab);
        lab.Pipeline.ImpactAccepted -= OnImpact;

        Vector2 launchVelocity = lab.Launcher.LastLaunchVelocity;
        float pushAlongLaunch = (buddyAfter - buddyBefore).X * Mathf.Sign(launchVelocity.X);
        checks.Add(new StartupCheck(
            "right_release_launches_fast_and_releases_grab",
            !lab.Launcher.IsAiming &&
            !lab.Grab.IsGrabbing &&
            lab.Launcher.LaunchCount == 1 &&
            launchVelocity.Length() >= 1200.0f &&
            Mathf.Sign(launchVelocity.X) == -side &&
            lab.Launcher.LastLaunchedBody == ball &&
            lab.Objects.Count == 1,
            $"velocity={launchVelocity} speed={launchVelocity.Length():F1} " +
            $"grabbed={lab.Grab.IsGrabbing} count={lab.Objects.Count}"));
        checks.Add(new StartupCheck(
            "fast_baseball_damages_and_pushes_buddy",
            baseballImpact is { Pain: > 0.0f, ContentId: ContentIds.ToolBaseball } &&
            (pushAlongLaunch >= 2.0f ||
             velocityAtImpact.X * Mathf.Sign(launchVelocity.X) >= 20.0f),
            $"impact={baseballImpact?.Impulse:F1} pain={baseballImpact?.Pain:F1} " +
            $"push={pushAlongLaunch:F2} torso_velocity={velocityAtImpact} " +
            $"min_gap={minimumSurfaceGap:F2} buddy_holding={lab.Buddy.ObjectInteraction.IsHolding} " +
            $"ball={ball.GlobalPosition} torso={lab.Buddy.Rig.Torso.GlobalPosition}"));

        messages.Add(
            $"spawns={lab.Launcher.SpawnCount} launch_velocity={launchVelocity} " +
            $"impact={baseballImpact?.Impulse:F1} pain={baseballImpact?.Pain:F1} " +
            $"push={pushAlongLaunch:F2} min_gap={minimumSurfaceGap:F2} " +
            $"anchor={settledAnchor} pull={pull} buddy_before={buddyBefore} " +
            $"ball_after={ball.GlobalPosition}");
        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static Vector2 CenterOfMass(BuddyLab lab)
    {
        Vector2 weighted = Vector2.Zero;
        float mass = 0.0f;
        foreach (var part in lab.Buddy.Rig.Parts)
        {
            weighted += part.GlobalPosition * part.Mass;
            mass += part.Mass;
        }
        return mass > 0.0f ? weighted / mass : lab.Buddy.Rig.Torso.GlobalPosition;
    }

}
