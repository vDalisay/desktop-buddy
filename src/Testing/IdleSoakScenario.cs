using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Telemetry;
using DesktopBuddy.Laboratory;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Thirty simulated minutes at 120 Hz. Automation invokes Godot with
/// <c>--fixed-fps 120</c>, which advances one fixed step per uncapped headless
/// main-loop iteration and therefore decouples simulation time from wall time.
/// The laboratory's capped Engine.TimeScale control is not used.
/// </summary>
public sealed class IdleSoakScenario : IScenario
{
    public const int FullTicks = 30 * 60 * 120;
    private readonly int _ticks;
    public IdleSoakScenario(int ticks = FullTicks) => _ticks = ticks;
    public string Id => _ticks == FullTicks ? "idle_soak" : "idle_soak_ci";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}", $"ticks={_ticks}", "real fixed-step pump; CI uses three-minute variant, full run is nightly/manual" };
        BuddyLab lab = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn").Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);
        if (!string.IsNullOrEmpty(ScenarioArtifacts.Directory)) lab.EnableTelemetry(ScenarioArtifacts.Directory, Id);
        EnvelopeBoundsProfile bounds = GD.Load<EnvelopeBoundsProfile>("res://data/buddy/lab_envelope_bounds.tres");
        SoakProbeResult result = await SoakProbe.RunAsync(tree, lab, _ticks);

        // "Standing-capable at soak end": the buddy may be mid-step/mid-jump on the
        // final tick, so allow a settle window before sampling. Window = worst-case
        // recovery clock (assistance delay + hard-recovery delay) + measured settle
        // bound. It no longer has to absorb a deep-rest foot-contact stall — that was
        // the rotated-normal bug fixed in PuppetPartBody, now guarded below.
        int recoveryWindow = RecoveryClock.AssistanceDelayTicks +
                             RecoveryClock.HardRecoveryDelayTicks +
                             bounds.MaximumSettleTicks;
        int hardRecoveriesBefore = lab.Buddy.Recovery.HardRecoveryCount;
        bool standing = false;
        int recoveryTicks = 0;
        for (; recoveryTicks < recoveryWindow; recoveryTicks++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.Standing.Snapshot.IsStable) { standing = true; recoveryTicks++; break; }
        }
        int hardRecoveries = lab.Buddy.Recovery.HardRecoveryCount - hardRecoveriesBefore;
        lab.TelemetryRecorder?.Complete();
        checks.Add(new StartupCheck("idle_soak_bodies_finite", result.Finite, $"ticks={result.TickCount}"));
        checks.Add(new StartupCheck("idle_soak_bodies_awake", result.Awake, "CanSleep=false"));
        checks.Add(new StartupCheck("idle_soak_connected", result.MaximumStrain <= bounds.MaximumLinkStrain, $"max_strain={result.MaximumStrain:F4} bound={bounds.MaximumLinkStrain:F4}"));
        checks.Add(new StartupCheck("idle_soak_contained", result.Contained, "all bodies inside room"));
        checks.Add(new StartupCheck("idle_soak_no_hard_recovery", result.HardRecoveries == 0,
            $"hard_recoveries_during_soak={result.HardRecoveries}"));
        checks.Add(new StartupCheck("idle_soak_standing_capable", standing,
            $"recovery_ticks={recoveryTicks}/{recoveryWindow} hard_recoveries={hardRecoveries}"));
        if (lab.TelemetryRecorder is not null)
        {
            checks.Add(new StartupCheck("idle_soak_envelope_written", System.IO.File.Exists(lab.TelemetryRecorder.EnvelopePath), lab.TelemetryRecorder.EnvelopePath));
            using var envelopeStream = System.IO.File.OpenRead(lab.TelemetryRecorder.EnvelopePath);
            TelemetryEnvelope envelope = TelemetrySerializer.ReadEnvelope(envelopeStream);
            int telemetryTicks = result.TickCount + recoveryTicks;
            checks.Add(new StartupCheck("idle_soak_frame_accounting", System.Math.Abs(envelope.FrameCount - telemetryTicks) <= 16,
                $"frames={envelope.FrameCount} ticks={telemetryTicks}"));
        }
        lab.QueueFree();
        bool passed = true; foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
