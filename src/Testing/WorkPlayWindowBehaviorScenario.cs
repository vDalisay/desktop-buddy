using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Platform;
using DesktopBuddy.Work;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Focused gate for the compact/full-screen Work/Play redesign and the cursor-tool bug that
/// motivated it.
/// </summary>
public sealed class WorkPlayWindowBehaviorScenario : IScenario
{
    public string Id => "work_play_window_behavior";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var loaded = await CharacterEditorModeScenarioSupport.Load(tree);
        if (loaded is null)
        {
            return new ScenarioResult(false,
                [new StartupCheck("work_play_sandbox_loadable", false, "sandbox")],
                [$"seed={seed}"]);
        }

        SandboxRoot sandbox = loaded.Value.Sandbox;
        try
        {
            var adapter = new EmulatedWindowsDesktopAdapter(
                [new Rect2I(0, 0, 1920, 1040)],
                transparencyAvailable: true);
            sandbox.Window.Configure(adapter);
            Rect2I compactRect = new(120, 160, 640, 480);
            sandbox.Window.ApplyWindowSettings(WindowSettings.Defaults with { Rect = compactRect });
            sandbox.Window.TrySetLayoutMode(WindowLayoutMode.Compact);
            sandbox.Window.SetInputMode(InputMode.Work, Array.Empty<Rect2I>());

            checks.Add(new StartupCheck(
                "compact_work_captures_entire_client",
                adapter.PlayModeCaptured &&
                !sandbox.Window.MainWindowMousePassthrough &&
                sandbox.Window.InputMode == InputMode.Work,
                $"native_capture={adapter.PlayModeCaptured} " +
                $"passthrough={sandbox.Window.MainWindowMousePassthrough} " +
                $"mode={sandbox.Window.InputMode}"));

            var bridge = new GameplayInputModeBridge
            {
                Name = "ScenarioGameplayInputModeBridge",
            };
            bridge.Configure(sandbox);
            sandbox.AddChild(bridge);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            sandbox.Progress.Unlock(ContentIds.ToolBaseballBat);
            sandbox.Pipeline.SelectTool(ToolId.BaseballBat);
            bool selectedInWork = sandbox.Pipeline.SelectedTool == ToolId.BaseballBat &&
                !bridge.GameplayInputEnabled &&
                !sandbox.CursorTools.IsActive;
            checks.Add(new StartupCheck(
                "work_mode_never_spawns_selected_bat",
                selectedInWork,
                $"selected={sandbox.Pipeline.SelectedTool} enabled={bridge.GameplayInputEnabled} active={sandbox.CursorTools.IsActive}"));

            sandbox.Shell._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = new Vector2(360, 260),
            });
            bool activationConsumed = sandbox.Shell.Mode == InputMode.Play &&
                bridge.GameplayInputEnabled &&
                !sandbox.Pointer.IsPrimaryHeld &&
                !sandbox.CursorTools.IsActive;
            checks.Add(new StartupCheck(
                "compact_activation_click_is_consumed",
                activationConsumed,
                $"mode={sandbox.Shell.Mode} primary={sandbox.Pointer.IsPrimaryHeld} active={sandbox.CursorTools.IsActive}"));

            sandbox.Pointer._Input(new InputEventMouseMotion { Position = new Vector2(420, 280) });
            sandbox.Pointer.ResolvePendingInput();
            sandbox.CursorTools.PhysicsTick(1.0 / 120.0);
            bool spawnedAfterFreshMotion = sandbox.CursorTools.IsActive &&
                sandbox.CursorTools.ActiveContentId == ContentIds.ToolBaseballBat;
            checks.Add(new StartupCheck(
                "play_spawns_bat_only_after_fresh_motion",
                spawnedAfterFreshMotion,
                $"active={sandbox.CursorTools.IsActive} content={sandbox.CursorTools.ActiveContentId}"));

            sandbox.Shell.ToggleInteractionMode();
            bool workClearedTool = sandbox.Shell.Mode == InputMode.Work &&
                !bridge.GameplayInputEnabled &&
                !sandbox.CursorTools.IsActive &&
                sandbox.Pipeline.SelectedTool == ToolId.BaseballBat;
            checks.Add(new StartupCheck(
                "work_releases_tool_but_preserves_selection",
                workClearedTool,
                $"mode={sandbox.Shell.Mode} active={sandbox.CursorTools.IsActive} selected={sandbox.Pipeline.SelectedTool}"));

            bool enteredFullscreen = sandbox.Window.TrySetLayoutMode(
                WindowLayoutMode.FullscreenOverlay,
                0);
            bool fullscreenWorkPassthrough = enteredFullscreen &&
                sandbox.Window.LayoutMode == WindowLayoutMode.FullscreenOverlay &&
                sandbox.Window.MainWindowMousePassthrough &&
                adapter.PlayModeCaptured &&
                adapter.LastWorkModeHitRegions.Count == 0;
            checks.Add(new StartupCheck(
                "fullscreen_work_passes_entire_main_window_through",
                fullscreenWorkPassthrough,
                $"layout={sandbox.Window.LayoutMode} " +
                $"passthrough={sandbox.Window.MainWindowMousePassthrough} " +
                $"legacy_regions={adapter.LastWorkModeHitRegions.Count}"));

            sandbox.Shell.ToggleInteractionMode();
            bool fullscreenPlayCapture = sandbox.Shell.Mode == InputMode.Play &&
                !sandbox.Window.MainWindowMousePassthrough &&
                adapter.PlayModeCaptured;
            checks.Add(new StartupCheck(
                "fullscreen_play_captures_monitor",
                fullscreenPlayCapture,
                $"mode={sandbox.Shell.Mode} passthrough={sandbox.Window.MainWindowMousePassthrough}"));

            sandbox.Shell.ToggleInteractionMode();
            bool fullscreenWorkRestored = sandbox.Shell.Mode == InputMode.Work &&
                sandbox.Window.MainWindowMousePassthrough;
            checks.Add(new StartupCheck(
                "fullscreen_toggle_restores_work_passthrough",
                fullscreenWorkRestored,
                $"mode={sandbox.Shell.Mode} passthrough={sandbox.Window.MainWindowMousePassthrough}"));

            sandbox.Window.TrySetLayoutMode(WindowLayoutMode.Compact, 0);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool compactRestored = sandbox.Window.LayoutMode == WindowLayoutMode.Compact &&
                sandbox.Window.CompactRect == compactRect &&
                sandbox.Window.CurrentSettings.Rect == compactRect &&
                !sandbox.Window.MainWindowMousePassthrough &&
                adapter.PlayModeCaptured;
            checks.Add(new StartupCheck(
                "layout_roundtrip_restores_compact_rect",
                compactRestored,
                $"layout={sandbox.Window.LayoutMode} compact={sandbox.Window.CompactRect} " +
                $"current={sandbox.Window.CurrentSettings.Rect} " +
                $"passthrough={sandbox.Window.MainWindowMousePassthrough}"));

            var unavailable = new EmulatedWindowsDesktopAdapter(
                [new Rect2I(0, 0, 1920, 1040)],
                transparencyAvailable: false);
            sandbox.Window.Configure(unavailable);
            bool refused = !sandbox.Window.TrySetLayoutMode(
                WindowLayoutMode.FullscreenOverlay,
                0) && sandbox.Window.LayoutMode == WindowLayoutMode.Compact;
            checks.Add(new StartupCheck(
                "fullscreen_overlay_refused_without_transparency",
                refused,
                $"available={sandbox.Window.FullscreenOverlayAvailable} layout={sandbox.Window.LayoutMode}"));

            var workView = new WorkCompanionView { Name = "ScenarioWorkCompanionView" };
            workView.Configure(sandbox, showLifetime: false, animationsEnabled: true);
            tree.Root.AddChild(workView);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var computer = workView.FindChild("WorkComputerArt", true, false) as TextureRect;
            var counter = workView.FindChild("WorkCrtCounter", true, false) as Control;
            var rig = workView.FindChild("WorkBuddyRig", true, false) as BuddyVisualRigView;
            bool compositionBuilt = GodotObject.IsInstanceValid(computer) &&
                computer!.Position.IsEqualApprox(new Vector2(200, -70)) &&
                computer.Size.IsEqualApprox(new Vector2(520, 520)) &&
                GodotObject.IsInstanceValid(counter) &&
                counter!.Position.IsEqualApprox(new Vector2(396, 91)) &&
                counter.Size.IsEqualApprox(new Vector2(137, 104)) &&
                GodotObject.IsInstanceValid(rig);
            checks.Add(new StartupCheck(
                "work_companion_uses_supplied_pc_and_screen_counter",
                compositionBuilt,
                $"computer={computer?.GetRect()} counter={counter?.GetRect()} rig={rig is not null}"));

            if (GodotObject.IsInstanceValid(rig))
            {
                Node3D torso = rig!.GetPartSocket(BuddyPartId.Torso);
                Node3D leftHand = rig.GetPartSocket(BuddyPartId.LeftHand);
                Node3D rightHand = rig.GetPartSocket(BuddyPartId.RightHand);
                float restingLeftY = leftHand.GlobalPosition.Y;
                bool sidewaysContactPose = Mathf.IsEqualApprox(
                        torso.GlobalRotation.Y,
                        Mathf.Pi / 6.0f) &&
                    leftHand.GlobalPosition.X > torso.GlobalPosition.X &&
                    rightHand.GlobalPosition.X > leftHand.GlobalPosition.X &&
                    leftHand.GlobalPosition.Y < torso.GlobalPosition.Y &&
                    rightHand.GlobalPosition.Y < torso.GlobalPosition.Y;

                workView.NotifyActivity(WorkActivityKind.KeyboardPress);
                bool subtleHandBob = leftHand.GlobalPosition.Y > restingLeftY &&
                    Mathf.IsEqualApprox(leftHand.GlobalPosition.Y - restingLeftY, 3.0f);
                checks.Add(new StartupCheck(
                    "work_companion_keeps_sideways_contact_pose_and_small_hand_bob",
                    sidewaysContactPose && subtleHandBob,
                    $"yaw={torso.GlobalRotation.Y} left={leftHand.GlobalPosition} " +
                    $"right={rightHand.GlobalPosition} bob={leftHand.GlobalPosition.Y - restingLeftY}"));
            }
            else
            {
                checks.Add(new StartupCheck(
                    "work_companion_keeps_sideways_contact_pose_and_small_hand_bob",
                    false,
                    "work rig missing"));
            }

            workView.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        finally
        {
            await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }
}
