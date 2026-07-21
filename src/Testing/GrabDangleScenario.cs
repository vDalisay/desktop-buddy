using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Every unsupported grab disables active drive but retains the same passive
/// structural springs as unconsciousness, so parts flex without sliding to link limits.</summary>
public sealed class GrabDangleScenario : IScenario
{
    private const int HoldTicks = 180;
    private const int MeasureStartTick = 90;
    private const float MaximumAcceptedLinkError = 24.0f;

    public string Id => "grab_dangle";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
            return new ScenarioResult(false,
                new[] { new StartupCheck("grab_dangle_scene_loadable", false, "buddy_lab") }, messages);

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ScenarioSteps.WaitForStanding(tree, lab, 1200);
        lab.Reactions.FearOverride = 1.0f;

        PuppetPartBody groundedFoot = lab.Buddy.Rig.LeftFoot;
        bool groundedGrabbed = lab.Grab.TryGrab(groundedFoot, groundedFoot.GlobalPosition);
        bool groundedGrabKeepsStanding = true;
        for (int tick = 0; tick < 12; tick++)
        {
            lab.Grab.MoveCursor(groundedFoot.GlobalPosition);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            groundedGrabKeepsStanding &=
                lab.Buddy.Standing.Snapshot.SupportContactCount > 0 &&
                lab.Buddy.ActiveDrive.ActiveOutputsEnabled;
        }
        lab.Grab.Release();

        bool allPartsPreserveTopology = true;
        bool allAirborneDrivePassive = true;
        bool allFinite = true;
        int totalUnsupportedTicks = 0;
        float worstLinkError = 0.0f;
        string worstPart = string.Empty;
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            BuddyPartId partId = (BuddyPartId)index;
            DangleObservation observation = await ObserveGrab(tree, lab, partId);
            allPartsPreserveTopology &= observation.MaximumLinkError <= MaximumAcceptedLinkError;
            allAirborneDrivePassive &= observation.Passive && observation.UnsupportedTicks >= 60;
            allFinite &= observation.Finite;
            totalUnsupportedTicks += observation.UnsupportedTicks;
            if (observation.MaximumLinkError > worstLinkError)
            {
                worstLinkError = observation.MaximumLinkError;
                worstPart = partId.ToString();
            }
            messages.Add($"grab_{partId}={observation}");
        }

        // The same structural topology must remain when the semantic state is explicitly
        // unconscious; conscious airborne grab differs only in expression/awareness.
        lab.Buddy.Rig.ResetToSafePose(new Vector2(240.0f, 240.0f));
        lab.Buddy.Standing.Reset();
        lab.Buddy.SetConsciousness(Consciousness.Unconscious);
        PuppetPartBody hand = lab.Buddy.Rig.LeftHand;
        bool unconsciousGrabbed = lab.Grab.TryGrab(hand, hand.GlobalPosition);
        float unconsciousWorstError = 0.0f;
        bool unconsciousPassive = true;
        for (int tick = 0; tick < HoldTicks; tick++)
        {
            lab.Grab.MoveCursor(new Vector2(240.0f, 150.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (tick >= MeasureStartTick)
                unconsciousWorstError = Mathf.Max(unconsciousWorstError, MaximumLinkError(lab));
            unconsciousPassive &= !lab.Buddy.ActiveDrive.ActiveOutputsEnabled;
        }
        lab.Grab.Release();
        lab.Buddy.SetConsciousness(Consciousness.Conscious);

        bool resumed = false;
        for (int tick = 0; tick < 1200 && !resumed; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            resumed = lab.Buddy.ActiveDrive.ActiveOutputsEnabled;
        }

        checks.Add(new StartupCheck("grounded_grab_keeps_standing",
            groundedGrabbed && groundedGrabKeepsStanding,
            $"grabbed={groundedGrabbed} standing={groundedGrabKeepsStanding}"));
        checks.Add(new StartupCheck("all_grabbed_parts_preserve_passive_topology",
            allPartsPreserveTopology && allFinite,
            $"worst={worstLinkError:F1}px part={worstPart} bound={MaximumAcceptedLinkError:F1}px finite={allFinite}"));
        checks.Add(new StartupCheck("all_airborne_grabs_disable_active_drive",
            allAirborneDrivePassive,
            $"passive={allAirborneDrivePassive} unsupported_ticks={totalUnsupportedTicks}"));
        checks.Add(new StartupCheck("unconscious_and_conscious_use_same_structure",
            unconsciousGrabbed && unconsciousPassive &&
            unconsciousWorstError <= MaximumAcceptedLinkError,
            $"grabbed={unconsciousGrabbed} passive={unconsciousPassive} " +
            $"worst={unconsciousWorstError:F1}px"));
        checks.Add(new StartupCheck("grab_dangle_release_resumes_drive", resumed,
            $"resumed={resumed}"));

        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<DangleObservation> ObserveGrab(
        SceneTree tree, BuddyLab lab, BuddyPartId partId)
    {
        lab.Grab.Release();
        lab.Buddy.Rig.ResetToSafePose(new Vector2(240.0f, 240.0f));
        lab.Buddy.Standing.Reset();
        PuppetPartBody target = lab.Buddy.Rig.GetPart(partId);
        bool grabbed = lab.Grab.TryGrab(target, target.GlobalPosition);
        int unsupportedTicks = 0;
        float maximumError = 0.0f;
        bool passive = grabbed;
        for (int tick = 0; tick < HoldTicks; tick++)
        {
            lab.Grab.MoveCursor(new Vector2(240.0f, 150.0f));
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.Standing.Snapshot.SupportContactCount == 0)
            {
                unsupportedTicks++;
                passive &= !lab.Buddy.ActiveDrive.ActiveOutputsEnabled &&
                    lab.Buddy.ActiveDrive.LastResistanceForce.IsZeroApprox() &&
                    lab.Buddy.ActiveDrive.LastBalanceForce.IsZeroApprox() &&
                    lab.Buddy.ActiveDrive.LastLocomotionForce.IsZeroApprox();
                if (tick >= MeasureStartTick)
                    maximumError = Mathf.Max(maximumError, MaximumLinkError(lab));
            }
        }
        lab.Grab.Release();
        return new DangleObservation(grabbed, unsupportedTicks, passive,
            maximumError, lab.Buddy.Rig.AllBodiesFinite());
    }

    private static float MaximumLinkError(BuddyLab lab)
    {
        float maximum = 0.0f;
        foreach (PuppetLinkDefinition link in lab.Buddy.Rig.Profile.Links)
        {
            PuppetPartBody partA = lab.Buddy.Rig.GetPart(link.PartA);
            PuppetPartBody partB = lab.Buddy.Rig.GetPart(link.PartB);
            Vector2 actual = partB.GlobalPosition - partA.GlobalPosition;
            Vector2 expected = link.RestOffset.Rotated(partA.GlobalRotation);
            maximum = Mathf.Max(maximum, actual.DistanceTo(expected));
        }
        return maximum;
    }

    private readonly record struct DangleObservation(
        bool Grabbed,
        int UnsupportedTicks,
        bool Passive,
        float MaximumLinkError,
        bool Finite);
}
