using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class SuspendNoCatchupScenario : IScenario
{
    public string Id => "suspend_no_catchup";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var time = new ManualMonotonicTimeSource();
        var loaded = await M4LifecycleScenarioSupport.Load(tree, time);
        if (loaded is null)
            return new ScenarioResult(false,
                [new StartupCheck("sandbox_loadable", false, "sandbox")], messages);
        SandboxRoot sandbox = loaded.Value.Sandbox;

        // Drive the real platform seam, not the coordinator directly: the emulated
        // adapter is what a native power/session notification will replace.
        var adapter = sandbox.Window.Adapter as EmulatedWindowsDesktopAdapter;
        checks.Add(new StartupCheck(
            "lifecycle_stimuli_arrive_through_the_platform_adapter",
            adapter is not null,
            $"adapter={sandbox.Window.Adapter.GetType().Name}"));
        if (adapter is null)
            return new ScenarioResult(false, checks, messages);

        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        time.Advance(1.0);
        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        double beforeSuspend = sandbox.Progress.Times.RunSeconds;
        long balanceBefore = sandbox.Progress.BalanceMilliCredits;

        time.Advance(0.1);
        adapter.RaiseSuspending();
        double settledAtSuspend = sandbox.Progress.Times.RunSeconds;
        checks.Add(new StartupCheck(
            "suspend_settles_the_pre_suspend_tail",
            settledAtSuspend - beforeSuspend is >= 0.09 and <= 0.11,
            $"before={beforeSuspend:F3} settled={settledAtSuspend:F3}"));
        time.Advance(3_600.0);
        adapter.RaiseResumed();
        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        time.Advance(0.1);
        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);

        double afterResume = sandbox.Progress.Times.RunSeconds;
        long balanceAfter = sandbox.Progress.BalanceMilliCredits;
        checks.Add(new StartupCheck(
            "suspend_gap_awards_no_catchup",
            afterResume - settledAtSuspend < 0.2 &&
            balanceAfter - balanceBefore < 10,
            $"run_before={beforeSuspend:F3} run_after={afterResume:F3} " +
            $"balance_before={balanceBefore} balance_after={balanceAfter}"));

        int excludedBeforeDiscontinuity = sandbox.Lifecycle.ExcludedSpanCount;
        time.Advance(10.0);
        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        double afterDiscontinuity = sandbox.Progress.Times.RunSeconds;
        checks.Add(new StartupCheck(
            "large_discontinuity_is_excluded",
            sandbox.Lifecycle.ExcludedSpanCount == excludedBeforeDiscontinuity + 1 &&
            afterDiscontinuity == afterResume,
            $"excluded_before={excludedBeforeDiscontinuity} " +
            $"excluded_after={sandbox.Lifecycle.ExcludedSpanCount} run={afterDiscontinuity:F3}"));
        checks.Add(new StartupCheck(
            "resume_state_is_finite",
            !tree.Paused && sandbox.Buddy.Rig.AllBodiesFinite(),
            $"paused={tree.Paused} finite={sandbox.Buddy.Rig.AllBodiesFinite()}"));

        // FR-016.8: a locked session is running hidden time, not a discontinuity. The
        // machine keeps going, so income and drift continue and nothing is excluded.
        // Add another valid sub-cadence span after the excluded gap. The lock transition
        // must retain both valid tails and settle them as foreground before switching.
        time.Advance(0.2);
        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        double runBeforeLock = sandbox.Progress.Times.RunSeconds;
        double hiddenBeforeLock = sandbox.Progress.Times.HiddenSeconds;
        long balanceBeforeLock = sandbox.Progress.BalanceMilliCredits;
        int excludedBeforeLock = sandbox.Lifecycle.ExcludedSpanCount;
        time.Advance(0.1);
        adapter.RaiseSessionLockChanged(locked: true);
        double foregroundSettledAtLock = sandbox.Progress.Times.RunSeconds;
        checks.Add(new StartupCheck(
            "session_lock_settles_the_foreground_tail",
            foregroundSettledAtLock - runBeforeLock is >= 0.39 and <= 0.41 &&
            sandbox.Progress.Times.HiddenSeconds == hiddenBeforeLock,
            $"before={runBeforeLock:F3} settled={foregroundSettledAtLock:F3}"));
        for (int sample = 0; sample < 20; sample++)
        {
            time.Advance(0.1);
            M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        }
        double lockedHidden = sandbox.Progress.Times.HiddenSeconds - hiddenBeforeLock;
        double lockedRun = sandbox.Progress.Times.RunSeconds - foregroundSettledAtLock;
        checks.Add(new StartupCheck(
            "session_lock_accrues_as_hidden_running_time",
            sandbox.Lifecycle.IsSessionLocked &&
            lockedHidden >= 1.9 && lockedHidden <= 2.1 &&
            lockedRun >= 1.9 && lockedRun <= 2.1 &&
            sandbox.Progress.BalanceMilliCredits > balanceBeforeLock &&
            sandbox.Lifecycle.ExcludedSpanCount == excludedBeforeLock,
            $"locked={sandbox.Lifecycle.IsSessionLocked} hidden={lockedHidden:F3} " +
            $"run={lockedRun:F3} excluded={sandbox.Lifecycle.ExcludedSpanCount}"));

        time.Advance(0.1);
        adapter.RaiseSessionLockChanged(locked: false);
        time.Advance(0.1);
        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        double activeAfterUnlock = sandbox.Progress.Times.HiddenSeconds;
        checks.Add(new StartupCheck(
            "session_unlock_restores_foreground_accounting",
            !sandbox.Lifecycle.IsSessionLocked &&
            !sandbox.Lifecycle.AccruesAsHidden &&
            activeAfterUnlock - (hiddenBeforeLock + lockedHidden) is >= 0.09 and <= 0.11,
            $"locked={sandbox.Lifecycle.IsSessionLocked} hidden={activeAfterUnlock:F3}"));

        messages.Add($"run={afterDiscontinuity:F3}s excluded={sandbox.Lifecycle.ExcludedSpanCount}");
        await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
