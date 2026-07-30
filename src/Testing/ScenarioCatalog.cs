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
        ["autonomy_respects_walls"] = () => new AutonomyWallScenario(),
        ["laboratory_controls"] = () => new LaboratoryControlsScenario(),
        ["grab_release"] = () => new GrabReleaseScenario(),
        ["grab_hold_aloft"] = () => new GrabHoldAloftScenario(),
        ["grab_dangle"] = () => new GrabDangleScenario(),
        ["grab_hang_orientation"] = () => new GrabHangOrientationScenario(),
        ["grab_swing_pendulum"] = () => new GrabSwingPendulumScenario(),
        ["grab_resistance"] = () => new GrabResistanceScenario(),
        ["grab_hard_recovery"] = () => new GrabHardRecoveryScenario(),
        ["room_resize_zoom"] = () => new RoomResizeZoomScenario(),
        ["idle_soak"] = () => new IdleSoakScenario(),
        ["idle_soak_ci"] = () => new IdleSoakScenario(IdleSoakScenario.CiTicks),
        ["pose_pipeline"] = () => new PosePipelineScenario(),
        ["facing_follows_walk"] = () => new FacingScenario(),
        ["activity_clips"] = () => new ActivityClipsScenario(),
        ["head_rights_after_disturbance"] = () => new HeadRightingScenario(),
        ["owner_feedback_visual"] = () => new OwnerFeedbackVisualScenario(),
        ["lookat_priority_and_cone"] = () => new LookAtScenario(),
        ["face_composition"] = () => new FaceCompositionScenario(),
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
        ["object_catch_hold"] = () => new ObjectCatchHoldScenario(),
        ["object_toss_discard"] = () => new ObjectTossDiscardScenario(),
        ["corner_scoop"] = () => new CornerScoopScenario(),
        ["object_budget"] = () => new ObjectBudgetScenario(),
        ["meal_consume"] = () => new MealConsumeScenario(),
        ["fun_catch_laugh"] = () => new FunCatchLaughScenario(),
        ["consume_care_cooldown"] = () => new ConsumeCareCooldownScenario(),
        ["behavior_priority_ladder"] = () => new BehaviorPriorityLadderScenario(),
        ["mood_band_behavior"] = () => new MoodBandBehaviorScenario(),
        ["jump_trait_gate"] = () => new JumpTraitGateScenario(),
        ["hidden_clock_accrual"] = () => new HiddenClockAccrualScenario(),
        ["suspend_no_catchup"] = () => new SuspendNoCatchupScenario(),
        ["baseball_pullback"] = () => new BaseballPullbackScenario(),
        ["bat_swing"] = () => new BatSwingScenario(),
        ["homerun_bat_feel"] = () => new HomeRunBatFeelScenario(),
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
