using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using DesktopBuddy.Platform;
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

            source.Emit(WorkActivityKind.KeyboardPress);
            source.Emit(WorkActivityKind.KeyboardPress);
            coordinator._Process(0.0);
            coordinator._PhysicsProcess(45.0);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

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

            await coordinator.ExitAsync();
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
