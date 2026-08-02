using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class HiddenClockAccrualScenario : IScenario
{
    public string Id => "hidden_clock_accrual";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var time = new ManualMonotonicTimeSource();
        var loaded = await M4LifecycleScenarioSupport.Load(tree, time);
        if (loaded is null)
            return new ScenarioResult(false,
                [new StartupCheck("sandbox_loadable", false, "sandbox")], messages);
        var (sandbox, store) = loaded.Value;

        var positions = new Vector2[sandbox.Buddy.Rig.Parts.Count];
        for (int index = 0; index < positions.Length; index++)
            positions[index] = sandbox.Buddy.Rig.Parts[index].GlobalPosition;

        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        time.Advance(0.4);
        sandbox.SetHiddenToTray(true);
        for (int sample = 0; sample < 601; sample++)
        {
            time.Advance(0.1);
            M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        }

        bool poseFrozen = true;
        for (int index = 0; index < positions.Length; index++)
        {
            PuppetPartBody part = sandbox.Buddy.Rig.Parts[index];
            poseFrozen &= part.GlobalPosition.IsEqualApprox(positions[index]);
        }
        checks.Add(new StartupCheck(
            "hidden_freezes_ragdoll",
            tree.Paused && poseFrozen && !sandbox.Window.Adapter.IsWindowVisible,
            $"paused={tree.Paused} frozen={poseFrozen} " +
            $"window_visible={sandbox.Window.Adapter.IsWindowVisible}"));

        // Pausing the tree stops gameplay but not the main loop. Without the render-loop
        // and frame-cap throttle the process keeps drawing behind an invisible window and
        // the hidden-CPU target is unreachable (ARCHITECTURE §24). Headless has no
        // rendering to throttle, so the flag is expected off there.
        bool headless = DisplayServer.GetName() == "headless";
        bool throttled = headless
            ? !sandbox.Lifecycle.IsPresentationThrottled
            : sandbox.Lifecycle.IsPresentationThrottled &&
              Engine.MaxFps == sandbox.MoodEconomy.HiddenMaxFps &&
              !RenderingServer.RenderLoopEnabled;
        checks.Add(new StartupCheck(
            "hidden_throttles_presentation",
            throttled,
            $"headless={headless} throttled={sandbox.Lifecycle.IsPresentationThrottled} " +
            $"max_fps={Engine.MaxFps} render_loop={RenderingServer.RenderLoopEnabled}"));

        long balance = sandbox.Progress.BalanceMilliCredits;
        double hidden = sandbox.Progress.Times.HiddenSeconds;
        double foreground = sandbox.Progress.Times.RunSeconds - hidden;
        // The expected payout is derived from the authored rate at neutral mood, not from a
        // literal: the rate is Task 12 calibration output and moves without this scenario's
        // meaning changing.
        long expected = (long)(sandbox.Progress.Times.RunSeconds *
                               sandbox.MoodEconomy.NeutralCreditsPerMinute / 60.0 * 1000.0);
        checks.Add(new StartupCheck(
            "hidden_accrues_mood_income_and_time",
            Mathf.Abs(balance - expected) <= 3 &&
            hidden is >= 59.9 and <= 60.2 &&
            foreground is >= 0.39 and <= 0.41,
            $"balance={balance} expected={expected} run={sandbox.Progress.Times.RunSeconds:F3} " +
            $"hidden={hidden:F3} foreground={foreground:F3}"));
        checks.Add(new StartupCheck(
            "hidden_accrual_autosaves",
            store.ProgressWriteCount >= 1,
            $"writes={store.ProgressWriteCount} dirty={sandbox.Saves.IsDirty}"));

        var beforeShow = new Vector2[sandbox.Buddy.Rig.Parts.Count];
        for (int index = 0; index < beforeShow.Length; index++)
            beforeShow[index] = sandbox.Buddy.Rig.Parts[index].GlobalPosition;

        // Create one sub-cadence tail. Showing the window must settle it into hidden time
        // before changing the accounting bucket.
        time.Advance(0.1);
        sandbox.SetHiddenToTray(false);
        double hiddenAfterShow = sandbox.Progress.Times.HiddenSeconds;
        checks.Add(new StartupCheck(
            "mode_transitions_settle_the_previous_bucket",
            hiddenAfterShow > hidden &&
            hiddenAfterShow - hidden is >= 0.09 and <= 0.11,
            $"before={hidden:F3} after={hiddenAfterShow:F3}"));
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        // No burst means the first simulated frame after show advances the pose by an
        // ordinary step, not by a replay of the hidden span (FR-015.10). The step
        // accumulator is bounded by physics/common/max_physics_steps_per_frame.
        float largestJump = 0.0f;
        for (int index = 0; index < beforeShow.Length; index++)
        {
            largestJump = Mathf.Max(
                largestJump,
                beforeShow[index].DistanceTo(sandbox.Buddy.Rig.Parts[index].GlobalPosition));
        }

        checks.Add(new StartupCheck(
            "show_resumes_without_physics_burst",
            !tree.Paused && sandbox.Buddy.Rig.AllBodiesFinite() && largestJump <= 8.0f,
            $"paused={tree.Paused} finite={sandbox.Buddy.Rig.AllBodiesFinite()} " +
            $"largest_jump={largestJump:F3}"));
        checks.Add(new StartupCheck(
            "show_restores_presentation",
            !sandbox.Lifecycle.IsPresentationThrottled &&
            RenderingServer.RenderLoopEnabled &&
            sandbox.Window.Adapter.IsWindowVisible,
            $"throttled={sandbox.Lifecycle.IsPresentationThrottled} " +
            $"render_loop={RenderingServer.RenderLoopEnabled} max_fps={Engine.MaxFps} " +
            $"window_visible={sandbox.Window.Adapter.IsWindowVisible}"));

        double beforeShutdown = sandbox.Progress.Times.RunSeconds;
        time.Advance(0.1);
        sandbox.Lifecycle.BeginShutdown();
        double settledAtShutdown = sandbox.Progress.Times.RunSeconds;
        await sandbox.Saves.FlushProgressAsync(force: true);
        time.Advance(1.0);
        M4LifecycleScenarioSupport.Sample(sandbox.Lifecycle);
        checks.Add(new StartupCheck(
            "clean_exit_settles_then_saves_the_final_revision",
            settledAtShutdown - beforeShutdown is >= 0.09 and <= 0.11 &&
            sandbox.Progress.Times.RunSeconds == settledAtShutdown &&
            store.Progress?.Times.RunSeconds == settledAtShutdown &&
            !sandbox.Saves.IsDirty,
            $"before={beforeShutdown:F3} settled={settledAtShutdown:F3} " +
            $"saved={store.Progress?.Times.RunSeconds:F3} dirty={sandbox.Saves.IsDirty}"));
        messages.Add($"hidden={hidden:F3}s balance_milli={balance}");
        await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
