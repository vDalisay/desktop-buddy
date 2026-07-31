using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M5 Task 8 gate for the Soccer Ball and the Drink — the milestone's two data-driven reuses.
/// Nothing here is new machinery: the ball is a second pullback launchable that rides the
/// existing clean-catch rule, and the Drink is a second care consumable on the same two-phase
/// consume transaction the Meal uses. What the scenario owns is the proof that the data
/// actually differs where it is supposed to and is shared where it is supposed to be.
///
/// <para>Five things are measured rather than asserted. The Soccer Ball's authored bounce
/// gives it a drop signature the Baseball does not have, and the Baseball's own signature is
/// pinned so the restitution seam can be shown not to have moved anything that authored no
/// bounce. The Drink is placed by its own spawn key like any launchable. A clean soccer catch
/// pays the one care point once per throw. And the Meal and the Drink gate each other not at
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

        // --- Phase 3: the clean-catch rule, unchanged, on a new ball ---
        checks.Add(await CheckCleanSoccerCatch(tree, lab, soccer, messages));

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
    /// The clean-catch rule is inherited whole: a Soccer Ball taken out of the air pays the
    /// one care point, and pays it once for that throw.
    /// </summary>
    private static async Task<StartupCheck> CheckCleanSoccerCatch(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectProfile soccer,
        List<string> messages)
    {
        // A buddy in a decent mood is a buddy that reaches for a ball; the catch rule, not the
        // mood ladder, is what this leg is about.
        lab.Progress.ApplyCareMood(30.0f);
        await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.ObjectInteraction.Phase == ObjectPhase.Idle, 600);

        int cleanBefore = lab.Buddy.ObjectInteraction.CleanCatchCount;
        int careBefore = lab.Buddy.ObjectInteraction.CatchCareCount;
        float moodBefore = lab.Progress.Mood;
        LooseObjectBody? ball = M4ObjectScenarioSupport.SpawnCleanThrow(lab, profile: soccer);
        bool caught = ball is not null && await M4ObjectScenarioSupport.WaitForPhase(
            tree, lab, ObjectPhase.Hold, 600);
        float moodAtCatch = lab.Progress.Mood;

        // Once per originating throw: keep watching while it is still carried.
        for (int tick = 0; tick < 240; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        bool paidOnce =
            lab.Buddy.ObjectInteraction.CleanCatchCount == cleanBefore + 1 &&
            lab.Buddy.ObjectInteraction.CatchCareCount == careBefore + 1;
        bool paidOnePoint = Mathf.Abs(moodAtCatch - (moodBefore + 1.0f)) < 0.01f;

        string detail =
            $"caught={caught} clean={cleanBefore}->{lab.Buddy.ObjectInteraction.CleanCatchCount} " +
            $"care={careBefore}->{lab.Buddy.ObjectInteraction.CatchCareCount} " +
            $"mood={moodBefore:F2}->{moodAtCatch:F2}";
        messages.Add($"soccer_catch {detail}");

        if (ball is not null)
            RemoveObject(lab, ball);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        return new StartupCheck(
            "a_clean_soccer_catch_pays_one_care_once",
            caught && paidOnce && paidOnePoint,
            detail);
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
        bool bitTwice = startedDrinking && await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.Activity.EatBitesCompleted >= 2, 600);
        bool neverRefused = buddy.RefusalCount == refusalsBefore &&
            buddy.LastConsumeRejection == ConsumeRejection.None;

        checks.Add(new StartupCheck(
            "a_full_buddy_still_accepts_a_drink",
            startedDrinking && bitTwice && neverRefused &&
            lab.Progress.Fullness <= fullnessWhenFull + 0.01f,
            $"started={startedDrinking} bites={lab.Buddy.Activity.EatBitesCompleted} " +
            $"rejection={buddy.LastConsumeRejection} refusals={buddy.RefusalCount} " +
            $"fullness={lab.Progress.Fullness:F1} appetite={lab.Progress.Appetite:F1}"));

        // Abandoned mid-drink: FR-008.10 says that starts nothing.
        int cancelsBefore = buddy.ConsumeCancelCount;
        lab.Buddy.ObjectInteraction.CancelActiveInteraction();
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        int cooldownAfterCancel = buddy.CooldownTicksRemaining(ContentIds.ToolDrink);
        checks.Add(new StartupCheck(
            "a_cancelled_drink_starts_no_cooldown",
            bitTwice &&
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
        bool drankAfterMeal = ateMeal && await Consume(tree, lab, ContentIds.ToolDrink);
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
