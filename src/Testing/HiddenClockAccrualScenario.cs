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
        checks.Add(new StartupCheck(
            "hidden_accrues_mood_income_and_time",
            balance is >= 998 and <= 1001 &&
            hidden is >= 59.9 and <= 60.1 &&
            sandbox.Progress.Times.RunSeconds is >= 59.9 and <= 60.1,
            $"balance={balance} run={sandbox.Progress.Times.RunSeconds:F3} hidden={hidden:F3}"));
        checks.Add(new StartupCheck(
            "hidden_accrual_autosaves",
            store.ProgressWriteCount >= 1,
            $"writes={store.ProgressWriteCount} dirty={sandbox.Saves.IsDirty}"));

        var beforeShow = new Vector2[sandbox.Buddy.Rig.Parts.Count];
        for (int index = 0; index < beforeShow.Length; index++)
            beforeShow[index] = sandbox.Buddy.Rig.Parts[index].GlobalPosition;

        sandbox.SetHiddenToTray(false);
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
        messages.Add($"hidden={hidden:F3}s balance_milli={balance}");
        await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
