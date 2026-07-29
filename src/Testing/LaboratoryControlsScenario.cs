using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Laboratory;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Real-input regression for pause, single step, speed, seed, and consciousness controls.</summary>
public sealed class LaboratoryControlsScenario : IScenario
{
    public string Id => "laboratory_controls";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("laboratory_controls_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);

        lab.TelemetryPanel.RefreshNow();
        LaboratoryTelemetrySnapshot telemetry = lab.TelemetryPanel.Snapshot;
        bool telemetryComposed = lab.TelemetryPanel.IsInitialized &&
                                 lab.BoundaryVisualizer.IsInitialized &&
                                 telemetry.AutonomySeed == seed &&
                                 telemetry.RoomWidth >= 360.0 &&
                                 telemetry.RoomHeight >= 270.0 &&
                                 lab.TelemetryPanel.InstructionsLabel.Text.Contains("PHYSICS LAB", System.StringComparison.Ordinal);
        checks.Add(new StartupCheck(
            "lab_guidance_and_telemetry_composed",
            telemetryComposed,
            $"seed={telemetry.AutonomySeed} room={telemetry.RoomWidth:F0}x{telemetry.RoomHeight:F0}"));

        await SendKey(tree, Key.H);
        bool panelHidden = !lab.TelemetryPanel.Visible;
        await SendKey(tree, Key.H);
        bool panelRestored = lab.TelemetryPanel.Visible;
        checks.Add(new StartupCheck(
            "lab_help_panel_toggles_from_real_input",
            panelHidden && panelRestored,
            $"hidden={panelHidden} restored={panelRestored}"));

        await SendKey(tree, Key.P);
        long pausedAtTick = lab.Controls.RoutedPhysicsTicks;
        Vector2 pausedPosition = lab.Buddy.Rig.Torso.GlobalPosition;
        for (int frame = 0; frame < 10; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        bool pauseHeld = lab.Controls.IsPaused &&
                         lab.Controls.RoutedPhysicsTicks == pausedAtTick &&
                         lab.Buddy.Rig.Torso.GlobalPosition.IsEqualApprox(pausedPosition);
        checks.Add(new StartupCheck(
            "lab_pause_freezes_simulation",
            pauseHeld,
            $"paused={lab.Controls.IsPaused} routed_delta={lab.Controls.RoutedPhysicsTicks - pausedAtTick}"));

        await SendKey(tree, Key.Period);
        for (int frame = 0; frame < 3; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        bool steppedOnce = lab.Controls.IsPaused &&
                           lab.Controls.RoutedPhysicsTicks == pausedAtTick + 1;
        checks.Add(new StartupCheck(
            "lab_single_step_routes_exactly_one_tick",
            steppedOnce,
            $"routed_delta={lab.Controls.RoutedPhysicsTicks - pausedAtTick}"));

        await SendKey(tree, Key.U);
        await SendKey(tree, Key.Period);
        for (int frame = 0; frame < 3; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        bool manualUnconscious = lab.Buddy.CurrentConsciousness == Consciousness.Unconscious &&
                                 !lab.Buddy.ActiveDrive.ActiveOutputsEnabled;
        checks.Add(new StartupCheck(
            "lab_manual_consciousness_uses_real_profile",
            manualUnconscious,
            $"state={lab.Buddy.CurrentConsciousness} active={lab.Buddy.ActiveDrive.ActiveOutputsEnabled}"));

        ulong priorSeed = lab.Controls.AutonomySeed;
        await SendKey(tree, Key.U, shiftPressed: true);
        bool reseeded = lab.Controls.AutonomySeed == priorSeed + 1 &&
                        lab.Buddy.AutonomousMotion.Seed == priorSeed + 1;
        checks.Add(new StartupCheck(
            "lab_seed_control_reseeds_behavior_stream",
            reseeded,
            $"seed={lab.Controls.AutonomySeed} last_key={lab.Controls.LastControlKey}"));

        await SendKey(tree, Key.Key1);
        bool slowed = Mathf.IsEqualApprox((float)lab.Controls.TimeScale, 0.25f) &&
                       Mathf.IsEqualApprox((float)Engine.TimeScale, 0.25f);
        await SendKey(tree, Key.Key3);
        bool restoredSpeed = Mathf.IsEqualApprox((float)lab.Controls.TimeScale, 1.0f) &&
                             Mathf.IsEqualApprox((float)Engine.TimeScale, 1.0f);
        checks.Add(new StartupCheck(
            "lab_slow_motion_changes_engine_time_scale",
            slowed && restoredSpeed,
            $"slowed={slowed} restored={restoredSpeed}"));

        await SendKey(tree, Key.P);
        for (int frame = 0; frame < 3; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        bool resumed = !lab.Controls.IsPaused && lab.Controls.RoutedPhysicsTicks > pausedAtTick + 2;
        checks.Add(new StartupCheck(
            "lab_resume_restores_fixed_tick_routing",
            resumed,
            $"paused={lab.Controls.IsPaused} routed={lab.Controls.RoutedPhysicsTicks}"));

        // O / Shift+O are the only way a human can put a loose object into the room and
        // take it back out again — the Eat key puts food straight into the hand — so the
        // owner gate's catch/toss/obstacle-hop steps depend on this key existing.
        int objectsBefore = lab.Objects.Count;
        await SendKey(tree, Key.O);
        int afterFirst = lab.Objects.Count;
        // One ball at a time: a second drop replaces the first rather than littering the room.
        await SendKey(tree, Key.O);
        int objectsAfterSpawn = lab.Objects.Count;
        float roomFloor = lab.Boundaries.InnerBounds.End.Y;
        bool insideRoom = true;
        for (int index = 0; index < lab.GetChildCount(); index++)
        {
            if (lab.GetChild(index) is DesktopBuddy.Objects.LooseObjectBody spawned)
            {
                insideRoom &= spawned.GlobalPosition.Y <= roomFloor &&
                    lab.Boundaries.InnerBounds.HasPoint(spawned.GlobalPosition);
            }
        }

        await SendKey(tree, Key.O, shiftPressed: true);
        int objectsAfterClear = lab.Objects.Count;
        checks.Add(new StartupCheck(
            "lab_spawns_one_ball_at_a_time_and_clears",
            afterFirst == objectsBefore + 1 && objectsAfterSpawn == afterFirst &&
            insideRoom && objectsAfterClear == 0,
            $"before={objectsBefore} after_first={afterFirst} after_second={objectsAfterSpawn} " +
            $"inside={insideRoom} cleared={objectsAfterClear}"));

        // Regression for the exact M4 owner-gate failure: the gate launches
        // buddy_lab.tscn, but the hide command used to exist only in SandboxRoot.
        // Drive the real Ctrl+Shift+H input action and prove it reaches the shared
        // lifecycle path. Restore through the command seam because an invisible,
        // unfocused window cannot receive the same in-app chord (the documented M6
        // native dependency).
        await SendKey(tree, Key.H, shiftPressed: true, ctrlPressed: true);
        int requestsAfterHotkey = lab.TrayCommands.HideShowRequestCount;
        bool lifecycleHiddenAfterHotkey = lab.Lifecycle.IsHiddenToTray;
        bool treePausedAfterHotkey = tree.Paused;
        bool windowVisibleAfterHotkey = lab.WindowAdapterVisibleForTests;
        bool hiddenFromRealInput =
            requestsAfterHotkey == 1 &&
            lifecycleHiddenAfterHotkey &&
            treePausedAfterHotkey &&
            !windowVisibleAfterHotkey;
        lab.SetHiddenToTray(false);
        bool restoredThroughCommandSeam =
            !lab.Lifecycle.IsHiddenToTray &&
            !tree.Paused &&
            lab.WindowAdapterVisibleForTests;
        checks.Add(new StartupCheck(
            "lab_hide_to_tray_routes_real_hotkey",
            hiddenFromRealInput && restoredThroughCommandSeam,
            $"requests={requestsAfterHotkey} lifecycle_hidden={lifecycleHiddenAfterHotkey} " +
            $"tree_paused={treePausedAfterHotkey} window_visible_after_hotkey={windowVisibleAfterHotkey} " +
            $"restored_window_visible={lab.WindowAdapterVisibleForTests} restored={restoredThroughCommandSeam}"));

        lab.Controls.SetTimeScale(1.0);
        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task SendKey(
        SceneTree tree,
        Key key,
        bool shiftPressed = false,
        bool ctrlPressed = false)
    {
        Input.ParseInputEvent(new InputEventKey
        {
            PhysicalKeycode = key,
            ShiftPressed = shiftPressed,
            CtrlPressed = ctrlPressed,
            Pressed = true,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventKey
        {
            PhysicalKeycode = key,
            ShiftPressed = shiftPressed,
            CtrlPressed = ctrlPressed,
            Pressed = false,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}
