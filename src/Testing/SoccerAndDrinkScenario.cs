using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Objects;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M5 Task 8 gate for the Soccer Ball and the Drink — the milestone's two data-driven reuses.
/// Nothing here is new machinery: the ball is a second pullback launchable that rides the
/// foot-only soccer interaction, and the Drink is a second care consumable on the same two-phase
/// consume transaction the Meal uses. What the scenario owns is the proof that the data
/// actually differs where it is supposed to and is shared where it is supposed to be.
///
/// <para>Five things are measured rather than asserted. The Soccer Ball's authored bounce
/// gives it a drop signature the Baseball does not have, and the Baseball's own signature is
/// pinned so the restitution seam can be shown not to have moved anything that authored no
/// bounce. The Drink is placed by its own spawn key like any launchable. The Soccer Ball never
/// enters the buddy's hands. And the Meal and the Drink gate each other not at
/// all: per-content-id cooldown slots plus a hunger fill of zero mean a full buddy still takes
/// a Drink, a cancelled Drink is drinkable again this instant, and a Drink on its 60 s
/// cooldown does not stop the buddy eating.</para>
/// </summary>
public sealed class SoccerAndDrinkScenario : IScenario
{
    /// <summary>Both balls are released this far above their own resting height.</summary>
    private const float DropHeightPx = 240.0f;
    private const int DropTimeoutTicks = 1800;
    private const float DrinkMoodGain = 5.0f;
    private const float MealMoodGain = 10.0f;
    private const float MealHungerFill = 50.0f;
    private const int DrinkCooldownTicks = 7200;
    private const int FetchTimeoutTicks = 2400;
    private const int ConsumeTimeoutTicks = 3000;

    public string Id => "soccer_and_drink";

    private readonly record struct DropSignature(
        bool Measured,
        int Bounces,
        float PeakReboundPx,
        int TicksToRest)
    {
        public override string ToString() =>
            $"measured={Measured} bounces={Bounces} peak={PeakReboundPx:F1} rest={TicksToRest}";
    }

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("soccer_and_drink_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        LooseObjectProfile? baseball = FindLaunchable(lab, ContentIds.ToolBaseball);
        LooseObjectProfile? soccer = FindLaunchable(lab, ContentIds.ToolSoccerBall);
        LooseObjectProfile? drink = FindLaunchable(lab, ContentIds.ToolDrink);
        if (baseball is null || soccer is null || drink is null)
        {
            checks.Add(new StartupCheck(
                "both_new_launchables_are_authored",
                false,
                $"baseball={baseball is not null} soccer={soccer is not null} " +
                $"drink={drink is not null}"));
            await M4ObjectScenarioSupport.Cleanup(tree, lab);
            return new ScenarioResult(false, checks, messages);
        }

        checks.Add(new StartupCheck(
            "both_new_launchables_are_authored",
            lab.Progress.IsToolUnlocked(ContentIds.ToolSoccerBall) &&
            lab.Progress.IsToolUnlocked(ContentIds.ToolDrink) &&
            Mathf.IsEqualApprox(soccer.Bounce, 0.65f) &&
            Mathf.IsZeroApprox(baseball.Bounce) &&
            drink.Consumable &&
            Mathf.IsEqualApprox(drink.ConsumeMoodGain, DrinkMoodGain) &&
            drink.ConsumeCooldownTicks == DrinkCooldownTicks &&
            Mathf.IsZeroApprox(drink.ConsumeHungerFill),
            $"soccer_bounce={soccer.Bounce:F2} baseball_bounce={baseball.Bounce:F2} " +
            $"drink=({drink.ConsumeMoodGain:F1},{drink.ConsumeCooldownTicks}," +
            $"{drink.ConsumeHungerFill:F1})"));

        // --- Phase 1: the two drop signatures, measured on the same fall ---
        DropSignature baseballDrop = await MeasureDrop(tree, lab, baseball);
        DropSignature soccerDrop = await MeasureDrop(tree, lab, soccer);
        messages.Add($"baseball_drop {baseballDrop}");
        messages.Add($"soccer_drop {soccerDrop}");

        // The regression band for the seam itself: a profile that authors no bounce takes no
        // PhysicsMaterial at all, so the Baseball must still land dead. If this ever moves, the
        // restitution seam has started charging profiles that never asked for it.
        checks.Add(new StartupCheck(
            "bounce_zero_objects_did_not_change",
            baseballDrop.Measured &&
            baseballDrop.Bounces <= 1 &&
            baseballDrop.PeakReboundPx <= 8.0f,
            baseballDrop.ToString()));

        // Distinctness is measured, not asserted (plan §2.2): a soccer ball dropped from the
        // same height bounces more times, rebounds far higher, and takes longer to settle.
        checks.Add(new StartupCheck(
            "soccer_signature_differs_from_baseball",
            soccerDrop.Measured && baseballDrop.Measured &&
            soccerDrop.Bounces >= baseballDrop.Bounces + 2 &&
            soccerDrop.PeakReboundPx >= 60.0f &&
            soccerDrop.PeakReboundPx >= baseballDrop.PeakReboundPx + 40.0f &&
            soccerDrop.TicksToRest > baseballDrop.TicksToRest,
            $"soccer=({soccerDrop}) baseball=({baseballDrop})"));

        // --- Phase 2: the spawn key, the shared chord, and rest ---
        checks.Add(await CheckSoccerSpawnsLaunchesAndRests(tree, lab, messages));

        // --- Phase 3: ordinary ball play is foot-only ---
        checks.Add(await CheckSoccerIsFootOnly(tree, lab, soccer, messages));

        // --- Phase 3a: a player-held ball produces a receive-pass stance ---
        checks.Add(await CheckSoccerReceiveStance(tree, lab, soccer, messages));

        // --- Phase 3b: a cornered ball gets the explicit hand-rescue exception ---
        checks.Add(await CheckCornerRescue(tree, lab, soccer, messages));

        // --- Phase 3c: football alone is not ambient-hop obstacle evidence ---
        checks.Add(await CheckFootballDoesNotRequestObstacleHop(
            tree, lab, soccer, baseball, messages));

        // --- Phase 3d: player touch survives ground, but not walls/ceiling ---
        checks.AddRange(await CheckSoccerTrapPermission(tree, lab, soccer, messages));

        // --- Phase 3e: the trap, the beat, and the kick back ---
        checks.AddRange(await CheckSoccerTrapAndKick(tree, lab, soccer, messages));

        // --- Phase 3f: both items are drawn once, in whichever mode is active ---
        checks.Add(await CheckDrawnSilhouettes(tree, lab, soccer, drink, messages));

        // --- Phase 4 and 5: the Drink's own spawn key and the care rules ---
        checks.AddRange(await CheckDrinkCare(tree, lab, messages));

        messages.Add(
            $"successes={lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"cancels={lab.Buddy.ObjectInteraction.ConsumeCancelCount} " +
            $"refusals={lab.Buddy.ObjectInteraction.RefusalCount} " +
            $"mood={lab.Progress.Mood:F1} fullness={lab.Progress.Fullness:F1}");
        await M4ObjectScenarioSupport.Cleanup(tree, lab);

        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// Drops one profile from <see cref="DropHeightPx"/> above its own resting height and
    /// records how it settles: how many times it comes back off the floor, how high the
    /// highest rebound goes, and how many routed ticks pass before the registry calls it
    /// at rest.
    ///
    /// <para>The buddy is not part of this measurement, so the ball enters through the
    /// registry's own ignore channel — the same one a buddy-released object uses to stop the
    /// buddy re-committing to it. Without that a fetched ball would end the fall early and the
    /// signature would depend on where the buddy happened to be pacing.</para>
    /// </summary>
    private static async Task<DropSignature> MeasureDrop(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectProfile profile)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        float restY = room.End.Y - profile.Radius;
        float torsoX = lab.Buddy.Rig.Torso.GlobalPosition.X;
        // The far side of the room, so nothing the buddy does can nudge the fall.
        float x = torsoX - room.Position.X > room.End.X - torsoX
            ? room.Position.X + profile.Radius + 4.0f
            : room.End.X - profile.Radius - 4.0f;

        LooseObjectBody? body = lab.SpawnLooseObject(
            profile, new Vector2(x, restY - DropHeightPx));
        if (body is null)
            return new DropSignature(false, 0, 0.0f, 0);

        lab.Objects.MarkBuddyReleased(body, ignoreTicks: DropTimeoutTicks + 60);

        int bounces = 0;
        float peak = 0.0f;
        int ticksToRest = DropTimeoutTicks;
        float previousVelocityY = 0.0f;
        bool landedOnce = false;
        for (int tick = 0; tick < DropTimeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (!GodotObject.IsInstanceValid(body))
                break;

            float velocityY = body.LinearVelocity.Y;
            // A bounce is the frame the fall turns into a climb. Thresholded on both sides so
            // solver jitter against the floor cannot be counted as a rebound.
            if (previousVelocityY > 30.0f && velocityY < -30.0f)
            {
                bounces++;
                landedOnce = true;
            }

            if (landedOnce)
                peak = Mathf.Max(peak, restY - body.GlobalPosition.Y);
            previousVelocityY = velocityY;

            if (lab.Objects.TryGetSnapshot(body.RuntimeId, out LooseObjectSnapshot snapshot) &&
                snapshot.AtRest)
            {
                ticksToRest = tick + 1;
                break;
            }
        }

        RemoveObject(lab, body);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        return new DropSignature(true, bounces, peak, ticksToRest);
    }

    /// <summary>
    /// Key <c>8</c> places one owned Soccer Ball, the ordinary Grab tether picks it up, the
    /// shared pullback chord launches it with the ball's <b>own</b> authored tuning, and it
    /// settles in the room afterwards.
    /// </summary>
    private static async Task<StartupCheck> CheckSoccerSpawnsLaunchesAndRests(
        SceneTree tree,
        BuddyLab lab,
        List<string> messages)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        float side = torso.X <= room.GetCenter().X ? 1.0f : -1.0f;
        Vector2 anchor = new(
            Mathf.Clamp(torso.X + (side * 130.0f), room.Position.X + 130.0f, room.End.X - 130.0f),
            Mathf.Clamp(torso.Y, room.Position.Y + 40.0f, room.End.Y - 40.0f));

        await M4ObjectScenarioSupport.MovePointer(tree, lab, anchor, 0);
        await M4ObjectScenarioSupport.SendKey(tree, Key.Key8);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Launcher.CurrentLaunchableContentId == ContentIds.ToolSoccerBall &&
                  lab.Objects.Count == 1,
            30);
        LooseObjectBody? ball = lab.Launcher.CurrentLaunchable;
        bool placed =
            GodotObject.IsInstanceValid(ball) &&
            ball!.SemanticContentId == ContentIds.ToolSoccerBall &&
            ball.RuntimeId != 0 &&
            lab.Objects.Count == 1 &&
            lab.Pipeline.SelectedTool == ToolId.Grab &&
            !lab.Grab.IsGrabbing;
        if (!placed)
        {
            return new StartupCheck(
                "soccer_spawns_launches_and_rests",
                false,
                $"placed=false count={lab.Objects.Count} " +
                $"content={lab.Launcher.CurrentLaunchableContentId}");
        }

        // Let it drop out of the air before reaching for it: the key places it at the pointer.
        for (int tick = 0; tick < 120; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        Vector2 pick = ball!.GlobalPosition;
        await M4ObjectScenarioSupport.MovePointer(tree, lab, pick, 0);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pick, MouseButton.Left, pressed: true, MouseButtonMask.Left);
        bool grabbed = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Grab.IsGrabbing && lab.Grab.CurrentGrab.Target == ball, 60);

        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pick, MouseButton.Right, pressed: true,
            MouseButtonMask.Left | MouseButtonMask.Right);
        bool aiming = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Launcher.IsAiming, 60);
        // The ball's own preset, not the launcher's shared one: this is the authored seam the
        // plan asks for, and it is only observable while an aim is live.
        PullbackLauncherProfile tuning = lab.Launcher.AimTuning;
        bool usedItsOwnTuning = aiming && tuning != lab.Launcher.Profile;

        // Pull toward the buddy so the launch sends the ball away from it and the settle that
        // follows is a settle, not a catch.
        Vector2 pull = new(
            Mathf.Clamp(pick.X - (side * 90.0f), room.Position.X + ball.Radius, room.End.X - ball.Radius),
            pick.Y);
        await M4ObjectScenarioSupport.MovePointer(
            tree, lab, pull, MouseButtonMask.Left | MouseButtonMask.Right);
        for (int tick = 0; tick < 20; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pull, MouseButton.Right, pressed: false, MouseButtonMask.Left);
        bool launched = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Launcher.LaunchCount >= 1, 60);
        Vector2 launchVelocity = lab.Launcher.LastLaunchVelocity;
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pull, MouseButton.Left, pressed: false, 0);

        // Rest is the property under test, so the buddy is kept out of it exactly as the drop
        // measurement does.
        bool rested = false;
        if (GodotObject.IsInstanceValid(ball))
        {
            lab.Objects.MarkBuddyReleased(ball, ignoreTicks: DropTimeoutTicks + 60);
            rested = await M4ObjectScenarioSupport.WaitFor(
                tree,
                () => GodotObject.IsInstanceValid(ball) &&
                      lab.Objects.TryGetSnapshot(ball.RuntimeId, out LooseObjectSnapshot rest) &&
                      rest.AtRest,
                DropTimeoutTicks);
        }

        string detail =
            $"placed={placed} grabbed={grabbed} aiming={aiming} own_tuning={usedItsOwnTuning} " +
            $"launched={launched} speed={launchVelocity.Length():F1} " +
            $"velocity_per_pull={tuning.VelocityPerPullPixel:F1} rested={rested} " +
            $"count={lab.Objects.Count}";
        messages.Add($"soccer_launch {detail}");
        if (GodotObject.IsInstanceValid(ball))
            RemoveObject(lab, ball!);

        return new StartupCheck(
            "soccer_spawns_launches_and_rests",
            placed && grabbed && aiming && usedItsOwnTuning && launched &&
            launchVelocity.Length() > 100.0f && rested,
            detail);
    }

    /// <summary>
    /// A resting Soccer Ball in ordinary pickup range is ignored by the hand lifecycle. This
    /// is content behavior, not a timing accident: it remains untracked for the full window.
    /// </summary>
    private static async Task<StartupCheck> CheckSoccerIsFootOnly(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectProfile soccer,
        List<string> messages)
    {
        lab.Progress.ApplyCareMood(30.0f);
        await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.ObjectInteraction.Phase == ObjectPhase.Idle, 600);

        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        float side = torso.X <= room.GetCenter().X ? 1.0f : -1.0f;
        float wallMargin = soccer.SoccerPlay!.WallTurnDistance + soccer.Radius + 2.0f;
        var spawn = new Vector2(
            Mathf.Clamp(torso.X + side * 120.0f,
                room.Position.X + wallMargin,
                room.End.X - wallMargin),
            room.End.Y - soccer.Radius - 1.0f);
        LooseObjectBody? ball = lab.SpawnLooseObject(soccer, spawn, Vector2.Zero);
        string? freeBallVisual = null;
        if (ball is not null && DisplayServer.GetName() != "headless")
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            CanvasLayer ui = lab.GetNode<CanvasLayer>("LabUi");
            bool uiVisible = ui.Visible;
            bool boundsVisible = lab.BoundaryVisualizer.Visible;
            ui.Visible = false;
            lab.BoundaryVisualizer.Visible = false;
            for (int frame = 0; frame < 3; frame++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string directory = Path.GetFullPath(
                ScenarioArtifacts.Directory ?? ".artifacts/soccer_visual_check");
            Directory.CreateDirectory(directory);
            freeBallVisual = Path.Combine(directory, "soccer_free_ball.png");
            if (tree.Root.GetTexture().GetImage().SavePng(freeBallVisual) != Error.Ok)
                freeBallVisual = null;
            ui.Visible = uiVisible;
            lab.BoundaryVisualizer.Visible = boundsVisible;
        }
        bool everTracked = false;
        bool everHeld = false;
        bool chased = false;
        int kicksBefore = lab.Buddy.ObjectInteraction.SoccerKickCount;
        float initialGap = ball is null ? float.MaxValue :
            Mathf.Abs(ball.GlobalPosition.X - torso.X) - ball.Radius;
        float minimumGap = initialGap;
        for (int tick = 0; tick < 600 && GodotObject.IsInstanceValid(ball) &&
            lab.Buddy.ObjectInteraction.SoccerKickCount == kicksBefore; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            everTracked |= lab.Buddy.ObjectInteraction.TrackedRuntimeId == ball!.RuntimeId;
            everHeld |= lab.Buddy.ObjectInteraction.IsHolding;
            chased |= lab.Buddy.ObjectInteraction.SoccerCommand == SoccerPlayCommand.Approach;
            minimumGap = Mathf.Min(
                minimumGap,
                Mathf.Abs(ball.GlobalPosition.X - lab.Buddy.Rig.Torso.GlobalPosition.X) - ball.Radius);
        }

        bool kicked = lab.Buddy.ObjectInteraction.SoccerKickCount > kicksBefore;
        string detail =
            $"spawned={ball is not null} tracked={everTracked} held={everHeld} " +
            $"chased={chased} kicked={kicked} gap={initialGap:F1}->{minimumGap:F1} " +
            $"style={lab.Buddy.ObjectInteraction.LastSoccerKickStyle} " +
            $"visual={freeBallVisual ?? "headless"}";
        messages.Add($"soccer_foot_only {detail}");

        if (ball is not null)
            RemoveObject(lab, ball);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        return new StartupCheck(
            "a_good_mood_buddy_chases_and_kicks_without_pickup",
            ball is not null && !everTracked && !everHeld && chased && kicked &&
            minimumGap < initialGap - 20.0f,
            detail);
    }

    private static async Task<StartupCheck> CheckSoccerReceiveStance(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectProfile soccer,
        List<string> messages)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        float side = torso.X <= room.GetCenter().X ? 1.0f : -1.0f;
        var spawn = new Vector2(
            Mathf.Clamp(torso.X + side * 75.0f,
                room.Position.X + soccer.Radius + 2.0f,
                room.End.X - soccer.Radius - 2.0f),
            room.End.Y - soccer.Radius - 1.0f);
        LooseObjectBody? ball = lab.SpawnLooseObject(soccer, spawn, Vector2.Zero);
        bool grabbed = ball is not null && lab.Grab.TryGrab(ball, ball.GlobalPosition);
        float initialGap = ball is null ? 0.0f :
            Mathf.Abs(ball.GlobalPosition.X - torso.X) - ball.Radius;
        bool received = false;
        bool watched = false;
        bool lookedAtBall = false;
        bool renderedHeadTracked = false;
        bool renderedEyesTracked = false;
        string? visualEvidence = null;
        bool paused = false;
        bool resumed = false;
        int travellingTicks = 0;
        bool continuouslyWatchedWhileTravelling = true;
        bool continuouslyRenderedWhileTravelling = true;
        string firstTravelGazeFailure = "none";
        float maximumGap = initialGap;
        for (int tick = 0; tick < 760 && grabbed && GodotObject.IsInstanceValid(ball); tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            received |= lab.Buddy.ObjectInteraction.SoccerCommand == SoccerPlayCommand.Receive &&
                Mathf.Sign(lab.Buddy.ObjectInteraction.ApproachDirection) == -side;
            watched |= lab.Buddy.ObjectInteraction.HasWatchTarget;
            lookedAtBall |= lab.HeadLookAt.CurrentSource == LookAtSource.Item;
            float ballDirection = Mathf.Sign(ball!.GlobalPosition.X - lab.Buddy.Rig.Head.GlobalPosition.X);
            renderedHeadTracked |= Mathf.Abs(lab.VisualPresenter.AppliedHeadYawDegrees) > 1.0f &&
                Mathf.Sign(lab.VisualPresenter.AppliedHeadYawDegrees) == ballDirection;
            FaceRenderState face = lab.Face.LastComposedState;
            renderedEyesTracked |= face.Eyes is FaceEyePose.Open or FaceEyePose.Narrow or FaceEyePose.Wide &&
                Mathf.Abs(face.PupilX) > 0.0f && Mathf.Sign(face.PupilX) == ballDirection;
            if (lab.Buddy.ObjectInteraction.SoccerCommand == SoccerPlayCommand.Receive &&
                !Mathf.IsZeroApprox(lab.Buddy.ObjectInteraction.ApproachDirection))
            {
                travellingTicks++;
                if (travellingTicks > 30)
                {
                    continuouslyWatchedWhileTravelling &=
                        lab.Buddy.ObjectInteraction.HasWatchTarget &&
                        lab.HeadLookAt.CurrentSource == LookAtSource.Item;
                    bool eyesOpen = face.Eyes is
                        FaceEyePose.Open or FaceEyePose.Narrow or FaceEyePose.Wide;
                    bool renderedThisTick =
                        Mathf.Abs(lab.VisualPresenter.AppliedHeadYawDegrees) > 1.0f &&
                        Mathf.Sign(lab.VisualPresenter.AppliedHeadYawDegrees) == ballDirection &&
                        (!eyesOpen || face.Blinking || (Mathf.Abs(face.PupilX) > 0.0f &&
                            Mathf.Sign(face.PupilX) == ballDirection));
                    if (!renderedThisTick && firstTravelGazeFailure == "none")
                    {
                        firstTravelGazeFailure = $"tick={tick} travel={travellingTicks} " +
                            $"dir={ballDirection:F0} yaw={lab.VisualPresenter.AppliedHeadYawDegrees:F2} " +
                            $"eyes={face.Eyes} pupil={face.PupilX:F2} source={lab.HeadLookAt.CurrentSource}";
                    }
                    continuouslyRenderedWhileTravelling &= renderedThisTick;
                }
            }
            if (renderedEyesTracked && visualEvidence is null &&
                DisplayServer.GetName() != "headless")
            {
                CanvasLayer ui = lab.GetNode<CanvasLayer>("LabUi");
                bool uiVisible = ui.Visible;
                bool boundsVisible = lab.BoundaryVisualizer.Visible;
                ui.Visible = false;
                lab.BoundaryVisualizer.Visible = false;
                for (int frame = 0; frame < 3; frame++)
                    await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                await tree.ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw);
                string directory = Path.GetFullPath(
                    ScenarioArtifacts.Directory ?? ".artifacts/soccer_visual_check");
                Directory.CreateDirectory(directory);
                visualEvidence = Path.Combine(directory, "soccer_receive_tracking.png");
                if (tree.Root.GetTexture().GetImage().SavePng(visualEvidence) != Error.Ok)
                    visualEvidence = null;
                ui.Visible = uiVisible;
                lab.BoundaryVisualizer.Visible = boundsVisible;
            }
            paused |= tick is >= 600 and < 720 &&
                lab.Buddy.ObjectInteraction.SoccerCommand == SoccerPlayCommand.Receive &&
                Mathf.IsZeroApprox(lab.Buddy.ObjectInteraction.ApproachDirection);
            resumed |= tick >= 720 &&
                Mathf.Sign(lab.Buddy.ObjectInteraction.ApproachDirection) == -side;
            maximumGap = Mathf.Max(
                maximumGap,
                Mathf.Abs(ball!.GlobalPosition.X - lab.Buddy.Rig.Torso.GlobalPosition.X) - ball.Radius);
        }

        bool playerStillOwns = lab.Grab.IsGrabbing &&
            lab.Grab.CurrentGrab.Target == ball &&
            !lab.Buddy.ObjectInteraction.IsHolding;
        string detail =
            $"grabbed={grabbed} receive={received} watched={watched} semantic_look={lookedAtBall} " +
            $"head={renderedHeadTracked} eyes={renderedEyesTracked} " +
            $"continuous=({continuouslyWatchedWhileTravelling}," +
            $"{continuouslyRenderedWhileTravelling}) travel_ticks={travellingTicks} " +
            $"first_failure=({firstTravelGazeFailure}) " +
            $"paused={paused} resumed={resumed} " +
            $"player_owns={playerStillOwns} gap={initialGap:F1}->{maximumGap:F1} " +
            $"visual={visualEvidence ?? "headless"}";
        messages.Add($"soccer_receive {detail}");

        if (lab.Grab.IsGrabbing)
            lab.Grab.Release(countsAsThrow: false);
        bool chasedAfterRelease = false;
        for (int tick = 0; tick < 10 && GodotObject.IsInstanceValid(ball); tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            chasedAfterRelease |= lab.Buddy.ObjectInteraction.SoccerCommand == SoccerPlayCommand.Approach;
        }
        if (ball is not null)
            RemoveObject(lab, ball);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        return new StartupCheck(
            "a_player_held_ball_makes_the_buddy_take_receive_spacing",
            grabbed && received && watched && lookedAtBall && renderedHeadTracked &&
            renderedEyesTracked && travellingTicks > 30 &&
            continuouslyWatchedWhileTravelling && continuouslyRenderedWhileTravelling &&
            paused && resumed &&
            chasedAfterRelease && playerStillOwns && maximumGap > initialGap + 20.0f,
            $"{detail} release_chase={chasedAfterRelease}");
    }

    private static async Task<StartupCheck> CheckCornerRescue(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectProfile soccer,
        List<string> messages)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        lab.Progress.ApplyCareMood(30.0f);
        var spawn = new Vector2(
            room.Position.X + soccer.Radius + 1.0f,
            room.End.Y - soccer.Radius - 1.0f);
        LooseObjectBody? ball = lab.SpawnLooseObject(soccer, spawn, Vector2.Zero);
        int kicksBefore = lab.Buddy.ObjectInteraction.SoccerKickCount;
        bool pickedUp = false;
        bool carriedInward = false;
        bool watchedWhileCarried = true;
        bool renderedWhileCarried = true;
        int carriedTicks = 0;
        bool droppedInFront = false;
        bool kickedInward = false;

        for (int tick = 0; tick < 1800 && GodotObject.IsInstanceValid(ball) && !kickedInward; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            bool carrying = lab.Buddy.ObjectInteraction.SoccerPhase == SoccerPlayPhase.CornerCarry;
            pickedUp |= carrying && lab.Buddy.ObjectInteraction.IsHolding;
            carriedInward |= carrying && lab.Buddy.ObjectInteraction.ApproachDirection > 0.0f;
            if (carrying && lab.Buddy.ObjectInteraction.IsHolding)
            {
                carriedTicks++;
                watchedWhileCarried &= lab.Buddy.ObjectInteraction.HasWatchTarget &&
                    lab.HeadLookAt.CurrentSource == LookAtSource.Item;
                if (carriedTicks > 30)
                {
                    float direction = Mathf.Sign(
                        ball!.GlobalPosition.X - lab.Buddy.Rig.Head.GlobalPosition.X);
                    FaceRenderState face = lab.Face.LastComposedState;
                    bool eyesOpen = face.Eyes is
                        FaceEyePose.Open or FaceEyePose.Narrow or FaceEyePose.Wide;
                    renderedWhileCarried &=
                        Mathf.Abs(lab.VisualPresenter.AppliedHeadYawDegrees) > 1.0f &&
                        Mathf.Sign(lab.VisualPresenter.AppliedHeadYawDegrees) == direction &&
                        (!eyesOpen || face.Blinking || (Mathf.Abs(face.PupilX) > 0.0f &&
                            Mathf.Sign(face.PupilX) == direction));
                }
            }

            droppedInFront |= lab.Buddy.ObjectInteraction.SoccerPhase == SoccerPlayPhase.CornerDrop &&
                !lab.Buddy.ObjectInteraction.IsHolding &&
                ball!.GlobalPosition.X > lab.Buddy.Rig.Torso.GlobalPosition.X;
            kickedInward = lab.Buddy.ObjectInteraction.SoccerKickCount == kicksBefore + 1 &&
                lab.Buddy.ObjectInteraction.LastSoccerKickStyle ==
                    SoccerKickStyle.TurnAwayFromWall &&
                lab.Buddy.ObjectInteraction.LastSoccerKickVelocity.X > 0.0f;
        }

        string detail = $"pickup={pickedUp} carry={carriedInward} carry_ticks={carriedTicks} " +
            $"watch={watchedWhileCarried} rendered={renderedWhileCarried} " +
            $"drop={droppedInFront} kick={kickedInward}";
        messages.Add($"soccer_corner_rescue {detail}");
        if (ball is not null)
            RemoveObject(lab, ball);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        return new StartupCheck(
            "a_cornered_ball_is_carried_inward_dropped_in_front_and_kicked",
            ball is not null && pickedUp && carriedInward && carriedTicks > 30 &&
            watchedWhileCarried && renderedWhileCarried && droppedInFront && kickedInward,
            detail);
    }

    private static async Task<StartupCheck> CheckFootballDoesNotRequestObstacleHop(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectProfile soccer,
        LooseObjectProfile baseball,
        List<string> messages)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        float side = torso.X <= room.GetCenter().X ? 1.0f : -1.0f;
        Vector2 probe = new(torso.X + side * 36.0f, room.End.Y - soccer.Radius - 1.0f);

        LooseObjectBody? football = lab.SpawnLooseObject(soccer, probe, Vector2.Zero);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool footballRay = lab.Buddy.AutonomousMotion.ObstacleInCommittedPath(side);
        bool footballRegistry = lab.Buddy.ObjectInteraction.RestingObstacleInPath(side);
        if (football is not null)
            RemoveObject(lab, football);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        probe.Y = room.End.Y - baseball.Radius - 1.0f;
        LooseObjectBody? ordinary = lab.SpawnLooseObject(baseball, probe, Vector2.Zero);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool ordinaryRay = lab.Buddy.AutonomousMotion.ObstacleInCommittedPath(side);
        bool ordinaryRegistry = lab.Buddy.ObjectInteraction.RestingObstacleInPath(side);
        if (ordinary is not null)
            RemoveObject(lab, ordinary);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        string detail = $"football=({footballRay},{footballRegistry}) " +
            $"baseball=({ordinaryRay},{ordinaryRegistry})";
        messages.Add($"soccer_obstacle_hop {detail}");
        return new StartupCheck(
            "football_does_not_request_object_hops_but_other_objects_do",
            football is not null && ordinary is not null && !footballRay && !footballRegistry &&
            ordinaryRay,
            detail);
    }

    private static async Task<List<StartupCheck>> CheckSoccerTrapPermission(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectProfile soccer,
        List<string> messages)
    {
        var checks = new List<StartupCheck>();
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        var floor = new Vector2(room.GetCenter().X, room.End.Y - soccer.Radius - 1.0f);
        LooseObjectBody? ball = lab.SpawnLooseObject(soccer, floor, Vector2.Zero);
        if (ball is null)
        {
            checks.Add(new StartupCheck("player_touch_survives_ground_contact", false, "spawn refused"));
            return checks;
        }

        lab.Objects.MarkPlayerThrown(ball, ContentIds.ToolSoccerBall);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool groundPreserved = lab.Objects.TryGetSnapshot(ball.RuntimeId, out LooseObjectSnapshot ground) &&
            ground.SoccerTrapAllowed;

        ball.GlobalPosition = new Vector2(
            room.Position.X + soccer.Radius,
            room.GetCenter().Y);
        ball.LinearVelocity = Vector2.Zero;
        ball.ResetPhysicsInterpolation();
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool wallCleared = lab.Objects.TryGetSnapshot(ball.RuntimeId, out LooseObjectSnapshot wall) &&
            !wall.SoccerTrapAllowed;

        lab.Objects.MarkPlayerThrown(ball, ContentIds.ToolSoccerBall);
        ball.GlobalPosition = new Vector2(room.GetCenter().X, room.Position.Y + soccer.Radius);
        ball.ResetPhysicsInterpolation();
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool ceilingCleared = lab.Objects.TryGetSnapshot(ball.RuntimeId, out LooseObjectSnapshot ceiling) &&
            !ceiling.SoccerTrapAllowed;

        checks.Add(new StartupCheck(
            "player_touch_survives_ground_contact",
            groundPreserved,
            $"ground_allowed={ground.SoccerTrapAllowed}"));
        checks.Add(new StartupCheck(
            "walls_and_ceiling_disable_trapping_until_the_next_player_touch",
            wallCleared && ceilingCleared,
            $"wall_allowed={wall.SoccerTrapAllowed} ceiling_allowed={ceiling.SoccerTrapAllowed}"));

        SoccerPlayProfile play = soccer.SoccerPlay!;
        float side = torso.X <= room.GetCenter().X ? 1.0f : -1.0f;
        ball.GlobalPosition = new Vector2(
            Mathf.Clamp(torso.X + side * 120.0f,
                room.Position.X + soccer.Radius + 2.0f,
                room.End.X - soccer.Radius - 2.0f),
            room.End.Y - soccer.Radius - 1.0f);
        ball.LinearVelocity = new Vector2(
            -side * (play.MinimumApproachSpeed + play.MaximumApproachSpeed) * 0.25f, 0.0f);
        ball.AngularVelocity = 0.0f;
        ball.Sleeping = false;
        ball.ResetPhysicsInterpolation();

        int trapsBefore = lab.Buddy.ObjectInteraction.SoccerTrapCount;
        int kicksBefore = lab.Buddy.ObjectInteraction.SoccerKickCount;
        bool kicked = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.SoccerKickCount == kicksBefore + 1,
            900);
        bool directKick = kicked &&
            lab.Buddy.ObjectInteraction.SoccerTrapCount == trapsBefore &&
            !lab.Buddy.ObjectInteraction.IsHolding;
        checks.Add(new StartupCheck(
            "a_ball_that_cannot_be_trapped_can_still_be_kicked",
            directKick,
            $"kicked={kicked} traps={trapsBefore}->{lab.Buddy.ObjectInteraction.SoccerTrapCount} " +
            $"kicks={kicksBefore}->{lab.Buddy.ObjectInteraction.SoccerKickCount} " +
            $"holding={lab.Buddy.ObjectInteraction.IsHolding}"));

        messages.Add(
            $"soccer_permission ground={groundPreserved} wall={wallCleared} " +
            $"ceiling={ceilingCleared} direct_kick={directKick}");
        RemoveObject(lab, ball);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        return checks;
    }

    /// <summary>
    /// Both new items carry a real model in the Mii3D presentation and degrade to their flat
    /// circle in legacy — exactly one silhouette per mode, never both and never neither, which
    /// is the rule the grenade's own presentation check established.
    ///
    /// <para>The verdict is identical in both modes on purpose (ARCHITECTURE §16): what is
    /// asserted is that the drawn set and the flat set are complements, not which one is on.
    /// The mesh envelope is pure geometry and holds either way.</para>
    /// </summary>
    private static async Task<StartupCheck> CheckDrawnSilhouettes(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectProfile soccer,
        LooseObjectProfile drink,
        List<string> messages)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        float side = torso.X - room.Position.X > room.End.X - torso.X ? -1.0f : 1.0f;
        var ballAt = new Vector2(
            Mathf.Clamp(torso.X + (side * 170.0f),
                room.Position.X + 40.0f, room.End.X - 40.0f),
            room.End.Y - soccer.Radius - 1.0f);
        var canAt = new Vector2(
            Mathf.Clamp(torso.X + (side * 210.0f),
                room.Position.X + 40.0f, room.End.X - 40.0f),
            room.End.Y - drink.Radius - 1.0f);

        LooseObjectBody? ball = lab.SpawnLooseObject(soccer, ballAt);
        LooseObjectBody? can = lab.SpawnLooseObject(drink, canAt);
        if (ball is null || can is null)
        {
            return new StartupCheck(
                "the_new_items_are_drawn_once_in_the_active_presentation", false, "spawn refused");
        }

        // Both are kept out of the buddy's way; this leg is about drawing, not behaviour.
        lab.Objects.MarkBuddyReleased(ball, ignoreTicks: 600);
        lab.Objects.MarkBuddyReleased(can, ignoreTicks: 600);
        for (int tick = 0; tick < 12; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        LooseObjectVisual3D presenter = lab.LooseObjectVisual;
        bool adopted = presenter.IsDrawing(ball.RuntimeId) && presenter.IsDrawing(can.RuntimeId);
        // Exactly one silhouette each: the mesh on and the flat body off, or the reverse.
        bool ballOnce = presenter.MeshVisible(ball.RuntimeId) != ball.Visible;
        bool canOnce = presenter.MeshVisible(can.RuntimeId) != can.Visible;

        bool ballInsideEnvelope = MeshFitsEnvelope(
            presenter.MeshFor(ball.RuntimeId), soccer.Radius, out float ballReach);
        bool canInsideEnvelope = MeshFitsEnvelope(
            presenter.MeshFor(can.RuntimeId), drink.Radius, out float canReach);

        // Nothing that authored no shape may be adopted: the Baseball, Meal, and Grenade keep
        // the flat circle they have always had.
        bool onlyTheNewOnes = presenter.DrawnCount == 2;

        string detail =
            $"mode={lab.Mode} adopted={adopted} drawn={presenter.DrawnCount} " +
            $"ball_once={ballOnce} can_once={canOnce} " +
            $"ball_reach={ballReach:F1}/{LooseObjectMeshBuilder.EnvelopeRadius(soccer.Radius):F1} " +
            $"can_reach={canReach:F1}/{LooseObjectMeshBuilder.EnvelopeRadius(drink.Radius):F1} " +
            $"meshes_built={presenter.BuiltMeshCount}";
        messages.Add($"soccer_drink_visuals {detail}");

        RemoveObject(lab, ball);
        RemoveObject(lab, can);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        return new StartupCheck(
            "the_new_items_are_drawn_once_in_the_active_presentation",
            adopted && ballOnce && canOnce && onlyTheNewOnes &&
            ballInsideEnvelope && canInsideEnvelope,
            detail);
    }

    /// <summary>No vertex may escape the builder's stated envelope for its collider radius.</summary>
    private static bool MeshFitsEnvelope(Mesh? mesh, float radius, out float reach)
    {
        reach = 0.0f;
        if (mesh is null)
            return false;

        Godot.Collections.Array surface = mesh.SurfaceGetArrays(0);
        var vertices = surface[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        foreach (Vector3 vertex in vertices)
            reach = Mathf.Max(reach, vertex.Length());

        return reach <= LooseObjectMeshBuilder.EnvelopeRadius(radius) + 0.01f;
    }

    /// <summary>
    /// The owner's soccer loop (2026-08-01): a ball rolled along the floor at the buddy is
    /// stopped dead under its foot, sat on for the authored dwell, and then kicked back the way
    /// it came at either a dead-straight or a slightly lofted angle.
    ///
    /// <para>The ball is rolled through the real registry with a real velocity — no component
    /// is commanded directly — and every claim is measured off runtime state: the ball's speed
    /// at the moment of the trap, the routed ticks it stays stopped, and which way it is
    /// travelling once the foot has gone through it.</para>
    /// </summary>
    private static async Task<List<StartupCheck>> CheckSoccerTrapAndKick(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectProfile soccer,
        List<string> messages)
    {
        var checks = new List<StartupCheck>();
        SoccerPlayProfile? play = soccer.SoccerPlay;
        if (play is null || !GodotObject.IsInstanceValid(play))
        {
            checks.Add(new StartupCheck(
                "the_soccer_ball_opts_into_being_played_with", false, "no SoccerPlay profile"));
            return checks;
        }

        // Only the Soccer Ball. If this ever fails, some other object has quietly been given
        // the beat, which is exactly what the owner's "no other loose object changes" means.
        LooseObjectProfile?[] others =
        [
            FindLaunchable(lab, ContentIds.ToolBaseball),
            FindLaunchable(lab, ContentIds.ToolMeal),
            FindLaunchable(lab, ContentIds.ToolGrenade),
            FindLaunchable(lab, ContentIds.ToolDrink),
            lab.SafeObjectProfile,
            lab.LabFoodProfile,
        ];
        bool nobodyElseOptedIn = true;
        foreach (LooseObjectProfile? other in others)
        {
            if (GodotObject.IsInstanceValid(other) && other!.SoccerPlay is not null)
                nobodyElseOptedIn = false;
        }

        checks.Add(new StartupCheck(
            "the_soccer_ball_opts_into_being_played_with",
            play.IsRuntimeValid && nobodyElseOptedIn,
            $"valid={play.IsRuntimeValid} dwell={play.DwellTicks} kick={play.KickSpeed:F0} " +
            $"loft_max={play.MaximumKickLoftDegrees:F0} choices={play.KickLoftChoices} " +
            $"nobody_else={nobodyElseOptedIn}"));

        await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.ObjectInteraction.Phase == ObjectPhase.Idle, 600);

        int trapsBefore = lab.Buddy.ObjectInteraction.SoccerTrapCount;
        int kicksBefore = lab.Buddy.ObjectInteraction.SoccerKickCount;
        LooseObjectBody? ball = RollBallAtBuddy(lab, soccer, play);
        if (ball is null)
        {
            checks.Add(new StartupCheck(
                "a_rolling_ball_is_trapped_under_the_foot", false, "roll refused"));
            return checks;
        }

        int ballId = ball.RuntimeId;
        float speedWhileRolling = 0.0f;
        float closestSurface = float.MaxValue;
        SoccerBallReading closestReading = default;
        bool everReserved = false;
        bool trapped = false;
        for (int tick = 0; tick < 900 && !trapped; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (!GodotObject.IsInstanceValid(ball))
                break;
            if (lab.Buddy.ObjectInteraction.SoccerPhase != SoccerPlayPhase.Trapping)
            {
                speedWhileRolling = Mathf.Max(
                    speedWhileRolling, Mathf.Abs(ball!.LinearVelocity.X));
                SoccerBallReading live = lab.Buddy.ObjectInteraction.LastSoccerReading;
                everReserved |= lab.Buddy.ObjectInteraction.SoccerBallReserved;
                if (live.IsValid && live.SurfaceDistance < closestSurface)
                {
                    closestSurface = live.SurfaceDistance;
                    closestReading = live;
                }
                continue;
            }

            trapped = true;
        }
        messages.Add(
            $"soccer_approach closest_surface={closestSurface:F1} reserved={everReserved} " +
            $"closest=({closestReading})");

        int dwellAtTrap = lab.Buddy.ObjectInteraction.SoccerDwellTicksRemaining;
        checks.Add(new StartupCheck(
            "a_rolling_ball_is_trapped_under_the_foot",
            trapped &&
            lab.Buddy.ObjectInteraction.SoccerTrapCount == trapsBefore + 1 &&
            lab.Buddy.ObjectInteraction.TrappedRuntimeId == ballId &&
            speedWhileRolling >= play.MinimumApproachSpeed &&
            // A trap is not a pickup: the ball never reaches the hands.
            !lab.Buddy.ObjectInteraction.IsHolding,
            $"trapped={trapped} traps={trapsBefore}->" +
            $"{lab.Buddy.ObjectInteraction.SoccerTrapCount} " +
            $"rolling_speed={speedWhileRolling:F0} dwell={dwellAtTrap} " +
            $"holding={lab.Buddy.ObjectInteraction.IsHolding}"));

        // The beat itself: the ball stays stopped for the whole authored dwell, and the trap
        // owns arbiter priority 5 while it does, so ambient autonomy cannot wander off.
        float fastestWhileTrapped = 0.0f;
        int trappedTicks = 0;
        bool ownedObjectAction = true;
        bool kicked = false;
        for (int tick = 0; tick < play.DwellTicks + 240 && trapped && !kicked; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (!GodotObject.IsInstanceValid(ball))
                break;

            if (lab.Buddy.ObjectInteraction.SoccerPhase == SoccerPlayPhase.Trapping)
            {
                trappedTicks++;
                fastestWhileTrapped = Mathf.Max(
                    fastestWhileTrapped, ball!.LinearVelocity.Length());
                ownedObjectAction &=
                    lab.Buddy.Arbiter.Diagnostics.Owner <= BehaviorPriority.ObjectAction;
                continue;
            }

            kicked = lab.Buddy.ObjectInteraction.SoccerKickCount == kicksBefore + 1;
        }

        checks.Add(new StartupCheck(
            "the_trapped_ball_is_held_still_for_about_a_second",
            trapped && kicked &&
            // The ball is stopped, not merely slowed: the trap writes its velocity to zero
            // every tick it holds it.
            fastestWhileTrapped <= 1.0f &&
            trappedTicks >= play.DwellTicks - 4 &&
            trappedTicks <= play.DwellTicks + 4 &&
            ownedObjectAction,
            $"trapped_ticks={trappedTicks} authored_dwell={play.DwellTicks} " +
            $"fastest_while_trapped={fastestWhileTrapped:F2} " +
            $"owned_object_action={ownedObjectAction}"));

        Vector2 kickVelocity = lab.Buddy.ObjectInteraction.LastSoccerKickVelocity;
        float loft = lab.Buddy.ObjectInteraction.LastSoccerKickLoftDegrees;
        float torsoAtKick = lab.Buddy.Rig.Torso.GlobalPosition.X;
        float ballAtKick = GodotObject.IsInstanceValid(ball)
            ? ball!.GlobalPosition.X
            : torsoAtKick;
        float awaySign = Mathf.Sign(ballAtKick - torsoAtKick);

        // Where it actually ends up, not merely what was commanded: sampled a beat later, the
        // ball must be further from the buddy than it was at the kick.
        float gapAtKick = Mathf.Abs(ballAtKick - torsoAtKick);
        for (int tick = 0; tick < 30; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        float gapAfter = GodotObject.IsInstanceValid(ball)
            ? Mathf.Abs(ball!.GlobalPosition.X - lab.Buddy.Rig.Torso.GlobalPosition.X)
            : gapAtKick;

        checks.Add(new StartupCheck(
            "the_kick_sends_the_ball_back_away_from_the_buddy",
            kicked &&
            lab.Buddy.ObjectInteraction.SoccerKickCount == kicksBefore + 1 &&
            !Mathf.IsZeroApprox(kickVelocity.X) &&
            Mathf.Sign(kickVelocity.X) == awaySign &&
            Mathf.Abs(kickVelocity.Length() - play.KickSpeed) <= 1.0f &&
            gapAfter > gapAtKick + 20.0f,
            $"kicked={kicked} velocity={kickVelocity} speed={kickVelocity.Length():F0} " +
            $"authored={play.KickSpeed:F0} away_sign={awaySign} " +
            $"gap={gapAtKick:F1}->{gapAfter:F1}"));

        checks.Add(new StartupCheck(
            "the_kick_is_straight_or_angled_a_little",
            kicked &&
            loft >= 0.0f &&
            loft <= play.MaximumKickLoftDegrees + 0.01f &&
            // Loft rises; screen space puts up at negative Y.
            (Mathf.IsZeroApprox(loft) ? Mathf.IsZeroApprox(kickVelocity.Y) : kickVelocity.Y < 0.0f),
            $"loft={loft:F1} maximum={play.MaximumKickLoftDegrees:F1} " +
            $"velocity_y={kickVelocity.Y:F1}"));

        messages.Add(
            $"soccer_play traps={lab.Buddy.ObjectInteraction.SoccerTrapCount} " +
            $"kicks={lab.Buddy.ObjectInteraction.SoccerKickCount} " +
            $"rolling_speed={speedWhileRolling:F0} trapped_ticks={trappedTicks} " +
            $"kick={kickVelocity} loft={loft:F1} gap={gapAtKick:F1}->{gapAfter:F1}");

        if (GodotObject.IsInstanceValid(ball))
            RemoveObject(lab, ball!);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        return checks;
    }

    /// <summary>
    /// Rolls one ball along the floor at the buddy, fast enough to be a pass and slow enough
    /// not to be a projectile. It is spawned unheld and given a real velocity, so everything
    /// after this point is the buddy reacting to ordinary physics.
    /// </summary>
    private static LooseObjectBody? RollBallAtBuddy(
        BuddyLab lab,
        LooseObjectProfile soccer,
        SoccerPlayProfile play)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        // From whichever side has room for the roll to develop.
        float side = torso.X - room.Position.X > room.End.X - torso.X ? -1.0f : 1.0f;
        float startX = Mathf.Clamp(
            torso.X + (side * 150.0f),
            room.Position.X + soccer.Radius + 2.0f,
            room.End.X - soccer.Radius - 2.0f);
        var spawn = new Vector2(startX, room.End.Y - soccer.Radius - 1.0f);

        // Comfortably inside the authored approach window at the moment it arrives.
        float speed = (play.MinimumApproachSpeed + play.MaximumApproachSpeed) * 0.25f;
        LooseObjectBody? ball = lab.SpawnLooseObject(
            soccer, spawn, new Vector2(-side * speed, 0.0f));
        if (ball is not null)
            lab.Objects.MarkPlayerThrown(ball, ContentIds.ToolSoccerBall);
        return ball;
    }

    /// <summary>
    /// The Drink's whole contract, in the order the cooldown allows it to be shown: it is
    /// placed by its own spawn key, a completely full buddy still takes one, abandoning it
    /// starts no cooldown, a Meal and a Drink can be taken back to back, the Drink's running
    /// cooldown does not gate the Meal, and a second Drink inside the minute is refused for
    /// the timer.
    /// </summary>
    private static async Task<List<StartupCheck>> CheckDrinkCare(
        SceneTree tree,
        BuddyLab lab,
        List<string> messages)
    {
        var checks = new List<StartupCheck>();
        ObjectInteractionAccess buddy = new(lab);

        // --- The spawn key, and a buddy with no room left at all ---
        lab.Progress.FillHunger(lab.Progress.Appetite);
        float fullnessWhenFull = lab.Progress.Fullness;
        float moodBeforeCancel = lab.Progress.Mood;
        int refusalsBefore = buddy.RefusalCount;
        bool placedFirst = await Place(tree, lab, ContentIds.ToolDrink);
        LooseObjectBody? firstDrink = lab.Launcher.CurrentLaunchable;
        bool admitted = placedFirst && lab.Objects.Count == 1 &&
            GodotObject.IsInstanceValid(firstDrink) &&
            lab.Objects.TryGetSnapshot(firstDrink!.RuntimeId, out LooseObjectSnapshot drinkSnapshot) &&
            drinkSnapshot.Consumable && !drinkSnapshot.Hazardous;

        checks.Add(new StartupCheck(
            "drink_spawns_like_a_meal",
            admitted &&
            firstDrink!.SemanticContentId == ContentIds.ToolDrink &&
            lab.Pipeline.SelectedTool == ToolId.Grab &&
            !lab.Grab.IsGrabbing,
            $"placed={placedFirst} admitted={admitted} count={lab.Objects.Count} " +
            $"content={lab.Launcher.CurrentLaunchableContentId}"));

        bool startedDrinking = admitted && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.Activity.Current == ActivityId.Eat,
            FetchTimeoutTicks);
        // The Drink is not eaten in bites: it is raised to the head once and held there
        // (owner instruction 2026-08-01), so "under way" is the raise having landed.
        bool raised = startedDrinking && await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.Activity.EatLift >= 0.99f, 600);
        bool oneStepGesture = lab.Buddy.Activity.EatBiteCount == 1 &&
            lab.Buddy.Activity.Gesture.Style == ConsumeGestureStyle.SingleRaise;
        bool neverRefused = buddy.RefusalCount == refusalsBefore &&
            buddy.LastConsumeRejection == ConsumeRejection.None;

        checks.Add(new StartupCheck(
            "a_full_buddy_still_accepts_a_drink",
            startedDrinking && raised && oneStepGesture && neverRefused &&
            lab.Progress.Fullness <= fullnessWhenFull + 0.01f,
            $"started={startedDrinking} raised={raised} one_step={oneStepGesture} " +
            $"lift={lab.Buddy.Activity.EatLift:F2} " +
            $"rejection={buddy.LastConsumeRejection} refusals={buddy.RefusalCount} " +
            $"fullness={lab.Progress.Fullness:F1} appetite={lab.Progress.Appetite:F1}"));

        // Abandoned mid-drink: FR-008.10 says that starts nothing.
        int cancelsBefore = buddy.ConsumeCancelCount;
        lab.Buddy.ObjectInteraction.CancelActiveInteraction();
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        int cooldownAfterCancel = buddy.CooldownTicksRemaining(ContentIds.ToolDrink);
        checks.Add(new StartupCheck(
            "a_cancelled_drink_starts_no_cooldown",
            raised &&
            buddy.ConsumeCancelCount == cancelsBefore + 1 &&
            buddy.ConsumeSuccessCount == 0 &&
            cooldownAfterCancel == 0 &&
            Mathf.Abs(lab.Progress.Mood - moodBeforeCancel) < 0.01f,
            $"cancels={cancelsBefore}->{buddy.ConsumeCancelCount} " +
            $"successes={buddy.ConsumeSuccessCount} cooldown={cooldownAfterCancel} " +
            $"mood={lab.Progress.Mood:F1} was={moodBeforeCancel:F1}"));

        // The abandoned can is still on the floor and a full buddy would happily go back for
        // it, which would spend the very cooldown the next legs need to be clear.
        if (GodotObject.IsInstanceValid(firstDrink))
            RemoveObject(lab, firstDrink!);
        lab.Progress.DrainHunger(1800.0, HungerActivity.Playing);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        // --- Meal, then Drink, with nothing in between ---
        float moodBeforeMeal = lab.Progress.Mood;
        float fullnessBeforeMeal = lab.Progress.Fullness;
        bool ateMeal = await Consume(tree, lab, ContentIds.ToolMeal);
        float moodAfterMeal = lab.Progress.Mood;
        float fullnessAfterMeal = lab.Progress.Fullness;
        int raiseCount = 0;
        int holdTicks = 0;
        bool drankAfterMeal = ateMeal &&
            await ConsumeDrinkMeasured(tree, lab, out_raises: r => raiseCount = r,
                out_hold: h => holdTicks = h);
        float fullnessAfterDrink = lab.Progress.Fullness;
        int drinkCooldown = buddy.CooldownTicksRemaining(ContentIds.ToolDrink);
        int mealCooldown = buddy.CooldownTicksRemaining(ContentIds.ToolMeal);

        checks.Add(new StartupCheck(
            "meal_then_drink_both_succeed",
            ateMeal && drankAfterMeal &&
            Mathf.Abs(moodAfterMeal - (moodBeforeMeal + MealMoodGain)) < 0.01f &&
            Mathf.Abs(lab.Progress.Mood - (moodAfterMeal + DrinkMoodGain)) < 0.01f &&
            // The meal fills the bar by its portion; the Drink adds nothing to it at all. The
            // lower bound is loose by a point because appetite keeps draining in real time
            // while the buddy walks over and drinks.
            fullnessAfterMeal - fullnessBeforeMeal >= MealHungerFill - 1.0f &&
            fullnessAfterMeal - fullnessBeforeMeal <= MealHungerFill + 0.01f &&
            fullnessAfterDrink <= fullnessAfterMeal + 0.01f &&
            drinkCooldown > 0 && mealCooldown == 0,
            $"meal={ateMeal} drink={drankAfterMeal} mood={moodBeforeMeal:F1}->" +
            $"{moodAfterMeal:F1}->{lab.Progress.Mood:F1} " +
            $"fullness={fullnessBeforeMeal:F1}->{fullnessAfterMeal:F1}->{fullnessAfterDrink:F1} " +
            $"drink_cooldown={drinkCooldown} meal_cooldown={mealCooldown}"));

        // The gesture itself, measured off runtime state: exactly one raise to the head, held
        // there for the authored two seconds, and then the can is gone (owner instruction
        // 2026-08-01). The Meal is untouched -- it still takes its five bites, which the
        // meal_consume gate continues to assert.
        checks.Add(new StartupCheck(
            "the_drink_is_raised_once_held_and_gone",
            drankAfterMeal &&
            raiseCount == 1 &&
            holdTicks >= 230 && holdTicks <= 260 &&
            lab.Objects.Count == 0,
            $"raises={raiseCount} hold_ticks={holdTicks} authored_hold=240 " +
            $"objects_left={lab.Objects.Count}"));

        // --- The Drink's running cooldown is its own; the Meal never sees it ---
        float moodBeforeSecondMeal = lab.Progress.Mood;
        bool ateAgain = drankAfterMeal && await Consume(tree, lab, ContentIds.ToolMeal);
        checks.Add(new StartupCheck(
            "the_drinks_cooldown_does_not_gate_the_meal",
            ateAgain &&
            buddy.CooldownTicksRemaining(ContentIds.ToolDrink) > 0 &&
            Mathf.Abs(lab.Progress.Mood - (moodBeforeSecondMeal + MealMoodGain)) < 0.01f,
            $"ate_again={ateAgain} " +
            $"drink_cooldown={buddy.CooldownTicksRemaining(ContentIds.ToolDrink)} " +
            $"mood={moodBeforeSecondMeal:F1}->{lab.Progress.Mood:F1}"));

        // --- A second Drink inside the minute ---
        float moodBeforeRefusal = lab.Progress.Mood;
        int successesBefore = buddy.ConsumeSuccessCount;
        bool placedSecond = await Place(tree, lab, ContentIds.ToolDrink);
        LooseObjectBody? secondDrink = lab.Launcher.CurrentLaunchable;
        bool refusedOnCooldown = placedSecond && await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => buddy.LastConsumeRejection == ConsumeRejection.OnCooldown,
            FetchTimeoutTicks);

        checks.Add(new StartupCheck(
            "a_second_drink_inside_the_minute_is_refused_on_cooldown",
            refusedOnCooldown &&
            buddy.ConsumeSuccessCount == successesBefore &&
            Mathf.Abs(lab.Progress.Mood - moodBeforeRefusal) < 0.01f &&
            buddy.CooldownTicksRemaining(ContentIds.ToolDrink) > 0,
            $"placed={placedSecond} rejection={buddy.LastConsumeRejection} " +
            $"successes={successesBefore}->{buddy.ConsumeSuccessCount} " +
            $"mood={moodBeforeRefusal:F1}->{lab.Progress.Mood:F1} " +
            $"cooldown={buddy.CooldownTicksRemaining(ContentIds.ToolDrink)}"));

        if (GodotObject.IsInstanceValid(secondDrink))
            RemoveObject(lab, secondDrink!);

        messages.Add(
            $"drink_care successes={buddy.ConsumeSuccessCount} " +
            $"cancels={buddy.ConsumeCancelCount} " +
            $"drink_cooldown={buddy.CooldownTicksRemaining(ContentIds.ToolDrink)} " +
            $"meal_cooldown={buddy.CooldownTicksRemaining(ContentIds.ToolMeal)}");
        return checks;
    }

    /// <summary>
    /// Places one launchable on the floor beside the buddy through the real launcher, which
    /// also clears the room — so each leg starts from one object and the previous leg's
    /// leftovers cannot be re-fetched mid-measurement.
    /// </summary>
    private static async Task<bool> Place(SceneTree tree, BuddyLab lab, string contentId)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        float torsoX = lab.Buddy.Rig.Torso.GlobalPosition.X;
        float side = room.End.X - torsoX > 110.0f ? 1.0f : -1.0f;
        float spawnX = Mathf.Clamp(
            torsoX + (side * 80.0f),
            room.Position.X + 20.0f,
            room.End.X - 20.0f);
        lab.Launcher.RequestSpawn(contentId, new Vector2(spawnX, room.End.Y - 24.0f));

        // The launcher consumes queued intent on the root's routed tick, never inline.
        for (int tick = 0; tick < 8; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        LooseObjectBody? placed = lab.Launcher.CurrentLaunchable;
        return GodotObject.IsInstanceValid(placed) &&
            placed!.SemanticContentId == contentId;
    }

    /// <summary>
    /// Places one Drink, waits for the buddy to finish it, and measures the gesture on the way
    /// through: how many separate raises to the head there were, and how many routed ticks the
    /// can spent held up there. A bite gesture would report five raises and a short hold.
    /// </summary>
    private static async Task<bool> ConsumeDrinkMeasured(
        SceneTree tree,
        BuddyLab lab,
        System.Action<int> out_raises,
        System.Action<int> out_hold)
    {
        int before = lab.Buddy.ObjectInteraction.ConsumeSuccessCount;
        if (!await Place(tree, lab, ContentIds.ToolDrink))
        {
            out_raises(0);
            out_hold(0);
            return false;
        }

        int raises = 0;
        int hold = 0;
        bool atTop = false;
        bool done = false;
        bool wasEating = false;
        for (int tick = 0; tick < ConsumeTimeoutTicks && !done; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            bool eating = lab.Buddy.Activity.Current == ActivityId.Eat;
            // Measure the gesture that actually completes. An attempt the buddy abandons
            // partway -- it can be knocked off the can -- starts the count again rather than
            // adding a phantom second raise to it.
            if (eating && !wasEating)
            {
                raises = 0;
                hold = 0;
                atTop = false;
            }

            wasEating = eating;
            if (eating)
            {
                bool up = lab.Buddy.Activity.EatLift >= 0.99f;
                if (up)
                {
                    hold++;
                    if (!atTop)
                        raises++;
                }

                atTop = up;
            }

            done = lab.Buddy.ObjectInteraction.ConsumeSuccessCount == before + 1;
        }

        out_raises(raises);
        out_hold(hold);
        return done;
    }

    /// <summary>Places one item and waits for the buddy to finish consuming it.</summary>
    private static async Task<bool> Consume(SceneTree tree, BuddyLab lab, string contentId)
    {
        int before = lab.Buddy.ObjectInteraction.ConsumeSuccessCount;
        if (!await Place(tree, lab, contentId))
            return false;

        return await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.ObjectInteraction.ConsumeSuccessCount == before + 1,
            ConsumeTimeoutTicks);
    }

    private static LooseObjectProfile? FindLaunchable(BuddyLab lab, string contentId)
    {
        foreach (LooseObjectProfile profile in lab.Launcher.LaunchableProfiles)
        {
            if (GodotObject.IsInstanceValid(profile) && profile.ContentId == contentId)
                return profile;
        }

        return null;
    }

    private static void RemoveObject(BuddyLab lab, LooseObjectBody body)
    {
        if (!GodotObject.IsInstanceValid(body))
            return;

        if (lab.Buddy.ObjectInteraction.IsHolding &&
            lab.Buddy.ObjectInteraction.TrackedRuntimeId == body.RuntimeId)
        {
            lab.Buddy.ObjectInteraction.CancelActiveInteraction();
        }

        if (lab.Grab.IsGrabbing)
            lab.Grab.Release(countsAsThrow: false);
        lab.Objects.Unregister(body);
        body.QueueFree();
    }

    /// <summary>Reader for the buddy's consume counters; keeps the legs above readable.</summary>
    private readonly struct ObjectInteractionAccess(BuddyLab lab)
    {
        private readonly BuddyLab _lab = lab;

        public int ConsumeSuccessCount => _lab.Buddy.ObjectInteraction.ConsumeSuccessCount;
        public int ConsumeCancelCount => _lab.Buddy.ObjectInteraction.ConsumeCancelCount;
        public int RefusalCount => _lab.Buddy.ObjectInteraction.RefusalCount;
        public ConsumeRejection LastConsumeRejection =>
            _lab.Buddy.ObjectInteraction.LastConsumeRejection;

        public int CooldownTicksRemaining(string contentId) =>
            _lab.Buddy.ObjectInteraction.CooldownTicksRemaining(contentId);
    }
}
