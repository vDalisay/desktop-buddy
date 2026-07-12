using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Live 120 Hz regression for the passive six-body rig slice.</summary>
public sealed class PassiveRigScenario : IScenario
{
    private const int SettleTicks = 360;

    public string Id => "passive_rig";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}", $"ticks={SettleTicks}" };
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        bool sceneLoaded = packed is not null;
        checks.Add(new StartupCheck("passive_rig_scene_loadable", sceneLoaded, "res://scenes/buddy_lab.tscn"));
        if (!sceneLoaded)
        {
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed!.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        // Isolate the passive-structure response: no autonomous walk/jump gait, so the
        // hand-impulse perturbation settles through the springs and upright/balance
        // rather than being re-excited by a step cycle.
        lab.Buddy.ActiveDrive.SuppressLocomotion = true;
        if (!string.IsNullOrEmpty(ScenarioArtifacts.Directory))
            lab.EnableTelemetry(ScenarioArtifacts.Directory, Id);

        PuppetRig rig = lab.Buddy.Rig;
        Godot.Collections.Array<string> profileErrors = rig.Profile.Validate();
        checks.Add(new StartupCheck("passive_rig_profile_valid", profileErrors.Count == 0,
            profileErrors.Count == 0 ? rig.Profile.ResourceName : string.Join("; ", profileErrors)));
        checks.Add(new StartupCheck("exactly_six_bodies",
            rig.Parts.Count == PuppetRigProfile.RequiredPartCount,
            $"count={rig.Parts.Count}"));

        bool collisionContract = true;
        bool bodyConfiguration = true;
        foreach (PuppetPartBody body in rig.Parts)
        {
            collisionContract &= body.CollisionLayer == CollisionLayers.BuddyParts &&
                                 body.CollisionMask == CollisionLayers.MaskBuddyParts &&
                                 (body.CollisionMask & CollisionLayers.BuddyParts) == 0;
            bodyConfiguration &= !body.CanSleep && body.ContactMonitor && body.MaxContactsReported >= 8 &&
                                 body.Collider.Shape is CircleShape2D circle &&
                                 Mathf.IsEqualApprox(circle.Radius, body.Radius);
            body.GravityScale = 0.0f;
        }

        checks.Add(new StartupCheck("buddy_never_self_collides", collisionContract,
            $"mask={CollisionLayers.MaskBuddyParts}"));
        checks.Add(new StartupCheck("body_runtime_configuration", bodyConfiguration,
            "awake/contact_monitor/circle"));

        PuppetPartBody rightHand = rig.GetPart(BuddyPartId.RightHand);
        rightHand.ApplyImpulse(new Vector2(600.0f, -250.0f));

        bool finite = true;
        float maximumStrain = 0.0f;
        float maximumForce = 0.0f;
        for (int tick = 0; tick < SettleTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            finite &= rig.AllBodiesFinite();
            foreach (LinkTelemetry telemetry in lab.Buddy.Constraints.Telemetry)
            {
                finite &= telemetry.ForceOnA.IsFinite() && float.IsFinite(telemetry.Strain);
                maximumStrain = Mathf.Max(maximumStrain, telemetry.Strain);
                maximumForce = Mathf.Max(maximumForce, telemetry.ForceOnA.Length());
            }
        }

        // Measure speed relative to the center of mass: with gravity disabled the
        // hand impulse leaves the whole rig gliding, and that rigid translation
        // decays only at the (deliberately low, feel Task 1) linear damping rate.
        // "Settled" here means the springs stopped oscillating, i.e. the bodies are
        // at rest relative to the rig, not that the rig stopped translating.
        Vector2 comVelocity = Vector2.Zero;
        float totalRigMass = 0.0f;
        foreach (PuppetPartBody body in rig.Parts)
        {
            comVelocity += body.LinearVelocity * body.Mass;
            totalRigMass += body.Mass;
        }
        comVelocity = totalRigMass > 0.0f ? comVelocity / totalRigMass : Vector2.Zero;

        float finalMaximumSpeed = 0.0f;
        foreach (PuppetPartBody body in rig.Parts)
        {
            finalMaximumSpeed = Mathf.Max(finalMaximumSpeed, (body.LinearVelocity - comVelocity).Length());
        }

        bool strainBounded = maximumStrain <= 1.10f;
        bool settled = finalMaximumSpeed <= 8.0f;
        checks.Add(new StartupCheck("passive_rig_remains_finite", finite,
            $"max_force={maximumForce:F3}"));
        checks.Add(new StartupCheck("passive_rig_strain_bounded", strainBounded,
            $"max_strain={maximumStrain:F4}"));
        checks.Add(new StartupCheck("passive_rig_settles", settled,
            $"final_max_speed={finalMaximumSpeed:F3}"));

        lab.TelemetryRecorder?.Complete();
        if (lab.TelemetryRecorder is not null)
        {
            checks.Add(new StartupCheck("telemetry_jsonl_written", System.IO.File.Exists(lab.TelemetryRecorder.JsonLinesPath), lab.TelemetryRecorder.JsonLinesPath));
            using var envelope = System.IO.File.OpenRead(lab.TelemetryRecorder.EnvelopePath);
            checks.Add(new StartupCheck("telemetry_envelope_parses", DesktopBuddy.Domain.Telemetry.TelemetrySerializer.ReadEnvelope(envelope).FrameCount > 0, lab.TelemetryRecorder.EnvelopePath));
        }
        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }
}
