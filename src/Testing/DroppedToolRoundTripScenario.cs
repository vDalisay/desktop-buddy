using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Steam Demo DEMO-3 gate: equipped bat -> registered drop -> existing Grab tether -> throw ->
/// double-click hit resolution -> equipped bat. Also covers save-flush transience, rapid duplicate
/// re-equip, menu recall, and the strict no-op for an unowned dropped tool.
/// </summary>
public sealed class DroppedToolRoundTripScenario : IScenario
{
    public string Id => "dropped_tool_round_trip";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(tree, 10.0f, 500.0f);
        if (lab is null)
        {
            checks.Add(new StartupCheck("dropped_tool_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        var droppedTools = new DroppedToolInteractionComponent { Name = "DroppedToolRoundTrip" };
        lab.AddChild(droppedTools);
        droppedTools.Initialize(lab.Objects, lab.Pipeline, lab.CursorTools, lab.Grab, lab.Buddy);

        lab.Pipeline.SelectTool(ToolId.BaseballBat);
        lab.CursorTools.MoveCursor(new Vector2(180.0f, 110.0f));
        await Frames(tree, 8);
        CursorToolBody? equippedBat = lab.CursorTools.Body;
        checks.Add(new StartupCheck(
            "owned_bat_is_equipped_before_drop",
            lab.Progress.IsToolUnlocked(ContentIds.ToolBaseballBat) &&
            equippedBat is not null && equippedBat.ContentId == ContentIds.ToolBaseballBat,
            $"owned={lab.Progress.IsToolUnlocked(ContentIds.ToolBaseballBat)} body={equippedBat?.ContentId}"));

        bool dropped = droppedTools.TryDropSelected();
        await Frames(tree, 2);
        DroppedCursorToolBody? worldBat = droppedTools.FindDropped(ContentIds.ToolBaseballBat);
        CollisionShape2D? worldCollider = FindCollider(worldBat);
        checks.Add(new StartupCheck(
            "drop_registers_one_real_bat_and_selects_grab",
            dropped && worldBat is not null && worldBat.RuntimeId != 0 &&
            lab.Objects.FindBody(worldBat.RuntimeId) == worldBat &&
            lab.Pipeline.SelectedTool == ToolId.Grab &&
            lab.CursorTools.Body is null && worldCollider?.Shape is CapsuleShape2D,
            $"dropped={dropped} runtime={worldBat?.RuntimeId} selected={lab.Pipeline.SelectedTool} " +
            $"cursor_active={lab.CursorTools.IsActive} shape={worldCollider?.Shape?.GetType().Name}"));
        if (worldBat is null)
            return await Finish(tree, lab, checks, messages);

        await lab.Saves.FlushProgressAsync(force: true);
        checks.Add(new StartupCheck(
            "save_flush_keeps_world_body_transient_and_selection_consistent",
            lab.Pipeline.SelectedTool == ToolId.Grab &&
            worldBat.RuntimeId != 0 && lab.Objects.FindBody(worldBat.RuntimeId) == worldBat,
            $"selected={lab.Pipeline.SelectedTool} runtime={worldBat.RuntimeId} dirty={lab.Saves.IsDirty}"));

        Vector2 grabPoint = worldBat.GlobalPosition;
        bool grabbed = lab.Grab.TryGrab(worldBat, grabPoint);
        for (int tick = 0; tick < 18; tick++)
        {
            lab.Grab.MoveCursor(grabPoint + new Vector2((tick + 1) * 6.0f, 0.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        lab.Grab.Release(countsAsThrow: true);
        await Frames(tree, 4);
        checks.Add(new StartupCheck(
            "existing_grab_tether_picks_up_and_throws_dropped_bat",
            grabbed && !lab.Grab.IsGrabbing && worldBat.LinearVelocity.Length() > 1.0f &&
            worldBat.RuntimeId != 0 && lab.Objects.FindBody(worldBat.RuntimeId) == worldBat,
            $"grabbed={grabbed} speed={worldBat.LinearVelocity.Length():F2} runtime={worldBat.RuntimeId}"));

        // IntersectShape reads the real LooseObjects layer used by the shipping double-click bridge.
        await Frames(tree, 1);
        bool reequip = droppedTools.TryReequipAt(worldBat.GlobalPosition);
        bool duplicateReequip = droppedTools.TryReequip(worldBat);
        await Frames(tree, 2);
        checks.Add(new StartupCheck(
            "double_click_hit_re_equips_once_and_consumes_world_body",
            reequip && !duplicateReequip && lab.Pipeline.SelectedTool == ToolId.BaseballBat &&
            worldBat.RuntimeId == 0 &&
            droppedTools.FindDropped(ContentIds.ToolBaseballBat) is null &&
            lab.CursorTools.Body?.ContentId == ContentIds.ToolBaseballBat,
            $"reequip={reequip} duplicate={duplicateReequip} selected={lab.Pipeline.SelectedTool} " +
            $"runtime={worldBat.RuntimeId} cursor={lab.CursorTools.Body?.ContentId}"));

        // A normal Tools-panel/hotkey selection while a copy is on the floor is treated as recall,
        // so an entitlement can never become one equipped copy plus one stale world copy.
        bool secondDrop = droppedTools.TryDropSelected();
        await Frames(tree, 2);
        DroppedCursorToolBody? recalledBat = droppedTools.FindDropped(ContentIds.ToolBaseballBat);
        int recalledRuntime = recalledBat?.RuntimeId ?? 0;
        lab.Pipeline.SelectTool(ToolId.BaseballBat);
        await Frames(tree, 2);
        checks.Add(new StartupCheck(
            "normal_tool_selection_recalls_existing_world_copy",
            secondDrop && recalledRuntime != 0 &&
            lab.Pipeline.SelectedTool == ToolId.BaseballBat &&
            droppedTools.FindDropped(ContentIds.ToolBaseballBat) is null &&
            lab.Objects.FindBody(recalledRuntime) is null,
            $"drop={secondDrop} old_runtime={recalledRuntime} selected={lab.Pipeline.SelectedTool}"));

        // A gun is not a cursor-tethered body, so it drops at the pointer through its authored
        // drop-only form instead (owner report 2026-08-19: "D does not drop any of the guns").
        lab.Progress.Adopt(new BuddyProgressState(
            lab.Progress.CashPerPain,
            unlockedToolIds: [ContentIds.ToolGrab, ContentIds.ToolPistol],
            selectedToolId: ContentIds.ToolPistol).Snapshot());
        lab.Pipeline.SelectTool(ToolId.Pistol);
        await Frames(tree, 2);
        var gunDropPoint = new Vector2(240.0f, 130.0f);
        bool gunDropped = droppedTools.TryDropSelected(gunDropPoint);
        await Frames(tree, 2);
        DroppedCursorToolBody? worldPistol = droppedTools.FindDropped(ContentIds.ToolPistol);
        checks.Add(new StartupCheck(
            "drop_key_puts_the_selected_gun_on_the_floor_at_the_pointer",
            gunDropped && worldPistol is not null && worldPistol.RuntimeId != 0 &&
            lab.Objects.FindBody(worldPistol.RuntimeId) == worldPistol &&
            lab.Pipeline.SelectedTool == ToolId.Grab,
            $"dropped={gunDropped} runtime={worldPistol?.RuntimeId} " +
            $"selected={lab.Pipeline.SelectedTool} position={worldPistol?.GlobalPosition}"));

        bool gunReequip = worldPistol is not null && droppedTools.TryReequip(worldPistol);
        await Frames(tree, 2);
        checks.Add(new StartupCheck(
            "dropped_gun_re_equips_back_into_the_gun_component",
            gunReequip && lab.Pipeline.SelectedTool == ToolId.Pistol &&
            droppedTools.FindDropped(ContentIds.ToolPistol) is null,
            $"reequip={gunReequip} selected={lab.Pipeline.SelectedTool}"));

        // Ownership is the final authority. Build a stale/unowned floor body deliberately and
        // prove the re-equip transaction leaves both persistent selection and registry state alone.
        lab.Progress.Adopt(new BuddyProgressState(
            lab.Progress.CashPerPain,
            unlockedToolIds: [ContentIds.ToolGrab],
            selectedToolId: ContentIds.ToolGrab).Snapshot());
        CursorToolProfile? batProfile = FindProfile(lab, ContentIds.ToolBaseballBat);
        DroppedCursorToolBody? unowned = null;
        bool unownedRegistered = false;
        if (batProfile is not null && batProfile.WorldDrop is not null)
        {
            unowned = new DroppedCursorToolBody { Name = "UnownedDroppedBat" };
            unowned.Configure(batProfile);
            droppedTools.AddChild(unowned);
            unowned.GlobalPosition = new Vector2(220.0f, 120.0f);
            unownedRegistered = lab.Objects.TryRegister(unowned, batProfile.WorldDrop, out _);
        }
        bool unownedReequip = unowned is not null && droppedTools.TryReequip(unowned);
        checks.Add(new StartupCheck(
            "unowned_or_stale_drop_is_strict_no_op",
            unowned is not null && unownedRegistered && !unownedReequip &&
            lab.Pipeline.SelectedTool == ToolId.Grab && unowned.RuntimeId != 0 &&
            lab.Objects.FindBody(unowned.RuntimeId) == unowned,
            $"registered={unownedRegistered} reequip={unownedReequip} selected={lab.Pipeline.SelectedTool} " +
            $"runtime={unowned?.RuntimeId}"));
        if (unowned is not null && GodotObject.IsInstanceValid(unowned))
        {
            if (unowned.RuntimeId != 0)
                lab.Objects.Unregister(unowned);
            unowned.QueueFree();
        }

        return await Finish(tree, lab, checks, messages);
    }

    private static CursorToolProfile? FindProfile(BuddyLab lab, string contentId)
    {
        foreach (CursorToolProfile? profile in lab.CursorTools.Profiles)
            if (profile is not null && GodotObject.IsInstanceValid(profile) && profile.ContentId == contentId)
                return profile;
        return null;
    }

    private static CollisionShape2D? FindCollider(DroppedCursorToolBody? body)
    {
        if (body is null)
            return null;
        foreach (Node child in body.GetChildren())
            if (child is CollisionShape2D collider)
                return collider;
        return null;
    }

    private static async Task Frames(SceneTree tree, int count)
    {
        for (int index = 0; index < count; index++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }

    private static async Task<ScenarioResult> Finish(
        SceneTree tree,
        BuddyLab lab,
        List<StartupCheck> checks,
        List<string> messages)
    {
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        if (GodotObject.IsInstanceValid(lab))
            lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new ScenarioResult(passed, checks, messages);
    }
}
