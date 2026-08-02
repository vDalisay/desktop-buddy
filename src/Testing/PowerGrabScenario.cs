using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Grab;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Power Grab against Normal Grab, same rig, same pose, same scripted cursor path (M5 §1.2).
/// The whole scenario is one comparison: everything that must be identical is measured on
/// both runs, and the four things that must differ are measured as a difference rather than
/// as an absolute — so re-tuning the profile at the owner feel gate cannot invalidate it.
///
/// <para>Rows 8 and 9 are the safety core: a cancel and a mid-hold tool switch must stay
/// under the <i>Normal</i> cap, which is what proves the raised Power cap is reachable only
/// by a deliberate throw.</para>
/// </summary>
public sealed class PowerGrabScenario : IScenario
{
    private const int SettleTimeoutTicks = 720;
    private const int DragTicks = 90;
    private const float DragReach = 96.0f;
    private const int SoakTicks = 10_000;
    private const float LooseObjectLinearDamp = 1.5f;
    private const float LooseObjectAngularDamp = 2.0f;

    public string Id => "power_grab";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("power_scene_loadable", false, "res://scenes/buddy_lab.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        PowerGrabProfile? power = lab.Pointer.PowerProfile;
        checks.Add(new StartupCheck(
            "power_profile_is_wired_and_valid",
            power is not null && power.Validate().Count == 0,
            power is null ? "missing" : string.Join("; ", power.Validate())));
        if (power is null || power.Validate().Count > 0)
        {
            lab.QueueFree();
            return new ScenarioResult(false, checks, messages);
        }

        bool standing = await WaitForStanding(tree, lab, SettleTimeoutTicks);
        checks.Add(new StartupCheck(
            "power_starts_from_standing",
            standing,
            $"stable_ticks={lab.Buddy.Standing.Snapshot.StableTicks}"));

        var loose = new LooseObjectBody();
        loose.Configure(12.0f, 1.0f, LooseObjectLinearDamp, LooseObjectAngularDamp);
        lab.AddChild(loose);
        loose.GlobalPosition = new Vector2(120.0f, 300.0f);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        // --- Rows 1-6: the same drag, twice ---
        HoldResult normal = await MeasureHold(tree, lab, loose, power: null);
        await ReSettle(tree, lab);
        HoldResult powered = await MeasureHold(tree, lab, loose, power);
        await ReSettle(tree, lab);

        checks.Add(new StartupCheck(
            "both_variants_acquire_a_buddy_part_and_a_loose_object",
            normal.GrabbedPart && normal.GrabbedLoose && powered.GrabbedPart && powered.GrabbedLoose,
            $"normal=({normal.GrabbedPart},{normal.GrabbedLoose}) " +
            $"power=({powered.GrabbedPart},{powered.GrabbedLoose})"));

        // Power tracks the cursor harder, so it sits closer to it: less residual extension.
        checks.Add(new StartupCheck(
            "power_tracks_the_cursor_harder_than_normal",
            powered.MedianExtension < normal.MedianExtension,
            $"normal={normal.MedianExtension:F2}px power={powered.MedianExtension:F2}px"));

        checks.Add(new StartupCheck(
            "both_variants_share_one_stretch_limit",
            Mathf.IsEqualApprox(normal.StretchLimit, powered.StretchLimit),
            $"normal={normal.StretchLimit:F4} power={powered.StretchLimit:F4}"));

        // The end state is deliberately not asserted to be Straining: Power pulls hard
        // enough that the whole buddy is dragged to the cursor, which brings the hand back
        // inside its own limit. That is the tool working, not the strain lapsing — the
        // never-snaps promise is what §1.2 actually makes, and the saturated strain counter
        // in the soak row below is where "buzzes at peak indefinitely" is measured.
        checks.Add(new StartupCheck(
            "only_normal_lets_the_buddy_snap_free",
            !normal.StillHolding && normal.Snaps == 1 &&
            powered.StillHolding && powered.Snaps == 0 &&
            powered.EndState != GrabStretchState.Snapped,
            $"normal=(holding={normal.StillHolding},snaps={normal.Snaps}) " +
            $"power=(holding={powered.StillHolding},snaps={powered.Snaps}," +
            $"state={powered.EndState})"));

        checks.Add(new StartupCheck(
            "the_buddy_keeps_struggling_through_the_whole_power_hold",
            powered.StruggledAtTheEnd,
            $"struggling_at_last_tick={powered.StruggledAtTheEnd} " +
            $"ever={powered.EverStruggled}"));

        // --- Row 7: the deliberate throw is the only place the raised cap applies ---
        float normalCap = lab.Grab.Profile.ThrowSpeedCap;
        float normalThrow = await ThrowAtCap(tree, lab, loose, power: null, normalCap);
        float powerThrow = await ThrowAtCap(tree, lab, loose, power, normalCap);
        checks.Add(new StartupCheck(
            "a_power_throw_leaves_faster_but_still_inside_its_own_cap",
            powerThrow > normalThrow && powerThrow <= power.ReleaseSpeedCap + 0.5f,
            $"normal={normalThrow:F1} power={powerThrow:F1} " +
            $"normal_cap={normalCap:F1} power_cap={power.ReleaseSpeedCap:F1}"));

        // --- Row 8: a cancelled Power hold is indistinguishable from a Normal one ---
        float cancelSpeed = await ThrowAtCap(
            tree, lab, loose, power, normalCap, countsAsThrow: false);
        checks.Add(new StartupCheck(
            "cancelling_a_power_hold_never_gets_the_power_launch",
            cancelSpeed <= normalCap + 0.5f,
            $"released={cancelSpeed:F1} normal_cap={normalCap:F1}"));

        // --- Row 9: switching tools mid-Power-hold drops, it does not fling ---
        lab.Pipeline.SelectTool(ToolId.PowerGrab);
        lab.Grab.TryGrab(loose, loose.GlobalPosition, power);
        loose.LinearVelocity = new Vector2(normalCap * 3.0f, 0.0f);
        lab.Pipeline.SelectTool(ToolId.Grab);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck(
            "switching_away_mid_power_hold_drops_under_the_normal_cap",
            !lab.Grab.IsGrabbing && lab.Grab.LastReleaseSpeed <= normalCap + 0.5f,
            $"grabbing={lab.Grab.IsGrabbing} released={lab.Grab.LastReleaseSpeed:F1} " +
            $"normal_cap={normalCap:F1}"));

        // --- Row 10: a Power hold left running does not drift, overflow, or escape ---
        SoakResult soak = await SoakPowerHold(tree, lab, power);
        checks.Add(new StartupCheck(
            "a_ten_thousand_tick_power_hold_saturates_and_stays_finite",
            soak.StrainTicks == lab.Grab.Profile.StretchShakeTicks &&
            soak.StillHolding && soak.InsideRoom && !soak.SawNonFinite,
            $"strain={soak.StrainTicks}/{lab.Grab.Profile.StretchShakeTicks} " +
            $"holding={soak.StillHolding} inside_room={soak.InsideRoom} " +
            $"non_finite={soak.SawNonFinite}"));

        lab.Grab.Release(countsAsThrow: false);

        messages.Add(
            $"normal_extension={normal.MedianExtension:F2}px " +
            $"power_extension={powered.MedianExtension:F2}px " +
            $"normal_throw={normalThrow:F1} power_throw={powerThrow:F1} " +
            $"stiffness_x={power.StiffnessMultiplier:F2} damping_x={power.DampingMultiplier:F2} " +
            $"force_x={power.MaximumForceMultiplier:F2} " +
            $"release_x={power.ReleaseVelocityMultiplier:F2} " +
            $"power_cap={power.ReleaseSpeedCap:F0}");

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// One identical hold for both variants: acquire a loose object, then a leashed hand,
    /// drag it well past the stretch limit and keep it there long enough for Normal to snap.
    /// </summary>
    private static async Task<HoldResult> MeasureHold(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectBody loose,
        PowerGrabProfile? power)
    {
        bool grabbedLoose = lab.Grab.TryGrab(loose, loose.GlobalPosition, power);
        lab.Grab.Release(countsAsThrow: false);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        PuppetPartBody hand = lab.Buddy.Rig.LeftHand;
        Vector2 anchor = hand.StretchAnchorWorld;
        int snapsBefore = lab.Grab.SnapCount;
        bool grabbedPart = lab.Grab.TryGrab(hand, hand.GlobalPosition, power);
        float limit = lab.Grab.Profile.StretchLimitHandWidths * hand.Radius * 2.0f;

        // Far outside the limit and held there: this is the pull the buddy must lose to
        // Normal Grab and never win against Power.
        Vector2 cursor = anchor + new Vector2(limit + DragReach, 0.0f);

        var extensions = new List<float>(DragTicks);
        bool everStruggled = false;
        for (int tick = 0; tick < DragTicks; tick++)
        {
            lab.Grab.MoveCursor(cursor);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Grab.IsGrabbing)
                extensions.Add(lab.Grab.Telemetry.Extension);
            everStruggled |= lab.Buddy.GrabResistance.Intent.Active;
        }

        // Long enough that Normal has spent its whole countdown and snapped.
        int holdTicks = lab.Grab.Profile.StretchShakeTicks + 120;
        bool struggledAtTheEnd = false;
        for (int tick = 0; tick < holdTicks; tick++)
        {
            if (lab.Grab.IsGrabbing)
                lab.Grab.MoveCursor(cursor);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            everStruggled |= lab.Buddy.GrabResistance.Intent.Active;
            struggledAtTheEnd = lab.Buddy.GrabResistance.Intent.Active;
        }

        var result = new HoldResult(
            grabbedPart,
            grabbedLoose,
            Median(extensions),
            limit,
            lab.Grab.IsGrabbing,
            lab.Grab.SnapCount - snapsBefore,
            lab.Grab.StretchState,
            everStruggled,
            struggledAtTheEnd);

        if (lab.Grab.IsGrabbing)
            lab.Grab.Release(countsAsThrow: false);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        return result;
    }

    /// <summary>
    /// Releases a body already moving far past every cap, so the measured speed is the cap
    /// that applied rather than whatever the tether happened to build up.
    /// </summary>
    private static async Task<float> ThrowAtCap(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectBody loose,
        PowerGrabProfile? power,
        float normalCap,
        bool countsAsThrow = true)
    {
        lab.Grab.TryGrab(loose, loose.GlobalPosition, power);
        loose.LinearVelocity = new Vector2(normalCap * 3.0f, 0.0f);
        lab.Grab.Release(countsAsThrow);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        return lab.Grab.LastReleaseSpeed;
    }

    private static async Task<SoakResult> SoakPowerHold(
        SceneTree tree,
        BuddyLab lab,
        PowerGrabProfile power)
    {
        PuppetPartBody hand = lab.Buddy.Rig.LeftHand;
        Vector2 anchor = hand.StretchAnchorWorld;
        float limit = lab.Grab.Profile.StretchLimitHandWidths * hand.Radius * 2.0f;
        Vector2 cursor = anchor + new Vector2(limit + DragReach, 0.0f);
        lab.Grab.TryGrab(hand, hand.GlobalPosition, power);

        Rect2 room = lab.Boundaries.InnerBounds.Grow(64.0f);
        bool insideRoom = true;
        bool sawNonFinite = false;
        for (int tick = 0; tick < SoakTicks; tick++)
        {
            if (lab.Grab.IsGrabbing)
                lab.Grab.MoveCursor(cursor);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

            foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
            {
                Vector2 position = part.GlobalPosition;
                sawNonFinite |= !IsFinite(position) || !IsFinite(part.LinearVelocity);
                insideRoom &= room.HasPoint(position);
            }

            sawNonFinite |= !float.IsFinite(lab.Grab.Telemetry.Extension) ||
                !IsFinite(lab.Grab.Telemetry.Force) ||
                !float.IsFinite(lab.Grab.StretchOverpull);
        }

        return new SoakResult(
            lab.Grab.StretchStrainTicks,
            lab.Grab.IsGrabbing,
            insideRoom,
            sawNonFinite);
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static float Median(List<float> values)
    {
        if (values.Count == 0)
            return 0.0f;
        values.Sort();
        return values[values.Count / 2];
    }

    private static async Task ReSettle(SceneTree tree, BuddyLab lab)
    {
        for (int tick = 0; tick < SettleTimeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.Standing.Snapshot.IsStable)
                return;
        }
    }

    private static async Task<bool> WaitForStanding(SceneTree tree, BuddyLab lab, int timeoutTicks)
    {
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.Standing.Snapshot.IsStable)
                return true;
        }

        return false;
    }

    private readonly record struct HoldResult(
        bool GrabbedPart,
        bool GrabbedLoose,
        float MedianExtension,
        float StretchLimit,
        bool StillHolding,
        int Snaps,
        GrabStretchState EndState,
        bool EverStruggled,
        bool StruggledAtTheEnd);

    private readonly record struct SoakResult(
        int StrainTicks,
        bool StillHolding,
        bool InsideRoom,
        bool SawNonFinite);
}
