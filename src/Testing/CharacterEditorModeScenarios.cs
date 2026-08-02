using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Persistence;
using DesktopBuddy.Platform;
using Godot;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.Testing;

internal static class CharacterEditorModeScenarioSupport
{
    public static async Task<(SandboxRoot Sandbox, ManualMonotonicTimeSource Time)?> Load(
        SceneTree tree)
    {
        var time = new ManualMonotonicTimeSource();
        (SandboxRoot Sandbox, InMemoryProgressStore Store)? loaded =
            await M4LifecycleScenarioSupport.Load(tree, time);
        return loaded is null ? null : (loaded.Value.Sandbox, time);
    }

    public static CharacterEditorModeCoordinator Coordinator(SandboxRoot sandbox) => new(
        sandbox.Window,
        sandbox.Shell,
        sandbox.Lifecycle);

    public static ScenarioResult Result(IReadOnlyList<StartupCheck> checks, ulong seed) =>
        new(checks.All(static check => check.Passed), checks, [$"seed={seed}"]);

    public static bool Contains(Rect2I outer, Rect2I inner) =>
        inner.Position.X >= outer.Position.X &&
        inner.Position.Y >= outer.Position.Y &&
        inner.End.X <= outer.End.X &&
        inner.End.Y <= outer.End.Y;
}

public sealed class EditorModeLifecycleAccountingScenario : IScenario
{
    public string Id => "editor_mode_lifecycle_accounting";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var loaded = await CharacterEditorModeScenarioSupport.Load(tree);
        if (loaded is null)
            return CharacterEditorModeScenarioSupport.Result(
                [new StartupCheck("a7_sandbox_loadable", false, "sandbox")], seed);

        (SandboxRoot sandbox, ManualMonotonicTimeSource time) = loaded.Value;
        try
        {
            var coordinator = CharacterEditorModeScenarioSupport.Coordinator(sandbox);
            long routedBefore = sandbox.Buddy.RoutedTicks;
            double acceptedBefore = sandbox.Lifecycle.AcceptedRunningSeconds;
            bool entered = coordinator.Enter();
            time.Advance(12.0);
            M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            bool frozen = entered && tree.Paused && sandbox.Lifecycle.IsEditorModeActive &&
                sandbox.Lifecycle.PauseCoordinator.Contains(GameplayPauseReason.CharacterEditor) &&
                sandbox.Lifecycle.AcceptedRunningSeconds == acceptedBefore &&
                sandbox.Buddy.RoutedTicks == routedBefore;
            checks.Add(new StartupCheck("a7_editor_freezes_gameplay_and_clock", frozen,
                $"paused={tree.Paused} accepted={acceptedBefore}->{sandbox.Lifecycle.AcceptedRunningSeconds} " +
                $"ticks={routedBefore}->{sandbox.Buddy.RoutedTicks}"));

            bool exited = coordinator.Exit();
            // Exiting resets the clock, so the first sample after it is a baseline that
            // awards nothing (the same re-anchoring every resume path uses).
            M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
            time.Advance(1.0);
            M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
            bool resumed = exited && !tree.Paused && !sandbox.Lifecycle.IsEditorModeActive &&
                sandbox.Lifecycle.AcceptedRunningSeconds > acceptedBefore;
            checks.Add(new StartupCheck("a7_editor_exit_resumes_accounting", resumed,
                $"accepted={sandbox.Lifecycle.AcceptedRunningSeconds}"));

            sandbox.SetHiddenToTray(true);
            coordinator.Enter();
            coordinator.Exit();
            bool nestedReason = tree.Paused && sandbox.Lifecycle.IsHiddenToTray &&
                sandbox.Lifecycle.PauseCoordinator.Contains(GameplayPauseReason.HiddenToTray) &&
                !sandbox.Lifecycle.PauseCoordinator.Contains(GameplayPauseReason.CharacterEditor);
            checks.Add(new StartupCheck("a7_editor_exit_preserves_other_pause_reason", nestedReason,
                $"paused={tree.Paused} hidden={sandbox.Lifecycle.IsHiddenToTray}"));
            sandbox.SetHiddenToTray(false);
        }
        finally
        {
            await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        }

        return CharacterEditorModeScenarioSupport.Result(checks, seed);
    }
}

public sealed class EditorWindowRestoreScenario : IScenario
{
    public string Id => "editor_window_restore";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var loaded = await CharacterEditorModeScenarioSupport.Load(tree);
        if (loaded is null)
            return CharacterEditorModeScenarioSupport.Result(
                [new StartupCheck("a7_sandbox_loadable", false, "sandbox")], seed);

        SandboxRoot sandbox = loaded.Value.Sandbox;
        try
        {
            var adapter = new EmulatedWindowsDesktopAdapter(
                [new Rect2I(0, 0, 1920, 1040)],
                transparencyAvailable: true);
            sandbox.Window.Configure(adapter);
            var initial = new WindowSettings(
                new Rect2I(200, 160, 540, 420),
                Transparent: true,
                AlwaysOnTop: true,
                MsaaLevel: 4,
                Vsync: false,
                Borderless: true,
                Resizable: false);
            sandbox.Window.ApplyWindowSettings(initial);
            sandbox.Window.SetInputMode(DomainInputMode.Work, sandbox.Shell.LastWorkModeHitRegions);
            var coordinator = CharacterEditorModeScenarioSupport.Coordinator(sandbox);

            coordinator.Enter();
            WindowSettings editor = sandbox.Window.CurrentSettings;
            bool editorState = editor.Rect.Size == CharacterEditorModeCoordinator.EditorClientSize &&
                !editor.Transparent && !editor.AlwaysOnTop && !editor.Borderless && editor.Resizable &&
                sandbox.Window.InputMode == DomainInputMode.Play;
            checks.Add(new StartupCheck("a7_editor_window_state", editorState,
                $"rect={editor.Rect} transparent={editor.Transparent} borderless={editor.Borderless}"));

            coordinator.Exit();
            WindowSettings restored = sandbox.Window.CurrentSettings;
            bool exact = restored == initial && sandbox.Window.InputMode == DomainInputMode.Work &&
                coordinator.EnterCount == 1 && coordinator.ExitCount == 1;
            checks.Add(new StartupCheck("a7_window_state_restored_exactly", exact,
                $"initial={initial} restored={restored}"));
        }
        finally
        {
            await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        }

        return CharacterEditorModeScenarioSupport.Result(checks, seed);
    }
}

public sealed class EditorWindowMonitorRemovedScenario : IScenario
{
    public string Id => "editor_window_monitor_removed";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var loaded = await CharacterEditorModeScenarioSupport.Load(tree);
        if (loaded is null)
            return CharacterEditorModeScenarioSupport.Result(
                [new StartupCheck("a7_sandbox_loadable", false, "sandbox")], seed);

        SandboxRoot sandbox = loaded.Value.Sandbox;
        try
        {
            Rect2I primary = new(0, 0, 1920, 1040);
            Rect2I secondary = new(1920, 0, 1600, 900);
            sandbox.Window.Configure(new EmulatedWindowsDesktopAdapter([primary, secondary]));
            WindowSettings captured = WindowSettings.Defaults with
            {
                Rect = new Rect2I(2600, 180, 640, 480),
            };
            sandbox.Window.ApplyWindowSettings(captured);
            var coordinator = CharacterEditorModeScenarioSupport.Coordinator(sandbox);
            coordinator.Enter();

            sandbox.Window.Configure(new EmulatedWindowsDesktopAdapter([primary]));
            coordinator.Exit();
            WindowSettings restored = sandbox.Window.CurrentSettings;
            bool recovered = CharacterEditorModeScenarioSupport.Contains(primary, restored.Rect) &&
                restored.Transparent == captured.Transparent &&
                restored.AlwaysOnTop == captured.AlwaysOnTop &&
                restored.Borderless == captured.Borderless &&
                restored.Resizable == captured.Resizable &&
                restored.MsaaLevel == captured.MsaaLevel &&
                restored.Vsync == captured.Vsync;
            checks.Add(new StartupCheck("a7_removed_monitor_recovers_window", recovered,
                $"captured={captured.Rect} restored={restored.Rect} primary={primary}"));
        }
        finally
        {
            await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        }

        return CharacterEditorModeScenarioSupport.Result(checks, seed);
    }
}

public sealed class EditorResizeBoundaryIsolationScenario : IScenario
{
    public string Id => "editor_resize_boundary_isolation";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var loaded = await CharacterEditorModeScenarioSupport.Load(tree);
        if (loaded is null)
            return CharacterEditorModeScenarioSupport.Result(
                [new StartupCheck("a7_sandbox_loadable", false, "sandbox")], seed);

        SandboxRoot sandbox = loaded.Value.Sandbox;
        try
        {
            var initial = WindowSettings.Defaults with
            {
                Rect = new Rect2I(120, 120, 600, 440),
            };
            sandbox.Window.ApplyWindowSettings(initial);
            var coordinator = CharacterEditorModeScenarioSupport.Coordinator(sandbox);
            int appliedBefore = sandbox.Boundaries.AppliedLayoutCount;
            coordinator.Enter();
            sandbox.Window.NotifyHeadlessClientBoundsChanged(new Rect2I(120, 120, 900, 650));
            sandbox.Window.NotifyHeadlessClientBoundsChanged(new Rect2I(120, 120, 1000, 700));
            sandbox.Window.NotifyHeadlessClientBoundsChanged(new Rect2I(120, 120, 820, 610));
            bool isolated = sandbox.Boundaries.AppliedLayoutCount == appliedBefore &&
                sandbox.Shell.EditorResizeObservationCount == 3;
            checks.Add(new StartupCheck("a7_editor_resize_does_not_rebuild_gameplay", isolated,
                $"layouts={appliedBefore}->{sandbox.Boundaries.AppliedLayoutCount} " +
                $"observed={sandbox.Shell.EditorResizeObservationCount}"));

            coordinator.Exit();
            int beforeRestoredTick = sandbox.Boundaries.AppliedLayoutCount;
            sandbox.Shell.PhysicsTick();
            sandbox.Boundaries.PhysicsTick();
            bool exactlyOne = sandbox.Boundaries.AppliedLayoutCount == beforeRestoredTick + 1 &&
                sandbox.Shell.RestoredBoundaryRequestCount == 1 &&
                sandbox.Boundaries.CurrentLayout.ClientWidth == initial.Rect.Size.X &&
                sandbox.Boundaries.CurrentLayout.ClientHeight == initial.Rect.Size.Y;
            checks.Add(new StartupCheck("a7_exit_applies_one_restored_boundary", exactlyOne,
                $"layouts={beforeRestoredTick}->{sandbox.Boundaries.AppliedLayoutCount} " +
                $"requests={sandbox.Shell.RestoredBoundaryRequestCount} " +
                $"layout={sandbox.Boundaries.CurrentLayout.ClientWidth}x{sandbox.Boundaries.CurrentLayout.ClientHeight}"));
        }
        finally
        {
            await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        }

        return CharacterEditorModeScenarioSupport.Result(checks, seed);
    }
}
