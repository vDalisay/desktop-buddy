using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The M5 Grenade gate (Task 6 plan Tasks B–D): the pin, the fuse, the blast, and the
/// presentation punctuation, on the real composition and through the real input chord.
///
/// <para>The claims worth stating up front, because the checks are written to catch them
/// if they stop being true:</para>
/// <list type="bullet">
///   <item>A grenade thrown with a plain grab is a ball. Only the secondary press that
///   begins a pullback pulls the pin, so there is no separate arming input and no way to
///   arm one by accident.</item>
///   <item>Once the pin is out the grenade is safe for exactly as long as somebody holds
///   it, and once let go nothing stops the three seconds — not catching it, not picking it
///   back up.</item>
///   <item>The blast is an impulse through the same shared curve every collision uses.
///   Nothing here multiplies damage; the falloff curve is the only authored quantity.</item>
///   <item>The pin is cosmetic on the magazine's rules, and the blast moves loose objects
///   without any of them entering the pain pipeline.</item>
/// </list>
/// </summary>
public sealed class GrenadeFuseScenario : IScenario
{
    /// <summary>Six seconds — twice the fuse — is "it is never going off".</summary>
    private const int SafetySoakTicks = 720;

    /// <summary>
    /// Where the witness object sits when the blast goes off, relative to the centre.
    /// Well inside the full-effect radius, so the falloff there is exactly 1 and the speed
    /// it leaves with is the authored shove divided by its mass — nothing to interpret.
    /// Far enough out that it is not overlapping the buddy part the blast is centred on,
    /// whose ejection would be the thing being measured instead.
    /// </summary>
    private static readonly Vector2 WitnessOffset = new(34.0f, -10.0f);

    public string Id => "grenade_fuse";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("grenade_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        GrenadeComponent grenades = lab.Grenades;
        GrenadeProfile profile = grenades.Profile;
        bool is3D = lab.Mode == PresentationMode.Mii3D;
        messages.Add($"presentation={lab.Mode}");

        // --- The mesh stays inside its stated envelope, pin in and pin out ---
        var envelopeReport = new List<string>();
        bool envelopeHolds = true;
        foreach (bool pinIn in new[] { true, false })
        {
            ArrayMesh mesh = GrenadeMeshBuilder.Build(profile, radius: 10.0f, pinIn);
            Vector3[] faces = mesh.GetFaces();
            int outside = 0;
            foreach (Vector3 vertex in faces)
            {
                if (!GrenadeMeshBuilder.IsInsideEnvelope(vertex, profile, 10.0f))
                    outside++;
            }

            envelopeHolds &= outside == 0 && faces.Length > 0;
            envelopeReport.Add($"pin_in={pinIn}: {faces.Length} verts, {outside} outside");
        }

        checks.Add(new StartupCheck(
            "grenade_mesh_stays_inside_its_authored_envelope",
            envelopeHolds,
            $"{string.Join(" | ", envelopeReport)} " +
            $"bound={GrenadeMeshBuilder.EnvelopeRadiusFactor}x drawn radius " +
            $"({GrenadeMeshBuilder.DrawnRadius(profile, 10.0f):F2}px from a 10px collider, " +
            $"scale={profile.VisualScale})"));
        checks.Add(new StartupCheck(
            "oversized_grenade_visual_keeps_its_bottom_on_the_collider",
            Mathf.IsEqualApprox(
                profile.DrawnRadiusPx(10.0f) - profile.VisualGroundOffsetPx(10.0f),
                10.0f) &&
            Mathf.IsEqualApprox(
                profile.DrawnRadiusPx(10.0f) * GrenadeMeshBuilder.BodyBottomRadiusFactor -
                    GrenadeMeshBuilder.VisualGroundOffset(profile, 10.0f),
                10.0f),
            $"drawn={profile.DrawnRadiusPx(10.0f):F2} " +
            $"flat_offset={profile.VisualGroundOffsetPx(10.0f):F2} " +
            $"mesh_offset={GrenadeMeshBuilder.VisualGroundOffset(profile, 10.0f):F2}"));

        Rect2 room = lab.Boundaries.InnerBounds;
        var bench = new Vector2(room.Position.X + 90.0f, room.Position.Y + 70.0f);

        // --- 1. A pinned grenade is a ball, however hard it is thrown ---
        LooseObjectBody? pinned = await Spawn(tree, lab, bench);
        bool spawned = pinned is not null &&
                       pinned.SemanticContentId == ContentIds.ToolGrenade &&
                       grenades.Tracked == pinned &&
                       grenades.Stage == GrenadeFuseStage.Pinned &&
                       lab.Objects.Count == 1;
        int registryAfterSpawn = lab.Objects.Count;

        // Grab and fling it by hand: primary only, never a secondary press.
        await Grab(tree, lab, pinned!);
        var flingTo = new Vector2(room.GetCenter().X, room.Position.Y + 50.0f);
        await M4ObjectScenarioSupport.MovePointer(tree, lab, flingTo, MouseButtonMask.Left);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, flingTo, MouseButton.Left, pressed: false, 0);
        await Idle(tree, SafetySoakTicks);

        checks.Add(new StartupCheck(
            "pin_in_grenade_never_explodes",
            spawned &&
            grenades.DetonationCount == 0 &&
            grenades.PinDropCount == 0 &&
            grenades.Stage == GrenadeFuseStage.Pinned &&
            GodotObject.IsInstanceValid(pinned) && pinned!.RuntimeId != 0,
            $"spawned={spawned} detonations={grenades.DetonationCount} " +
            $"pins={grenades.PinDropCount} stage={grenades.Stage} " +
            $"soak={SafetySoakTicks} ticks registry={registryAfterSpawn}"));

        // --- 2. The first secondary press drops exactly one pin, and it is cosmetic ---
        LooseObjectBody? armed = await Spawn(tree, lab, bench);
        await Grab(tree, lab, armed!);
        Vector2 hold = armed!.GlobalPosition;
        await SetSecondary(tree, lab, hold, pressed: true);
        await M4ObjectScenarioSupport.WaitFor(tree, () => grenades.PinIsOut, 20);
        int pinsAfterFirstPress = grenades.PinDropCount;
        PinBody? pin = FindLivePin(grenades);
        uint pinLayer = pin?.CollisionLayer ?? 1u;
        uint pinMask = pin?.CollisionMask ?? 0u;

        // Cancel the aim and press again: the pin only comes out once.
        await SetSecondary(tree, lab, hold, pressed: false);
        await Idle(tree, 6);
        await SetSecondary(tree, lab, hold, pressed: true);
        await Idle(tree, 6);
        await SetSecondary(tree, lab, hold, pressed: false);
        await Idle(tree, 6);

        // A contact probe, not just a mask reading: the pin is teleported onto the chest
        // and given time to resolve. Nothing may happen.
        long scoredBeforePinProbe = lab.Pipeline.ScoredImpactCount;
        int pinBuddyContactsBefore = pin?.BuddyContactCount ?? 0;
        if (pin is not null && GodotObject.IsInstanceValid(pin))
        {
            pin.GlobalPosition = lab.Buddy.Rig.Torso.GlobalPosition;
            pin.LinearVelocity = new Vector2(0.0f, 40.0f);
            pin.Sleeping = false;
            pin.ResetPhysicsInterpolation();
        }

        await Idle(tree, 30);

        checks.Add(new StartupCheck(
            "pin_drops_on_first_rmb_press",
            pinsAfterFirstPress == 1 &&
            grenades.PinDropCount == 1 &&
            grenades.PinIsOut &&
            pin is not null &&
            pinLayer == 0u &&
            pinMask == CollisionLayers.RoomBounds &&
            lab.Pipeline.ScoredImpactCount == scoredBeforePinProbe &&
            (pin?.BuddyContactCount ?? 0) == pinBuddyContactsBefore &&
            lab.Objects.Count == 1,
            $"pins_after_first={pinsAfterFirstPress} total={grenades.PinDropCount} " +
            $"layer={pinLayer} mask={pinMask} room_bounds={CollisionLayers.RoomBounds} " +
            $"scored={scoredBeforePinProbe}->{lab.Pipeline.ScoredImpactCount} " +
            $"buddy_contacts={pinBuddyContactsBefore}->{pin?.BuddyContactCount ?? 0} " +
            $"registry={lab.Objects.Count} (pins never register)"));

        // One pin, one silhouette: the mode that is drawing owns it and the other is dark.
        // Read after a render frame, because which mesh is on screen is decided in _Process.
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        int pinMeshes = lab.GrenadeVisual.Pins.VisiblePinCount;
        bool pinDrawsItself = pin is not null && pin.Visible;
        checks.Add(new StartupCheck(
            "the_dropped_pin_is_drawn_once_in_the_active_presentation",
            is3D ? pinMeshes == 1 && !pinDrawsItself : pinMeshes == 0 && pinDrawsItself,
            $"mode={lab.Mode} pin_meshes={pinMeshes} flat_pin_drawing={pinDrawsItself} " +
            $"live_pins={grenades.ActivePinCount}"));

        // --- 3. Pin out and still in the hand: safe indefinitely ---
        await Idle(tree, SafetySoakTicks);
        checks.Add(new StartupCheck(
            "held_live_grenade_never_explodes",
            grenades.Stage == GrenadeFuseStage.PinPulled &&
            grenades.DetonationCount == 0 &&
            !grenades.IsCountingDown &&
            lab.Grab.IsGrabbing && lab.Grab.CurrentGrab.Target == armed,
            $"stage={grenades.Stage} detonations={grenades.DetonationCount} " +
            $"counting={grenades.IsCountingDown} grabbed={lab.Grab.IsGrabbing} " +
            $"soak={SafetySoakTicks} ticks"));

        // --- 4. Let go: exactly 360 routed ticks, and re-grabbing does not stop it ---
        long releaseTick = 0;
        long blastTick = 0;
        int detonationsBefore = grenades.DetonationCount;
        bool protectedWhileLive = false;
        bool survivedEvictionPressure = false;
        int reGrabs = 0;

        await M4ObjectScenarioSupport.SetButton(
            tree, lab, hold, MouseButton.Left, pressed: false, 0);
        for (int tick = 0; tick < profile.FuseTicks + 240; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (releaseTick == 0 && grenades.IsCountingDown)
            {
                releaseTick = lab.Controls.RoutedPhysicsTicks;
                protectedWhileLive =
                    lab.Objects.TryGetSnapshot(armed.RuntimeId, out LooseObjectSnapshot live) &&
                    live.Protected;
                // Pile new objects onto a full registry while the fuse runs. The oldest
                // safe object goes; the live grenade must not, however old it is.
                survivedEvictionPressure = await SurviveEvictionPressure(tree, lab, armed);
            }

            // Pick it back up part way through: the countdown is the same countdown, and
            // it goes off in whoever's hand is holding it.
            if (releaseTick != 0 && reGrabs == 0 &&
                grenades.FuseTicksRemaining is > 0 and < 200 &&
                GodotObject.IsInstanceValid(armed))
            {
                await Grab(tree, lab, armed);
                reGrabs++;
            }

            if (grenades.DetonationCount > detonationsBefore)
            {
                blastTick = lab.Controls.RoutedPhysicsTicks;
                break;
            }
        }

        long fuseTicks = blastTick - releaseTick;
        checks.Add(new StartupCheck(
            "fuse_runs_360_ticks_from_release_and_a_regrab_does_not_stop_it",
            releaseTick > 0 &&
            blastTick > 0 &&
            fuseTicks == profile.FuseTicks &&
            reGrabs == 1 &&
            grenades.DetonationCount == detonationsBefore + 1,
            $"release_tick={releaseTick} blast_tick={blastTick} measured={fuseTicks} " +
            $"authored={profile.FuseTicks} regrabs={reGrabs}"));

        checks.Add(new StartupCheck(
            "live_grenade_is_never_evicted_and_frees_its_slot_when_it_goes_off",
            protectedWhileLive &&
            survivedEvictionPressure &&
            (!GodotObject.IsInstanceValid(armed) || armed!.RuntimeId == 0),
            $"protected={protectedWhileLive} survived_eviction={survivedEvictionPressure} " +
            $"runtime_id_after={(GodotObject.IsInstanceValid(armed) ? armed!.RuntimeId : 0)} " +
            $"registry={lab.Objects.Count} evictions={lab.Objects.EvictionCount}"));

        // --- 5. The blast: point-blank, and again at range ---
        BlastReading close = await MeasureBlast(
            tree, lab, grenades, () => lab.Buddy.Rig.Head.GlobalPosition);
        messages.Add(
            $"close blast: pain={close.TotalPain:F2} parts={close.ScoredParts} " +
            $"milli={close.TotalMilli} knockout={close.Knockout} shoved={close.ShovedBodies}");

        // Just inside the outer radius, where the falloff has nearly run out.
        BlastReading far = await MeasureBlast(
            tree,
            lab,
            grenades,
            () => lab.Buddy.Rig.Head.GlobalPosition +
                  new Vector2(0.0f, -(profile.BlastZeroRadiusPx - 25.0f)));
        messages.Add(
            $"far blast ({profile.BlastZeroRadiusPx - 25.0f:F0}px): pain={far.TotalPain:F2} " +
            $"parts={far.ScoredParts} knockout={far.Knockout}");

        // "Five solid aimed pistol bullets" — the gun plan measured one at 12.8–14.4 pain
        // on a square hit and 40.5–42.3 with the spin channel. The owner default anchors
        // to the strong reading, so the target band is five of those, and crossing the
        // 100-pain knockout window is the point rather than an accident.
        const float SolidBulletPainLow = 40.5f;
        const float SolidBulletPainHigh = 42.3f;
        float bandLow = SolidBulletPainLow * 5.0f * 0.75f;
        float bandHigh = SolidBulletPainHigh * 5.0f * 1.45f;
        checks.Add(new StartupCheck(
            "close_blast_scores_about_five_solid_bullets_and_knocks_the_buddy_out",
            close.TotalPain >= bandLow &&
            close.TotalPain <= bandHigh &&
            close.Knockout &&
            close.AllAttributedToGrenade &&
            close.TotalMilli > 0,
            $"pain={close.TotalPain:F2} band=[{bandLow:F1},{bandHigh:F1}] " +
            $"(5 x {SolidBulletPainLow}-{SolidBulletPainHigh} solid bullet) " +
            $"parts={close.ScoredParts} knockout={close.Knockout} " +
            $"milli={close.TotalMilli} attributed={close.AllAttributedToGrenade} " +
            $"placements={close.Placements}"));

        checks.Add(new StartupCheck(
            "blast_falloff_reduces_with_distance",
            far.TotalPain < close.TotalPain * 0.5f && far.ScoredParts <= close.ScoredParts,
            $"close={close.TotalPain:F2} over {close.ScoredParts} parts, " +
            $"far={far.TotalPain:F2} over {far.ScoredParts} parts, " +
            $"full={profile.BlastFullRadiusPx}px zero={profile.BlastZeroRadiusPx}px"));

        BlastReading held = await MeasureBlast(
            tree, lab, grenades, () => lab.Buddy.Rig.RightHand.GlobalPosition);
        messages.Add(
            $"held blast: pain={held.TotalPain:F2} hand={held.HandPain:F2} " +
            $"parts={held.ScoredParts} knockout={held.Knockout}");

        // A grenade in the buddy's hand is, to the falloff, a grenade at the hand's
        // position — so this measures the thing that actually decides the result. The
        // journey catches a live one for real.
        checks.Add(new StartupCheck(
            "buddy_holding_at_detonation_takes_the_close_range_result",
            held.HandPain > 0.0f &&
            held.TotalPain >= bandLow &&
            held.TotalPain <= bandHigh &&
            held.Knockout,
            $"hand_pain={held.HandPain:F2} total={held.TotalPain:F2} " +
            $"band=[{bandLow:F1},{bandHigh:F1}] parts={held.ScoredParts} " +
            $"knockout={held.Knockout} (head blast measured {close.TotalPain:F2})"));

        // --- 6. The blast moves objects; only the buddy is scored ---
        BlastReading witnessed = await MeasureBlast(
            tree,
            lab,
            grenades,
            () => lab.Buddy.Rig.Head.GlobalPosition,
            withWitnessObject: true);
        messages.Add(
            $"witness moved {witnessed.WitnessMovedPx:F2}px, " +
            $"non-grenade impacts={witnessed.NonGrenadeImpacts}");

        // The witness sits 30px from the centre — inside the full-effect radius, so the
        // falloff is 1 — and weighs exactly the profile's mass. Its speed on the way out
        // is therefore the authored shove itself, which is what the owner's "double it"
        // moved. How far it ends up is a story about which wall it met.
        float expectedWitnessSpeed = profile.ShoveImpulseAtCenter / lab.SafeObjectProfile.Mass;
        checks.Add(new StartupCheck(
            "blast_moves_objects_but_only_the_buddy_feels_pain",
            witnessed.WitnessMovedPx > 4.0f &&
            witnessed.WitnessPeakSpeedPx >= expectedWitnessSpeed * 0.85f &&
            witnessed.ShovedBodies > witnessed.ScoredParts &&
            witnessed.AllAttributedToGrenade &&
            witnessed.ScoredParts > 0,
            $"witness_moved={witnessed.WitnessMovedPx:F2}px " +
            $"witness_peak_speed={witnessed.WitnessPeakSpeedPx:F1}px/s " +
            $"(expected>={expectedWitnessSpeed * 0.85f:F1} from " +
            $"shove {profile.ShoveImpulseAtCenter} / mass {lab.SafeObjectProfile.Mass}) " +
            $"shoved={witnessed.ShovedBodies} " +
            $"scored_parts={witnessed.ScoredParts} " +
            $"non_grenade_impacts_on_the_blast_frame={witnessed.NonGrenadeImpacts} " +
            $"(what a shoved object hits afterwards is ordinary physics)"));

        // --- 7. Presentation: the medium kick, the ring, and the cues ---
        checks.Add(new StartupCheck(
            "kick_peaks_at_authored_medium_and_never_stacks",
            witnessed.KickCountDelta == 1 &&
            witnessed.KickPeakPx > 0.0f &&
            witnessed.KickPeakPx <= profile.KickAmplitudePx * 1.45f &&
            witnessed.RestartPeakPx <= profile.KickAmplitudePx * 1.45f &&
            witnessed.RestartKicks == 4 &&
            witnessed.CameraReturned,
            $"blast_kicks={witnessed.KickCountDelta} peak={witnessed.KickPeakPx:F3}px " +
            $"restart_kicks={witnessed.RestartKicks} restart_peak={witnessed.RestartPeakPx:F3}px " +
            $"authored={profile.KickAmplitudePx}px/{profile.KickDecayTicks}t " +
            $"bound={profile.KickAmplitudePx * 1.45f:F3}px camera_returned={witnessed.CameraReturned}"));

        float ringPeak = is3D
            ? lab.GrenadeVisual.PeakRingRadiusPx
            : lab.GrenadeVisualLegacy.PeakRingRadiusPx;
        checks.Add(new StartupCheck(
            "explosion_reads_at_the_blast_radius",
            witnessed.FlashSeen &&
            ringPeak >= profile.BlastFullRadiusPx * 0.9f &&
            ringPeak <= profile.BlastFullRadiusPx * 1.05f,
            $"mode={lab.Mode} flash_seen={witnessed.FlashSeen} ring_peak={ringPeak:F2}px " +
            $"full_radius={profile.BlastFullRadiusPx}px ring_ticks={profile.RingTicks}"));

        // The thud is gated so a grenade rolling along the floor stays quiet. Give the
        // spent room a long settle and confirm the counter does not creep.
        int thudsAfterBlasts = lab.GrenadeAudio.ThudCount;
        await Idle(tree, 240);
        checks.Add(new StartupCheck(
            "boom_and_thud_cues_fire_with_counters",
            lab.GrenadeAudio.BoomCount == grenades.DetonationCount &&
            lab.GrenadeAudio.BoomCount >= 5 &&
            lab.GrenadeAudio.ThudCount == thudsAfterBlasts &&
            lab.GrenadeAudio.GeneratedStreamCount == 2 &&
            lab.GrenadeAudio.PlayCount >= lab.GrenadeAudio.BoomCount,
            $"booms={lab.GrenadeAudio.BoomCount} detonations={grenades.DetonationCount} " +
            $"thuds={thudsAfterBlasts}->{lab.GrenadeAudio.ThudCount} " +
            $"(gate {profile.ThudMinImpactSpeed}px/s, {profile.ThudMinIntervalTicks}t) " +
            $"streams={lab.GrenadeAudio.GeneratedStreamCount} plays={lab.GrenadeAudio.PlayCount}"));

        messages.Add(
            $"fuse={fuseTicks} ticks; blast pain close={close.TotalPain:F2} " +
            $"far={far.TotalPain:F2}; kick peak={witnessed.KickPeakPx:F3}px");

        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>Everything one detonation is worth measuring.</summary>
    private readonly record struct BlastReading(
        float TotalPain,
        long TotalMilli,
        int ScoredParts,
        int Placements,
        float HandPain,
        bool Knockout,
        bool AllAttributedToGrenade,
        int NonGrenadeImpacts,
        int ShovedBodies,
        float WitnessMovedPx,
        float WitnessPeakSpeedPx,
        int KickCountDelta,
        float KickPeakPx,
        int RestartKicks,
        float RestartPeakPx,
        bool CameraReturned,
        bool FlashSeen);

    /// <summary>
    /// Spawns a grenade, arms it through the real chord, releases it, and puts it exactly
    /// where the measurement wants it on the tick before it goes off.
    ///
    /// <para>The teleport is deliberate. A live grenade spends its three seconds falling
    /// and rolling, and where it ends up depends on the buddy's walk — which would make
    /// this a measurement of the pose rather than of the blast. Placing it on the tick
    /// before detonation measures the falloff curve at a known distance, which is the
    /// authored quantity under test.</para>
    /// </summary>
    private static async Task<BlastReading> MeasureBlast(
        SceneTree tree,
        BuddyLab lab,
        GrenadeComponent grenades,
        System.Func<Vector2> blastPoint,
        bool withWitnessObject = false)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        var bench = new Vector2(room.Position.X + 90.0f, room.Position.Y + 70.0f);
        LooseObjectBody? grenade = await Spawn(tree, lab, bench);
        if (grenade is null)
            return default;

        // The spawn cleared the room; now wait out any knockout the previous leg left
        // running. A blast cannot trigger a knockout that is already in progress, and
        // measuring one against an unconscious buddy would read as the blast being weak.
        await RecoverBuddy(tree, lab);

        LooseObjectBody? witness = null;
        if (withWitnessObject)
        {
            witness = lab.SpawnLooseObject(lab.SafeObjectProfile, blastPoint() + WitnessOffset);
        }

        await Grab(tree, lab, grenade);
        Vector2 hold = grenade.GlobalPosition;
        await SetSecondary(tree, lab, hold, pressed: true);
        await M4ObjectScenarioSupport.WaitFor(tree, () => grenades.PinIsOut, 20);
        await SetSecondary(tree, lab, hold, pressed: false);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, hold, MouseButton.Left, pressed: false, 0);

        long totalMilli = 0;
        int grenadeImpacts = 0;
        int nonGrenadeImpacts = 0;
        float handPain = 0.0f;
        bool knockout = false;
        void OnImpact(AcceptedImpact impact)
        {
            if (impact.ContentId != ContentIds.ToolGrenade)
            {
                nonGrenadeImpacts++;
                return;
            }

            totalMilli += impact.MilliCredits;
            grenadeImpacts++;
            knockout |= impact.KnockoutTriggered;
            if (impact.Part is BuddyPart.LeftHand or BuddyPart.RightHand)
                handPain += impact.Pain;
        }

        // Exercise the kick's restart contract before the real one, the same way the
        // pistol scenario does: the blast is a single event, so non-stacking has to be
        // pinned against back-to-back restarts.
        Vector2 cameraBase = lab.Boundaries.WorldCamera.Position;
        lab.CameraKick.ResetPeak();
        int restartBefore = lab.CameraKick.KickCount;
        for (int restart = 0; restart < 4; restart++)
        {
            lab.CameraKick.Kick(grenades.Profile.KickAmplitudePx, grenades.Profile.KickDecayTicks);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        float restartPeak = lab.CameraKick.PeakOffsetPx;
        int restartKicks = lab.CameraKick.KickCount - restartBefore;
        await Idle(tree, grenades.Profile.KickDecayTicks + 8);
        bool cameraReturned =
            !lab.CameraKick.IsKicking &&
            lab.Boundaries.WorldCamera.Position.DistanceTo(cameraBase) < 0.001f;

        lab.Pipeline.ImpactAccepted += OnImpact;
        lab.CameraKick.ResetPeak();
        int kicksBefore = lab.CameraKick.KickCount;
        int detonationsBefore = grenades.DetonationCount;
        Vector2 witnessBefore = GodotObject.IsInstanceValid(witness)
            ? witness!.GlobalPosition
            : Vector2.Zero;
        bool flashSeen = false;
        int placements = 0;
        int blastFrameGrenadeImpacts = 0;
        int blastFrameNonGrenadeImpacts = 0;
        long blastMilli = 0;
        float blastHandPain = 0.0f;

        for (int tick = 0; tick < grenades.Profile.FuseTicks + 120; tick++)
        {
            // Re-placed on every one of the last few ticks, not once. A grenade dropped
            // inside a buddy part is deeply overlapping it, and the solver ejects it hard
            // in a single step — placing it only once would measure the blast wherever
            // that ejection happened to fling it.
            if (grenades.FuseTicksRemaining is > 0 and <= 4 &&
                GodotObject.IsInstanceValid(grenade))
            {
                grenade.GlobalPosition = blastPoint();
                grenade.LinearVelocity = Vector2.Zero;
                grenade.AngularVelocity = 0.0f;
                grenade.ResetPhysicsInterpolation();
                placements++;

                // The witness is placed for the same reason and on the same ticks. Left
                // where it was spawned it spends the fuse falling to the floor, and then
                // "how hard did the blast throw it" would be a question about the falloff
                // at wherever it rolled to, and about the floor holding it down. Held at a
                // known offset it is airborne, inside the full-effect radius, and the
                // impulse it leaves with is the authored shove.
                if (GodotObject.IsInstanceValid(witness))
                {
                    witness!.GlobalPosition = blastPoint() + WitnessOffset;
                    witness.LinearVelocity = Vector2.Zero;
                    witness.AngularVelocity = 0.0f;
                    witness.Sleeping = false;
                    witness.ResetPhysicsInterpolation();
                    witnessBefore = witness.GlobalPosition;
                }
            }

            int grenadeBefore = grenadeImpacts;
            int nonGrenadeBefore = nonGrenadeImpacts;
            long milliBefore = totalMilli;
            float handBefore = handPain;
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            flashSeen |= lab.Mode == PresentationMode.Mii3D
                ? lab.GrenadeVisual.IsFlashVisible
                : lab.GrenadeVisualLegacy.IsFlashVisible;
            if (grenades.DetonationCount > detonationsBefore)
            {
                // Narrowed to the frame the blast happened on. Anything a shoved object
                // hits afterwards is ordinary physics, not the explosion.
                blastFrameGrenadeImpacts = grenadeImpacts - grenadeBefore;
                blastFrameNonGrenadeImpacts = nonGrenadeImpacts - nonGrenadeBefore;
                blastMilli = totalMilli - milliBefore;
                blastHandPain = handPain - handBefore;
                break;
            }
        }

        // The blast's own contribution, straight off the shared curve, rather than
        // whatever else the pipeline scored in the same frame.
        float blastPain = grenades.LastBlastPain;
        int blastParts = grenades.LastBlastScoredParts;
        int shoved = grenades.LastBlastShovedBodies;
        // Let the shove actually move things before reading how far they went. The witness'
        // speed is sampled over the first few ticks, before it can reach a wall: how far it
        // ends up is a story about the room, but how hard it left is the authored shove.
        float witnessPeakSpeed = 0.0f;
        for (int tick = 0; tick < grenades.Profile.RingTicks + 20; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (tick < 4 && GodotObject.IsInstanceValid(witness))
            {
                witnessPeakSpeed = Mathf.Max(
                    witnessPeakSpeed, witness!.LinearVelocity.Length());
            }

            flashSeen |= lab.Mode == PresentationMode.Mii3D
                ? lab.GrenadeVisual.IsFlashVisible
                : lab.GrenadeVisualLegacy.IsFlashVisible;
        }

        float kickPeak = lab.CameraKick.PeakOffsetPx;
        int kickDelta = lab.CameraKick.KickCount - kicksBefore;
        float witnessMoved = GodotObject.IsInstanceValid(witness)
            ? witness!.GlobalPosition.DistanceTo(witnessBefore)
            : 0.0f;
        lab.Pipeline.ImpactAccepted -= OnImpact;

        return new BlastReading(
            blastPain,
            blastMilli,
            blastParts,
            placements,
            blastHandPain,
            knockout,
            blastFrameGrenadeImpacts == blastParts && blastFrameNonGrenadeImpacts == 0,
            blastFrameNonGrenadeImpacts,
            shoved,
            witnessMoved,
            witnessPeakSpeed,
            kickDelta,
            kickPeak,
            restartKicks,
            restartPeak,
            cameraReturned,
            flashSeen);
    }

    /// <summary>
    /// Fills the registry past its cap while the fuse runs. Every safe object is a
    /// candidate for the oldest-safe eviction; the live grenade must never be one.
    /// </summary>
    private static async Task<bool> SurviveEvictionPressure(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectBody grenade)
    {
        Rect2 room = lab.Boundaries.InnerBounds;
        for (int index = 0; index < LooseObjectRegistry.Capacity + 6; index++)
        {
            lab.SpawnLooseObject(
                lab.SafeObjectProfile,
                new Vector2(
                    room.Position.X + 20.0f + (index % 12) * 12.0f,
                    room.Position.Y + 20.0f + (index / 12) * 12.0f));
        }

        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        return GodotObject.IsInstanceValid(grenade) &&
               grenade.RuntimeId != 0 &&
               lab.Objects.EvictionCount > 0;
    }

    private static async Task<LooseObjectBody?> Spawn(SceneTree tree, BuddyLab lab, Vector2 at)
    {
        await M4ObjectScenarioSupport.MovePointer(tree, lab, at, 0);
        await M4ObjectScenarioSupport.SendKey(tree, Key.Key7);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Launcher.CurrentLaunchableContentId == ContentIds.ToolGrenade &&
                  lab.Grenades.Tracked is not null,
            20);
        return lab.Grenades.Tracked;
    }

    private static async Task Grab(SceneTree tree, BuddyLab lab, LooseObjectBody body)
    {
        Vector2 at = body.GlobalPosition;
        await M4ObjectScenarioSupport.MovePointer(tree, lab, at, 0);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, at, MouseButton.Left, pressed: true, MouseButtonMask.Left);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Grab.IsGrabbing && lab.Grab.CurrentGrab.Target == body,
            30);
    }

    private static async Task SetSecondary(
        SceneTree tree,
        BuddyLab lab,
        Vector2 at,
        bool pressed) =>
        await M4ObjectScenarioSupport.SetButton(
            tree,
            lab,
            at,
            MouseButton.Right,
            pressed,
            pressed
                ? MouseButtonMask.Left | MouseButtonMask.Right
                : MouseButtonMask.Left);

    /// <summary>Waits out any knockout window so the next blast starts from a fresh buddy.</summary>
    private static async Task RecoverBuddy(SceneTree tree, BuddyLab lab)
    {
        for (int tick = 0; tick < 900; tick++)
        {
            if (!lab.Pipeline.LastKnockoutState.KnockoutActive)
                break;
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        await Idle(tree, 30);
    }

    private static async Task Idle(SceneTree tree, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }

    private static PinBody? FindLivePin(GrenadeComponent grenades)
    {
        foreach (Node child in grenades.GetChildren())
        {
            if (child is PinBody { IsLive: true } pin)
                return pin;
        }

        return null;
    }
}
