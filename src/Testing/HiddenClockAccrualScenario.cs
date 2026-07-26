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
            tree.Paused && poseFrozen,
            $"paused={tree.Paused} frozen={poseFrozen}"));

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

        sandbox.SetHiddenToTray(false);
        checks.Add(new StartupCheck(
            "show_resumes_without_physics_burst",
            !tree.Paused && sandbox.Buddy.Rig.AllBodiesFinite(),
            $"paused={tree.Paused} finite={sandbox.Buddy.Rig.AllBodiesFinite()}"));
        messages.Add($"hidden={hidden:F3}s balance_milli={balance}");
        await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
