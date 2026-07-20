using System;
using System.Collections.Generic;

namespace DesktopBuddy.Testing;

/// <summary>
/// Registry of headless scenarios by stable id. New scenarios register here as
/// the milestone that introduces them lands; Milestone 0 ships only the boot
/// smoke gate.
/// </summary>
public static class ScenarioCatalog
{
    private static readonly Dictionary<string, Func<IScenario>> Factories = new(StringComparer.Ordinal)
    {
        ["boot_smoke"] = () => new BootSmokeScenario(),
        ["passive_rig"] = () => new PassiveRigScenario(),
        ["standing_recovery"] = () => new StandingRecoveryScenario(),
        ["autonomous_motion"] = () => new AutonomousMotionScenario(),
        ["laboratory_controls"] = () => new LaboratoryControlsScenario(),
        ["grab_release"] = () => new GrabReleaseScenario(),
        ["grab_hold_aloft"] = () => new GrabHoldAloftScenario(),
        ["grab_resistance"] = () => new GrabResistanceScenario(),
        ["grab_hard_recovery"] = () => new GrabHardRecoveryScenario(),
        ["room_resize_zoom"] = () => new RoomResizeZoomScenario(),
        ["idle_soak"] = () => new IdleSoakScenario(),
        ["idle_soak_ci"] = () => new IdleSoakScenario(IdleSoakScenario.CiTicks),
        ["pose_pipeline"] = () => new PosePipelineScenario(),
        ["facing_follows_walk"] = () => new FacingScenario(),
        ["activity_clips"] = () => new ActivityClipsScenario(),
        ["lookat_priority_and_cone"] = () => new LookAtScenario(),
        ["repeat_envelope"] = () => new RepeatEnvelopeScenario(),
        ["dual_profile_smoke"] = () => new DualProfileSmokeScenario(),
        ["impact_dedup"] = () => new ImpactDedupScenario(),
        ["knockout_window"] = () => new KnockoutWindowScenario(),
        ["payout_by_region"] = () => new PayoutByRegionScenario(),
        ["pet_tickle_mood"] = () => new PetTickleMoodScenario(),
        ["m3_presentation"] = () => new M3PresentationScenario(),
        ["tool_feel_reactions"] = () => new ToolFeelReactionScenario(),
        ["presentation_3d"] = () => new Presentation3DScenario(),
        ["presentation_look"] = () => new PresentationLookScenario(),
    };

    public static IReadOnlyCollection<string> Ids => Factories.Keys;

    public static IScenario? Find(string? id)
    {
        if (id is not null && Factories.TryGetValue(id, out Func<IScenario>? factory))
        {
            return factory();
        }

        return null;
    }
}
