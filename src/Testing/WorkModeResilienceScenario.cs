using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using DesktopBuddy.Platform;
using DesktopBuddy.UI.Win98;
using DesktopBuddy.Work;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>WM4/WM5 ownership, journal, suspend, low-cost, and teardown gate.</summary>
public sealed class WorkModeResilienceScenario : IScenario
{
    public string Id => "work_mode_resilience";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
        if (packed?.Instantiate() is not SandboxRoot sandbox)
            return new ScenarioResult(false,
                [new StartupCheck("work_sandbox_loadable", false, "sandbox")],
                [$"seed={seed}"]);

        var progress = new BuddyProgressState(
            sandbox.Pipeline.RequirePainProfile().CashPerPain);
        var work = new WorkProgressState();
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store, work: work);
        var context = new RunContext(
            progress,
            new EconomyService(progress, CatalogueLoader.Catalogue),
            store,
            saves,
            new LocalSettingsSave(),
            SaveLoadStatus.NewSave,
            WorkProgress: work);
        sandbox.Configure(context);
        sandbox.Shell.ConfigureRuntime(context.Settings, saves);
        tree.Root.AddChild(sandbox);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        var adapter = new EmulatedWindowsDesktopAdapter(
            [new Rect2I(0, 0, 1920, 1040)],
            transparencyAvailable: true);
        sandbox.Window.Configure(adapter);
        var source = new ScenarioWorkActivitySource();
        var milestones = new WorkMilestoneCatalogue([
            new WorkMilestoneDefinition(
                "work.scenario.session.2",
                WorkCounterKind.TotalActions,
                WorkMilestoneScope.CurrentSession,
                2,
                5_000,
                WorkMilestoneRepeatPolicy.RepeatPerSession),
        ]);
        var coordinator = new WorkCompanionCoordinator
        {
            Name = nameof(WorkCompanionCoordinator),
        };
        coordinator.Configure(sandbox, context, () => source, milestones);
        sandbox.AddChild(coordinator);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        try
        {
            Rect2I normalRect = sandbox.Window.CompactRect;
            bool entered = await coordinator.EnterAsync();
            checks.Add(new StartupCheck(
                "work_first_click_unlocks_without_equipping",
                entered && work.FirstEntryGlassesGranted &&
                    progress.IsToolUnlocked(ContentIds.CosmeticWorkGlasses),
                $"entered={entered} flag={work.FirstEntryGlassesGranted} " +
                $"owned={progress.IsToolUnlocked(ContentIds.CosmeticWorkGlasses)}"));
            checks.Add(new StartupCheck(
                "work_disables_normal_gameplay_tick",
                entered && !sandbox.IsPhysicsProcessing() && source.IsRunning,
                $"physics={sandbox.IsPhysicsProcessing()} source={source.IsRunning}"));

            var workView = tree.Root.FindChild(
                nameof(WorkCompanionView), recursive: true, owned: false) as WorkCompanionView;
            Rect2I beforeWheel = sandbox.Window.WorkCompanionRect;
            Vector2 viewportSize = workView?.GetWindow().Size ?? Vector2.Zero;
            float compositionScale = Math.Max(0.01f, Math.Min(
                viewportSize.X / WorkCompanionView.PreferredSize.X,
                viewportSize.Y / WorkCompanionView.PreferredSize.Y));
            Vector2 compositionOffset = (viewportSize - ((Vector2)WorkCompanionView.PreferredSize * compositionScale)) * .5f;
            Vector2 resizePointer = compositionOffset + (new Vector2(478, 148) * compositionScale);
            if (GodotObject.IsInstanceValid(workView))
            {
                workView!._Input(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = true,
                    Position = resizePointer,
                });
                workView._Input(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.WheelUp,
                    Pressed = true,
                    Position = resizePointer,
                });
                workView._Input(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = false,
                    Position = resizePointer,
                });
            }
            Rect2I afterWheel = sandbox.Window.WorkCompanionRect;
            Vector2 beforeAnchor = (Vector2)beforeWheel.Position + resizePointer;
            Vector2 normalizedAnchor = new(
                resizePointer.X / beforeWheel.Size.X,
                resizePointer.Y / beforeWheel.Size.Y);
            Vector2 afterAnchor = (Vector2)afterWheel.Position + ((Vector2)afterWheel.Size * normalizedAnchor);
            checks.Add(new StartupCheck(
                "work_lmb_wheel_resizes_from_pc_surface",
                GodotObject.IsInstanceValid(workView) &&
                    afterWheel.Size.X > beforeWheel.Size.X && afterWheel.Size.Y > beforeWheel.Size.Y &&
                    afterWheel.Size.X < beforeWheel.Size.X * 1.04f &&
                    afterAnchor.DistanceTo(beforeAnchor) <= 2.0f,
                $"view={GodotObject.IsInstanceValid(workView)} size={beforeWheel}->{afterWheel}"));

            if (GodotObject.IsInstanceValid(workView))
            {
                Rect2I beforeDrag = sandbox.Window.WorkCompanionRect;
                bool counterBefore = workView!.ShowLifetime;
                workView._UnhandledInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = true,
                    Position = resizePointer,
                });
                workView._UnhandledInput(new InputEventMouseMotion
                {
                    Position = resizePointer + new Vector2(12, 8),
                    Relative = new Vector2(12, 8),
                });
                workView._UnhandledInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = false,
                    Position = resizePointer + new Vector2(12, 8),
                });
                Rect2I afterDrag = sandbox.Window.WorkCompanionRect;
                workView._UnhandledInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = true,
                    Position = resizePointer,
                });
                workView._UnhandledInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = false,
                    Position = resizePointer,
                });
                Button resize = (Button)workView.FindChild("WorkResizeButton", true, false);
                Button motion = (Button)workView.FindChild("WorkMotionToggle", true, false);
                Button exit = (Button)workView.FindChild("WorkExitButton", true, false);
                // The title strip is gone (owner instruction 2026-08-20). What replaces it must
                // stay a fixed size in window pixels: the cluster sits outside the scaled
                // composition root precisely so shrinking the companion cannot shrink its own
                // controls into something unhittable.
                Control cluster = (Control)workView.FindChild("WorkControlCluster", true, false);
                bool unscaledControls =
                    Mathf.IsEqualApprox(cluster.Scale.X, 1.0f) &&
                    Mathf.IsEqualApprox(cluster.Scale.Y, 1.0f) &&
                    Mathf.IsEqualApprox(resize.Size.X, exit.Size.X) &&
                    Mathf.IsEqualApprox(resize.Size.Y, exit.Size.Y) &&
                    resize.Size.X >= 40.0f &&
                    workView.FindChild("WorkControlTitleBar", true, false) is null &&
                    resize.GetThemeStylebox("hover") is StyleBoxFlat hover &&
                    hover.BgColor == Win98ThemeFactory.Highlight &&
                    resize.GetThemeColor("font_hover_color") == Win98ThemeFactory.Dark;
                checks.Add(new StartupCheck(
                    "work_crt_drag_moves_click_toggles_and_controls_are_fixed_size",
                    afterDrag.Position != beforeDrag.Position && workView.ShowLifetime != counterBefore &&
                        resize.HasThemeStyleboxOverride("normal") && motion.HasThemeStyleboxOverride("normal") &&
                        exit.HasThemeStyleboxOverride("normal") && unscaledControls,
                    $"position={beforeDrag.Position}->{afterDrag.Position} counter={counterBefore}->{workView.ShowLifetime} " +
                    $"button={resize.Size} cluster_scale={cluster.Scale}"));
            }

            var requestedWorkSize = new Vector2I(600, 358);
            sandbox.Window.ResizeWorkCompanion(requestedWorkSize);
            sandbox.Shell.CaptureWindowStateForSave();
            checks.Add(new StartupCheck(
                "work_resize_does_not_replace_normal_window_geometry",
                sandbox.Window.WorkCompanionRect.Size == requestedWorkSize &&
                    sandbox.Shell.CurrentLocalSettings.WindowWidth == normalRect.Size.X &&
                    sandbox.Shell.CurrentLocalSettings.WindowHeight == normalRect.Size.Y,
                $"work={sandbox.Window.WorkCompanionRect} normal={normalRect} " +
                $"savedNormal={sandbox.Shell.CurrentLocalSettings.WindowWidth}x" +
                $"{sandbox.Shell.CurrentLocalSettings.WindowHeight}"));

            // Anything that re-applies normal-room geometry mid-session used to teleport the
            // companion to the room rectangle, and the 45s recovery tick below then made the
            // move permanent. The parked placement must survive both.
            Rect2I parked = sandbox.Window.WorkCompanionRect;
            WindowSettings preWork = sandbox.Window.CompactWindowSettings;
            sandbox.Window.ApplyWindowSettings(preWork with
            {
                Rect = new Rect2I(parked.Position + new Vector2I(400, 300), normalRect.Size),
            });
            bool layoutRefused = !sandbox.Window.TrySetLayoutMode(
                DesktopBuddy.Domain.Platform.WindowLayoutMode.FullscreenOverlay);
            Rect2I afterIntruder = sandbox.Window.WorkCompanionRect;
            // Applied normal geometry lands on the restore, not on the live companion.
            sandbox.Window.ApplyWindowSettings(preWork);

            source.Emit(WorkActivityKind.KeyboardPress);
            source.Emit(WorkActivityKind.KeyboardPress);
            coordinator._Process(0.0);
            coordinator._PhysicsProcess(45.0);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            checks.Add(new StartupCheck(
                "work_companion_placement_survives_normal_window_geometry",
                afterIntruder == parked &&
                    sandbox.Window.WorkCompanionRect == parked &&
                    layoutRefused,
                $"parked={parked} afterIntruder={afterIntruder} " +
                $"afterTick={sandbox.Window.WorkCompanionRect} layoutRefused={layoutRefused}"));

            WorkSessionSave? journal = store.Progress?.Work.ActiveSession;
            checks.Add(new StartupCheck(
                "work_periodic_checkpoint_persists_bounded_session_journal",
                journal is not null &&
                    journal.KeyboardPresses == 2 &&
                    journal.MouseClicks == 0 &&
                    journal.EarnedRepeatPerSessionMilestoneIds.SequenceEqual(
                        new[] { "work.scenario.session.2" }, StringComparer.Ordinal),
                $"session={journal?.SessionId} keys={journal?.KeyboardPresses} " +
                $"claims={journal?.EarnedRepeatPerSessionMilestoneIds.Count}"));

            adapter.RaiseSuspending();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool stoppedForSuspend = !source.IsRunning && source.StopCount >= 1;
            adapter.RaiseResumed();
            bool restartedAfterResume = source.IsRunning && source.StartCount == 2;
            checks.Add(new StartupCheck(
                "work_suspend_stops_and_resume_restarts_capture",
                stoppedForSuspend && restartedAfterResume,
                $"stopped={stoppedForSuspend} restarted={restartedAfterResume} " +
                $"starts={source.StartCount} stops={source.StopCount}"));

            progress.Unlock(ContentIds.ToolBaseballBat);
            sandbox.Pipeline.SelectTool(ToolId.BaseballBat);
            bool nonGrabWasSelectedDuringWork = sandbox.Pipeline.SelectedTool == ToolId.BaseballBat;

            await coordinator.ExitAsync();
            LocalSettingsSave savedSettings = sandbox.Shell.CurrentLocalSettings;
            checks.Add(new StartupCheck(
                "work_exit_clears_journal_and_restores_gameplay",
                !coordinator.IsActive &&
                    !source.IsRunning && source.Disposed &&
                    sandbox.IsPhysicsProcessing() &&
                    !sandbox.Window.WorkCompanionActive &&
                    !work.ActiveSession.HasValue &&
                    store.Progress?.Work.ActiveSession is null,
                $"active={coordinator.IsActive} running={source.IsRunning} disposed={source.Disposed} " +
                $"physics={sandbox.IsPhysicsProcessing()} window={sandbox.Window.WorkCompanionActive} " +
                $"journal={work.ActiveSession.HasValue}/{store.Progress?.Work.ActiveSession is not null}"));
            checks.Add(new StartupCheck(
                "work_exit_selects_normal_grab",
                nonGrabWasSelectedDuringWork && sandbox.Pipeline.SelectedTool == ToolId.Grab,
                $"preExitBat={nonGrabWasSelectedDuringWork} selected={sandbox.Pipeline.SelectedTool}"));
            checks.Add(new StartupCheck(
                "work_size_persists_separately_for_next_entry",
                sandbox.Window.CompactRect == normalRect &&
                    savedSettings.WorkWindowWidth == requestedWorkSize.X &&
                    savedSettings.WorkWindowHeight == requestedWorkSize.Y &&
                    sandbox.Shell.ResolveInitialWorkCompanionRect(WorkCompanionView.PreferredSize).Size ==
                        requestedWorkSize,
                $"normal={sandbox.Window.CompactRect} workSaved=" +
                $"{savedSettings.WorkWindowWidth}x{savedSettings.WorkWindowHeight}"));
        }
        finally
        {
            if (coordinator.IsActive)
                await coordinator.ExitAsync();
            await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }

    private sealed class ScenarioWorkActivitySource : IWorkActivitySource
    {
        public event Action<WorkActivityKind>? Activity;
        public bool IsRunning { get; private set; }
        public bool Disposed { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public WorkActivitySourceResult Start()
        {
            StartCount++;
            IsRunning = true;
            return WorkActivitySourceResult.Started;
        }

        public void Stop()
        {
            StopCount++;
            IsRunning = false;
        }

        public void Emit(WorkActivityKind kind) => Activity?.Invoke(kind);

        public void Dispose()
        {
            Disposed = true;
            IsRunning = false;
        }
    }
}
