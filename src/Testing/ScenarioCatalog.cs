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
        ["grab_resistance"] = () => new GrabResistanceScenario(),
        ["grab_hard_recovery"] = () => new GrabHardRecoveryScenario(),
        ["room_resize_zoom"] = () => new RoomResizeZoomScenario(),
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
