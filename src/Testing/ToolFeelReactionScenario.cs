using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// End-to-end semantics for the owner-confirmed M3 tool-feel slice: readable
/// rotating face, care hands/progress, Tickle escalation, responsive physical
/// glove, speed-scaled pain, braced-hand absorption, bypass, and hit-stop.
/// </summary>
public sealed class ToolFeelReactionScenario : IScenario
{
    private const int ThreeSeconds = 3 * 120;
    private const int EightSeconds = 8 * 120;

    public string Id => "tool_feel_reactions";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("tool_feel_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab careLab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(careLab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        var head = careLab.Buddy.Rig.Head;
        head.SetFace(":)");
        head.Freeze = true;
        head.GlobalRotation = 0.9f;
        float sidewaysFaceWorldRotation = Mathf.Wrap(
            head.GlobalRotation + head.FaceDrawRotation,
            -Mathf.Pi,
            Mathf.Pi);
        head.SetFace("x_x");
        float frontFacingFaceWorldRotation = Mathf.Wrap(
            head.GlobalRotation + head.FaceDrawRotation,
            -Mathf.Pi,
            Mathf.Pi);
        checks.Add(new StartupCheck("sideways_ascii_face_is_rotated_into_world_upright_basis",
            Math.Abs(head.GlobalRotation) > 0.8f &&
            Math.Abs(Mathf.Wrap(sidewaysFaceWorldRotation - Mathf.Pi * 0.5f, -Mathf.Pi, Mathf.Pi)) < 0.001f &&
            PuppetPartBody.UsesSidewaysAsciiLayout(">:("),
            $"head={head.GlobalRotation:F3} world={sidewaysFaceWorldRotation:F3}"));
        checks.Add(new StartupCheck("front_facing_ascii_art_faces_keep_zero_world_rotation",
            Math.Abs(frontFacingFaceWorldRotation) < 0.001f &&
            !PuppetPartBody.UsesSidewaysAsciiLayout("x_x") &&
            !PuppetPartBody.UsesSidewaysAsciiLayout("o_o") &&
            !PuppetPartBody.UsesSidewaysAsciiLayout(">_<") &&
            !PuppetPartBody.UsesSidewaysAsciiLayout("^_^"),
            $"head={head.GlobalRotation:F3} world={frontFacingFaceWorldRotation:F3}"));
        head.Freeze = false;

        careLab.CareCursor.SetPointerState(ToolId.Pet, head.GlobalPosition, true);
        checks.Add(new StartupCheck("care_hand_follows_cursor_instantly",
            careLab.CareCursor.IsHandVisible &&
            careLab.CareCursor.GlobalPosition.IsEqualApprox(head.GlobalPosition),
            $"visible={careLab.CareCursor.IsHandVisible} error={careLab.CareCursor.GlobalPosition.DistanceTo(head.GlobalPosition):F3}"));
        careLab.CareCursor.SetPointerState(ToolId.Pet, head.GlobalPosition, false);

        careLab.Pipeline.SelectTool(ToolId.Pet);
        int firstFavoriteSelection = careLab.CareStroke.FavoriteSelectionCount;
        await RubHeadTicks(tree, careLab, ThreeSeconds);
        checks.Add(new StartupCheck("pet_rub_uses_hidden_dual_gate_and_smiles",
            careLab.Pipeline.CareAwardCount == 1 &&
            careLab.Pipeline.PetDistanceProgress < 0.001 &&
            careLab.Pipeline.PetValidSecondsProgress < 1.0 / 120.0 &&
            careLab.Reactions.CurrentFace == ":)",
            $"awards={careLab.Pipeline.CareAwardCount} face={careLab.Reactions.CurrentFace}"));
        int petSmileFrames = 0;
        while (petSmileFrames < 100 && careLab.Reactions.PetSmileTicksRemaining > 0)
        {
            petSmileFrames++;
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        checks.Add(new StartupCheck("pet_completion_smile_lasts_three_quarters_second",
            petSmileFrames is >= 88 and <= 92,
            $"frames={petSmileFrames} expected=90"));

        careLab.Pipeline.SelectTool(ToolId.Tickle);
        foreach (PuppetPartBody part in careLab.Buddy.Rig.Parts)
        {
            part.Freeze = true;
            part.LinearVelocity = Vector2.Zero;
            part.AngularVelocity = 0.0f;
        }
        int friendlyHops = await TickleHeadTicks(tree, careLab, ThreeSeconds);
        checks.Add(new StartupCheck("tickle_first_three_seconds_is_friendly",
            careLab.Pipeline.CareAwardCount == 2 &&
            careLab.Pipeline.TickleDisposition == TickleDisposition.Friendly &&
            friendlyHops >= 2,
            $"awards={careLab.Pipeline.CareAwardCount} disposition={careLab.Pipeline.TickleDisposition} hops={friendlyHops}"));

        int secondFriendlyHops = await TickleHeadTicks(tree, careLab, ThreeSeconds);
        checks.Add(new StartupCheck("tickle_turns_angry_after_six_seconds",
            careLab.Pipeline.CareAwardCount == 3 &&
            careLab.Pipeline.TickleDisposition == TickleDisposition.Angry &&
            careLab.Reactions.IsTickleAnnoyed &&
            careLab.ToolReactions.IsTickleFleeing,
            $"awards={careLab.Pipeline.CareAwardCount} face={careLab.Reactions.CurrentFace} fleeing={careLab.ToolReactions.IsTickleFleeing} hops={secondFriendlyHops}"));

        int angryHops = await TickleHeadTicks(tree, careLab, ThreeSeconds);
        checks.Add(new StartupCheck("angry_tickle_reduces_mood_and_hops_faster",
            careLab.Pipeline.CarePenaltyCount == 1 && angryHops >= 4 &&
            careLab.ToolReactions.IsTickleFleeing,
            $"penalties={careLab.Pipeline.CarePenaltyCount} hops={angryHops} fleeing={careLab.ToolReactions.IsTickleFleeing}"));

        careLab.CareStroke.SetStroke(false, Vector2.Zero);
        for (int tick = 0; tick < EightSeconds - 1; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool remainedAngryBeforeCooldown = careLab.Pipeline.TickleDisposition == TickleDisposition.Angry;
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("tickle_anger_resets_after_eight_second_cooldown",
            remainedAngryBeforeCooldown &&
            careLab.Pipeline.TickleDisposition == TickleDisposition.Friendly,
            $"before={remainedAngryBeforeCooldown} after={careLab.Pipeline.TickleDisposition}"));
        foreach (PuppetPartBody part in careLab.Buddy.Rig.Parts)
            part.Freeze = false;

        careLab.Pipeline.SelectTool(ToolId.Pet);
        checks.Add(new StartupCheck("pet_favorite_randomizes_each_selection",
            firstFavoriteSelection == 1 && careLab.CareStroke.FavoriteSelectionCount == 2,
            $"first={firstFavoriteSelection} total={careLab.CareStroke.FavoriteSelectionCount} spot={careLab.CareStroke.FavoritePart}"));

        PuppetPartBody favoritePart = ResolvePart(careLab, careLab.CareStroke.FavoritePart);
        favoritePart.Freeze = true;
        Vector2 favoritePoint = favoritePart.GlobalPosition;
        Vector2 favoriteViewport = careLab.GetViewport().GetCanvasTransform() * favoritePoint;
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Position = favoriteViewport,
            GlobalPosition = favoriteViewport,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventAction
        {
            Action = InputActions.Primary,
            Pressed = true,
            Strength = 1.0f,
        });
        for (int frame = 0; frame < 30 && careLab.CareCursor.SparkleEmissionCount == 0; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        checks.Add(new StartupCheck("favorite_pet_contact_sparkles_on_hand",
            careLab.CareCursor.IsFavoriteSparkleActive &&
            careLab.CareCursor.ActiveSparkleCount > 0 &&
            careLab.CareCursor.SparkleEmissionCount > 0,
            $"active={careLab.CareCursor.IsFavoriteSparkleActive} particles={careLab.CareCursor.ActiveSparkleCount} emitted={careLab.CareCursor.SparkleEmissionCount}"));
        Input.ParseInputEvent(new InputEventAction
        {
            Action = InputActions.Primary,
            Pressed = false,
            Strength = 0.0f,
        });
        for (int frame = 0;
             frame < 30 && (careLab.CareCursor.IsFavoriteSparkleActive || careLab.CareCursor.ActiveSparkleCount > 0);
             frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        checks.Add(new StartupCheck("favorite_sparkles_stop_without_held_contact",
            !careLab.CareCursor.IsFavoriteSparkleActive && careLab.CareCursor.ActiveSparkleCount == 0,
            $"active={careLab.CareCursor.IsFavoriteSparkleActive} particles={careLab.CareCursor.ActiveSparkleCount}"));
        favoritePart.Freeze = false;
        careLab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        BuddyLab? gloveLab = await ScenarioSteps.CreateControlledImpactLab(tree, 10.0f, 500.0f);
        if (gloveLab is null)
        {
            checks.Add(new StartupCheck("glove_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        gloveLab.Pipeline.SelectTool(ToolId.BoxingGlove);
        gloveLab.CursorTools.MoveCursor(new Vector2(60.0f, 60.0f));
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        gloveLab.CursorTools.MoveCursor(new Vector2(160.0f, 60.0f));
        for (int tick = 0; tick < 12; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        float gloveLag = gloveLab.CursorTools.Body?.GlobalPosition.DistanceTo(new Vector2(160.0f, 60.0f)) ?? float.PositiveInfinity;
        checks.Add(new StartupCheck("boxing_glove_tracks_cursor_promptly",
            gloveLag <= 40.0f,
            $"lag={gloveLag:F2}px after 0.1s"));

        gloveLab.Pointer.NotifyPointerExitedPlayArea();
        bool despawnedImmediately = !gloveLab.CursorTools.IsActive;
        for (int tick = 0; tick < 3; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool stayedDespawnedOutside = !gloveLab.CursorTools.IsActive;
        checks.Add(new StartupCheck("boxing_glove_despawns_when_pointer_leaves_play_area",
            gloveLab.Pipeline.SelectedTool == ToolId.BoxingGlove &&
            !gloveLab.CursorTools.HasCursor && despawnedImmediately && stayedDespawnedOutside,
            $"selected={gloveLab.Pipeline.SelectedTool} hasCursor={gloveLab.CursorTools.HasCursor} immediate={despawnedImmediately} active={gloveLab.CursorTools.IsActive}"));
        Rect2 gloveBounds = gloveLab.Boundaries.InnerBounds;
        gloveLab.CursorTools.MoveCursor(gloveBounds.Position - new Vector2(100.0f, 100.0f));
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        float gloveInset = gloveLab.CursorTools.ActiveProfile!.Radius + gloveLab.CursorTools.ActiveProfile!.WallClearance;
        Vector2 edgeSafeMinimum = gloveBounds.Position + new Vector2(gloveInset, gloveInset);
        checks.Add(new StartupCheck("boxing_glove_respawns_clear_of_room_edges",
            gloveLab.CursorTools.HasCursor && gloveLab.CursorTools.IsActive &&
            gloveLab.CursorTools.Cursor.IsEqualApprox(edgeSafeMinimum) &&
            gloveLab.CursorTools.Body is { } respawnedGlove &&
            respawnedGlove.GlobalPosition.X >= edgeSafeMinimum.X - 0.25f &&
            respawnedGlove.GlobalPosition.Y >= edgeSafeMinimum.Y - 0.25f &&
            respawnedGlove.GlobalPosition.X <= gloveBounds.End.X - gloveInset + 0.25f &&
            respawnedGlove.GlobalPosition.Y <= gloveBounds.End.Y - gloveInset + 0.25f,
            $"cursor={gloveLab.CursorTools.Cursor} minimum={edgeSafeMinimum} glove={gloveLab.CursorTools.Body?.GlobalPosition}"));
        gloveLab.CursorTools.MoveCursor(new Vector2(160.0f, 60.0f));
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        AcceptedImpact? slow = await ScenarioSteps.StrikePartAtSpeed(
            tree, gloveLab, gloveLab.Buddy.Rig.Torso, 250.0f);
        AcceptedImpact? fast = await ScenarioSteps.StrikePartAtSpeed(
            tree, gloveLab, gloveLab.Buddy.Rig.Torso, 900.0f);
        checks.Add(new StartupCheck("glove_speed_increases_impulse_and_pain",
            slow is not null && fast is not null &&
            fast.Value.RelativeSpeed > slow.Value.RelativeSpeed &&
            fast.Value.RawImpulse > slow.Value.RawImpulse &&
            fast.Value.Pain > slow.Value.Pain,
            $"slow(v={slow?.RelativeSpeed:F1},i={slow?.RawImpulse:F1},p={slow?.Pain:F2}) fast(v={fast?.RelativeSpeed:F1},i={fast?.RawImpulse:F1},p={fast?.Pain:F2})"));

        Vector2 protectedCenter = (gloveLab.Buddy.Rig.Head.GlobalPosition + gloveLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        gloveLab.CursorTools.MoveCursor(protectedCenter);
        for (int tick = 0; tick < 30 && !gloveLab.ToolReactions.IsDefending; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("learned_harm_raises_real_hand_guard",
            gloveLab.Pipeline.IsToolHarmful(ToolId.BoxingGlove) &&
            gloveLab.ToolReactions.IsDefending &&
            gloveLab.Buddy.CurrentDriveIntent.GuardActive,
            $"harmful={gloveLab.Pipeline.IsToolHarmful(ToolId.BoxingGlove)} defending={gloveLab.ToolReactions.IsDefending}"));

        CursorToolBody? physicalGlove = gloveLab.CursorTools.Body;
        if (physicalGlove is not null)
        {
            physicalGlove.CollisionLayer = 0;
            physicalGlove.CollisionMask = 0;
            physicalGlove.Freeze = true;
        }

        foreach (PuppetPartBody part in gloveLab.Buddy.Rig.Parts)
            part.LinearVelocity = Vector2.Zero;
        Vector2 defenseCenter =
            (gloveLab.Buddy.Rig.Head.GlobalPosition + gloveLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        Vector2 threatRight = defenseCenter + Vector2.Right * 100.0f;
        gloveLab.CursorTools.MoveCursor(threatRight);
        if (physicalGlove is not null) physicalGlove.GlobalPosition = threatRight;
        float fleeStartX = gloveLab.Buddy.Rig.Torso.GlobalPosition.X;
        for (int tick = 0; tick < 24; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        Vector2 guardBeforeTurn = gloveLab.ToolReactions.GuardDirection;
        Vector2 centerBeforeTurn =
            (gloveLab.Buddy.Rig.Head.GlobalPosition + gloveLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        Vector2 desiredBeforeTurn = (gloveLab.CursorTools.Cursor - centerBeforeTurn).Normalized();
        float fleeDeltaX = gloveLab.Buddy.Rig.Torso.GlobalPosition.X - fleeStartX;
        float nearestHandToGlove = physicalGlove is null
            ? float.PositiveInfinity
            : Math.Min(
                gloveLab.Buddy.Rig.LeftHand.GlobalPosition.DistanceTo(physicalGlove.GlobalPosition),
                gloveLab.Buddy.Rig.RightHand.GlobalPosition.DistanceTo(physicalGlove.GlobalPosition));
        checks.Add(new StartupCheck("defending_buddy_flees_instead_of_chasing_glove",
            fleeDeltaX < -2.0f && nearestHandToGlove > 18.0f &&
            gloveLab.Buddy.CurrentDriveIntent.WalkDirection < 0.0f,
            $"fleeDx={fleeDeltaX:F2} nearestHandGlove={nearestHandToGlove:F2} walk={gloveLab.Buddy.CurrentDriveIntent.WalkDirection:F1}"));

        Vector2 currentCenter = (gloveLab.Buddy.Rig.Head.GlobalPosition + gloveLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        Vector2 threatLeft = currentCenter - desiredBeforeTurn * 100.0f;
        gloveLab.CursorTools.MoveCursor(threatLeft);
        if (physicalGlove is not null) physicalGlove.GlobalPosition = threatLeft;
        foreach (PuppetPartBody part in gloveLab.Buddy.Rig.Parts)
            part.LinearVelocity = Vector2.Zero;
        float rightFleeStartX = gloveLab.Buddy.Rig.Torso.GlobalPosition.X;
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        Vector2 guardAfterOneTick = gloveLab.ToolReactions.GuardDirection;
        float guardReach = gloveLab.ToolReactions.GuardCenter.DistanceTo(
            (gloveLab.Buddy.Rig.Head.GlobalPosition + gloveLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f);
        Vector2 guardNetForce =
            gloveLab.Buddy.ActiveDrive.LastLeftGuardForce +
            gloveLab.Buddy.ActiveDrive.LastRightGuardForce +
            gloveLab.Buddy.ActiveDrive.LastGuardReactionForce;
        checks.Add(new StartupCheck("guard_lag_aims_at_pointer_without_glove_attachment",
            guardBeforeTurn.Dot(desiredBeforeTurn) > 0.75f &&
            guardAfterOneTick.Dot(desiredBeforeTurn) > 0.50f &&
            Math.Abs(guardReach - gloveLab.ToolReactions.Profile.GuardReach) <= 1.0f &&
            guardNetForce.Length() <= 0.1f,
            $"aligned={guardBeforeTurn.Dot(desiredBeforeTurn):F2} retained={guardAfterOneTick.Dot(desiredBeforeTurn):F2} reach={guardReach:F2} net={guardNetForce.Length():F3}"));

        for (int tick = 1; tick < 24; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        float rightFleeDeltaX = gloveLab.Buddy.Rig.Torso.GlobalPosition.X - rightFleeStartX;
        float leftFleeDistance = -fleeDeltaX;
        float rightFleeDistance = rightFleeDeltaX;
        float fleeParity = Math.Min(leftFleeDistance, rightFleeDistance) /
                           Math.Max(0.001f, Math.Max(leftFleeDistance, rightFleeDistance));
        checks.Add(new StartupCheck("glove_defense_flees_both_directions_without_ambient_countermand",
            leftFleeDistance > 8.0f && rightFleeDistance > 8.0f && fleeParity >= 0.5f &&
            gloveLab.Buddy.CurrentDriveIntent.WalkDirection > 0.0f,
            $"left={leftFleeDistance:F2} right={rightFleeDistance:F2} parity={fleeParity:F2} walk={gloveLab.Buddy.CurrentDriveIntent.WalkDirection:F1}"));

        bool stayedDefensiveInBand = true;
        float hysteresisDistance =
            (gloveLab.ToolReactions.Profile.DefenseRange + gloveLab.ToolReactions.Profile.DefenseReleaseRange) * 0.5f;
        for (int tick = 0; tick < 30; tick++)
        {
            Vector2 center = (gloveLab.Buddy.Rig.Head.GlobalPosition + gloveLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
            Vector2 inBand = center + Vector2.Right * hysteresisDistance;
            gloveLab.CursorTools.MoveCursor(inBand);
            if (physicalGlove is not null) physicalGlove.GlobalPosition = inBand;
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            stayedDefensiveInBand &= gloveLab.ToolReactions.IsDefending;
        }

        Vector2 releaseCenter = (gloveLab.Buddy.Rig.Head.GlobalPosition + gloveLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        Vector2 beyondRelease = releaseCenter + Vector2.Right * (gloveLab.ToolReactions.Profile.DefenseReleaseRange + 5.0f);
        gloveLab.CursorTools.MoveCursor(beyondRelease);
        if (physicalGlove is not null) physicalGlove.GlobalPosition = beyondRelease;
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool releasedOutside = !gloveLab.ToolReactions.IsDefending;

        Vector2 reentryCenter = (gloveLab.Buddy.Rig.Head.GlobalPosition + gloveLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        Vector2 bandReentry = reentryCenter + Vector2.Right * hysteresisDistance;
        gloveLab.CursorTools.MoveCursor(bandReentry);
        if (physicalGlove is not null) physicalGlove.GlobalPosition = bandReentry;
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool stayedCalmInBand = !gloveLab.ToolReactions.IsDefending;

        Vector2 reacquireCenter = (gloveLab.Buddy.Rig.Head.GlobalPosition + gloveLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        Vector2 insideDefense = reacquireCenter + Vector2.Right * (gloveLab.ToolReactions.Profile.DefenseRange - 5.0f);
        gloveLab.CursorTools.MoveCursor(insideDefense);
        if (physicalGlove is not null) physicalGlove.GlobalPosition = insideDefense;
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        checks.Add(new StartupCheck("glove_defense_uses_enter_exit_hysteresis_without_face_thrashing",
            stayedDefensiveInBand && releasedOutside && stayedCalmInBand && gloveLab.ToolReactions.IsDefending,
            $"held={stayedDefensiveInBand} released={releasedOutside} calmBand={stayedCalmInBand} reacquired={gloveLab.ToolReactions.IsDefending}"));

        int absorptionBefore = gloveLab.Buddy.ActiveDrive.GuardAbsorptionCount;
        AcceptedImpact? guarded = await ScenarioSteps.StrikePartAtSpeed(
            tree, gloveLab, gloveLab.Buddy.Rig.LeftHand, 400.0f);
        checks.Add(new StartupCheck("guarded_hand_hit_absorbs_half",
            guarded is not null && guarded.Value.Guarded &&
            Math.Abs(guarded.Value.Impulse - guarded.Value.RawImpulse * 0.5f) <= 0.01f &&
            gloveLab.Buddy.ActiveDrive.GuardAbsorptionCount == absorptionBefore + 1 &&
            Math.Abs(gloveLab.Buddy.ActiveDrive.LastGuardCounterImpulse.Length() - guarded.Value.RawImpulse * 0.5f) <= 0.05f,
            $"guarded={guarded?.Guarded} raw={guarded?.RawImpulse:F2} accepted={guarded?.Impulse:F2} counter={gloveLab.Buddy.ActiveDrive.LastGuardCounterImpulse.Length():F2}"));

        Vector2 bypassCenter =
            (gloveLab.Buddy.Rig.Head.GlobalPosition + gloveLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        gloveLab.CursorTools.MoveCursor(bypassCenter + Vector2.Right * 100.0f);
        for (int tick = 0; tick < 30 && gloveLab.ToolReactions.GuardDirection.X < 0.8f; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        AcceptedImpact? bypass = await ScenarioSteps.StrikePartAtSpeed(
            tree, gloveLab, gloveLab.Buddy.Rig.Torso, 400.0f);
        checks.Add(new StartupCheck("fast_bypass_around_hands_is_unmitigated",
            bypass is not null && bypass.Value.Part == BuddyPart.Torso && !bypass.Value.Guarded &&
            Math.Abs(bypass.Value.Impulse - bypass.Value.RawImpulse) <= 0.01f,
            $"guarded={bypass?.Guarded} raw={bypass?.RawImpulse:F2} accepted={bypass?.Impulse:F2}"));
        messages.Add($"gloveLag={gloveLag:F2} slowPain={slow?.Pain:F2} fastPain={fast?.Pain:F2}");
        gloveLab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        BuddyLab? hitStopLab = await ScenarioSteps.CreateControlledImpactLab(tree, 100.0f);
        if (hitStopLab is null)
        {
            checks.Add(new StartupCheck("hit_stop_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        AcceptedImpact? maximumHit = await ScenarioSteps.StrikePart(tree, hitStopLab, hitStopLab.Buddy.Rig.Torso);
        float markerDistance = maximumHit is null
            ? float.PositiveInfinity
            : hitStopLab.ImpactFeedback.LastImpactWorldPoint.DistanceTo(hitStopLab.Buddy.Rig.Torso.GlobalPosition);
        Vector2 markerRoundTrip = hitStopLab.ImpactFeedback.GlobalTransform * hitStopLab.ImpactFeedback.LastImpactLocalPoint;
        checks.Add(new StartupCheck("impact_feedback_uses_solver_world_contact",
            maximumHit is not null &&
            hitStopLab.ImpactFeedback.LastImpactWorldPoint.IsEqualApprox(maximumHit.Value.Point) &&
            markerDistance <= hitStopLab.Buddy.Rig.Torso.Radius + 16.0f &&
            markerRoundTrip.DistanceTo(maximumHit.Value.Point) <= 0.01f,
            $"point={maximumHit?.Point} marker={hitStopLab.ImpactFeedback.LastImpactWorldPoint} bodyDistance={markerDistance:F2}"));

        float midpointScale = DesktopBuddy.Buddy.Presentation.ImpactFeedbackPresenter.EvaluateHitStopScale(
            hitStopLab.ImpactFeedback.Profile.HitStopScale, 0.5);
        checks.Add(new StartupCheck("hit_stop_keeps_early_envelope_visibly_slow",
            midpointScale <= 0.30f &&
            midpointScale > hitStopLab.ImpactFeedback.Profile.HitStopScale &&
            DesktopBuddy.Buddy.Presentation.ImpactFeedbackPresenter.EvaluateHitStopScale(
                hitStopLab.ImpactFeedback.Profile.HitStopScale, 1.0) >= 0.999f,
            $"midpoint={midpointScale:F3} start={hitStopLab.ImpactFeedback.Profile.HitStopScale:F3}"));
        int triggersAfterFirst = hitStopLab.ImpactFeedback.HitStopTriggerCount;
        AcceptedImpact? stackedHit = await ScenarioSteps.StrikePart(tree, hitStopLab, hitStopLab.Buddy.Rig.Head);
        checks.Add(new StartupCheck("maximum_hit_starts_nonstacking_hit_stop",
            maximumHit is not null && maximumHit.Value.Pain >= 99.99f &&
            hitStopLab.ImpactFeedback.IsHitStopActive &&
            triggersAfterFirst == 1 && hitStopLab.ImpactFeedback.HitStopTriggerCount == 1 &&
            stackedHit is not null,
            $"pain={maximumHit?.Pain:F1} active={hitStopLab.ImpactFeedback.IsHitStopActive} triggers={hitStopLab.ImpactFeedback.HitStopTriggerCount}"));

        for (int frame = 0; frame < 20_000 && hitStopLab.ImpactFeedback.IsHitStopActive; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        checks.Add(new StartupCheck("hit_stop_eases_back_to_previous_speed",
            !hitStopLab.ImpactFeedback.IsHitStopActive &&
            Math.Abs(Engine.TimeScale - 1.0) < 0.001,
            $"active={hitStopLab.ImpactFeedback.IsHitStopActive} scale={Engine.TimeScale:F3}"));

        hitStopLab.Pipeline.SelectTool(ToolId.BoxingGlove);
        Vector2 knockoutCenter =
            (hitStopLab.Buddy.Rig.Head.GlobalPosition + hitStopLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        hitStopLab.CursorTools.MoveCursor(knockoutCenter - Vector2.Right * 100.0f);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        CursorToolBody? knockoutGlove = hitStopLab.CursorTools.Body;
        if (knockoutGlove is not null)
        {
            knockoutGlove.CollisionLayer = 0;
            knockoutGlove.CollisionMask = 0;
            knockoutGlove.Freeze = true;
        }

        bool stayedPassiveWhileUnconscious = true;
        for (int tick = 0;
             tick < 600 && hitStopLab.Buddy.CurrentConsciousness != Consciousness.Conscious;
             tick++)
        {
            Vector2 center =
                (hitStopLab.Buddy.Rig.Head.GlobalPosition + hitStopLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
            Vector2 threat = center - Vector2.Right * 100.0f;
            hitStopLab.CursorTools.MoveCursor(threat);
            if (knockoutGlove is not null) knockoutGlove.GlobalPosition = threat;
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            stayedPassiveWhileUnconscious &= !hitStopLab.Buddy.ActiveDrive.ActiveOutputsEnabled;
        }

        bool wokeNaturally = hitStopLab.Buddy.CurrentConsciousness == Consciousness.Conscious;
        foreach (PuppetPartBody part in hitStopLab.Buddy.Rig.Parts)
            part.LinearVelocity = Vector2.Zero;
        float postKnockoutStartX = hitStopLab.Buddy.Rig.Torso.GlobalPosition.X;
        for (int tick = 0; tick < 48; tick++)
        {
            Vector2 center =
                (hitStopLab.Buddy.Rig.Head.GlobalPosition + hitStopLab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
            Vector2 threat = center - Vector2.Right * 100.0f;
            hitStopLab.CursorTools.MoveCursor(threat);
            if (knockoutGlove is not null) knockoutGlove.GlobalPosition = threat;
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        float postKnockoutFleeDistance =
            hitStopLab.Buddy.Rig.Torso.GlobalPosition.X - postKnockoutStartX;
        checks.Add(new StartupCheck("post_knockout_buddy_flees_promptly_from_glove_on_left",
            stayedPassiveWhileUnconscious && wokeNaturally &&
            hitStopLab.ToolReactions.IsDefending &&
            hitStopLab.Buddy.CurrentDriveIntent.WalkDirection > 0.0f &&
            postKnockoutFleeDistance > 8.0f,
            $"passive={stayedPassiveWhileUnconscious} woke={wokeNaturally} defending={hitStopLab.ToolReactions.IsDefending} walk={hitStopLab.Buddy.CurrentDriveIntent.WalkDirection:F1} dx={postKnockoutFleeDistance:F2}"));

        hitStopLab.QueueFree();

        // Owner regression 2026-07-24: persistent harmful memory must not pin the
        // visible startle face forever after the glove pointer leaves the window.
        BuddyLab? faceTailLab = await ScenarioSteps.CreateControlledImpactLab(tree, 10.0f, 500.0f);
        if (faceTailLab is null)
        {
            checks.Add(new StartupCheck(
                "learned_glove_face_reverts_five_seconds_after_pointer_exit",
                false,
                "face-tail lab failed to compose"));
        }
        else
        {
            faceTailLab.Pipeline.SelectTool(ToolId.BoxingGlove);
            faceTailLab.CursorTools.MoveCursor(faceTailLab.Buddy.Rig.Head.GlobalPosition);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            _ = await ScenarioSteps.StrikePartAtSpeed(
                tree,
                faceTailLab,
                faceTailLab.Buddy.Rig.Torso,
                500.0f);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

            faceTailLab.Pointer.NotifyPointerExitedPlayArea();
            int threatTailTicks = (int)Math.Round(
                faceTailLab.Reactions.Profile.LearnedThreatFaceTailSeconds *
                Engine.PhysicsTicksPerSecond,
                MidpointRounding.AwayFromZero);
            bool startleSeenDuringTail = false;
            for (int tick = 0; tick < threatTailTicks - 1; tick++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
                startleSeenDuringTail |= faceTailLab.Reactions.CurrentFace == "o_o";
            }
            bool tailHeldUntilLastTick =
                faceTailLab.Reactions.LearnedThreatFaceTicksRemaining == 1;
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            bool faceRevertedAfterTail =
                faceTailLab.Reactions.LearnedThreatFaceTicksRemaining == 0 &&
                faceTailLab.Reactions.CurrentFace != "o_o";
            checks.Add(new StartupCheck("learned_glove_face_reverts_five_seconds_after_pointer_exit",
                startleSeenDuringTail && tailHeldUntilLastTick && faceRevertedAfterTail,
                $"seen={startleSeenDuringTail} before={tailHeldUntilLastTick} " +
                $"remaining={faceTailLab.Reactions.LearnedThreatFaceTicksRemaining} " +
                $"face={faceTailLab.Reactions.CurrentFace} ticks={threatTailTicks}"));
            faceTailLab.QueueFree();
        }

        bool passed = AllPassed(checks);
        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task RubHeadTicks(SceneTree tree, BuddyLab lab, int ticks)
    {
        long target = lab.CareStroke.ValidContactTicks + ticks;
        for (int iteration = 0; iteration < ticks + 8 && lab.CareStroke.ValidContactTicks < target; iteration++)
        {
            float offset = iteration % 2 == 0 ? -8.0f : 8.0f;
            lab.CareStroke.SetStroke(true, lab.Buddy.Rig.Head.GlobalPosition + Vector2.Right * offset);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        lab.CareStroke.SetStroke(false, Vector2.Zero);
    }

    private static async Task<int> TickleHeadTicks(SceneTree tree, BuddyLab lab, int ticks)
    {
        int hops = 0;
        long target = lab.CareStroke.ValidContactTicks + ticks;
        for (int iteration = 0; iteration < ticks + 8 && lab.CareStroke.ValidContactTicks < target; iteration++)
        {
            lab.CareStroke.SetStroke(
                true, lab.CareStroke.PointerForContactAt(lab.Buddy.Rig.Head.GlobalPosition));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.CareStroke.TickleHopRequested) hops++;
        }
        lab.CareStroke.SetStroke(false, Vector2.Zero);
        return hops;
    }

    private static bool AllPassed(IReadOnlyList<StartupCheck> checks)
    {
        foreach (StartupCheck check in checks)
            if (!check.Passed) return false;
        return true;
    }

    private static PuppetPartBody ResolvePart(BuddyLab lab, BuddyPart part) => part switch
    {
        BuddyPart.Torso => lab.Buddy.Rig.Torso,
        BuddyPart.Head => lab.Buddy.Rig.Head,
        BuddyPart.LeftHand => lab.Buddy.Rig.LeftHand,
        BuddyPart.RightHand => lab.Buddy.Rig.RightHand,
        BuddyPart.LeftFoot => lab.Buddy.Rig.LeftFoot,
        BuddyPart.RightFoot => lab.Buddy.Rig.RightFoot,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
    };

}
