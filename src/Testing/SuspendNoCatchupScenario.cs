using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
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

        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        time.Advance(1.0);
        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        double beforeSuspend = sandbox.Progress.Times.RunSeconds;
        long balanceBefore = sandbox.Progress.BalanceMilliCredits;

        sandbox.Lifecycle.NotifySuspended();
        time.Advance(3_600.0);
        sandbox.Lifecycle.NotifyResumed(remainHidden: false);
        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        time.Advance(0.1);
        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);

        double afterResume = sandbox.Progress.Times.RunSeconds;
        long balanceAfter = sandbox.Progress.BalanceMilliCredits;
        checks.Add(new StartupCheck(
            "suspend_gap_awards_no_catchup",
            afterResume - beforeSuspend < 0.2 &&
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

        messages.Add($"run={afterDiscontinuity:F3}s excluded={sandbox.Lifecycle.ExcludedSpanCount}");
        await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
