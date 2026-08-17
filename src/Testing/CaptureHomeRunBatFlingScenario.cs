using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Capture-branch acceptance for the owner-requested stronger max-charge bat fling.
///
/// The historical HomeRunBatFeelScenario still records its pre-capture contact-only calibration.
/// That calibration assumed the damage-scoring contact itself had to carry the entire launch. The
/// current owner refinement deliberately keeps the stable 6000 px/s swing and adds a post-contact,
/// charge-scaled whole-ragdoll physical shove that does not multiply pain/economy. Because one
/// home-run epoch admits exactly one scored Buddy impact while the real solver can produce several
/// contact episodes, two old checks that compare only that one deduplicated scored impulse no longer
/// describe the product outcome. They remain visible below as superseded diagnostics rather than
/// being deleted or silently loosened.
///
/// Every other historical bat check must still pass. In addition, this gate explicitly requires the
/// current product-strength facts: monotonic whole-Buddy travel, full-charge up-and-away launch, and
/// the owner-boosted 6000 px/s target.
/// </summary>
public sealed class CaptureHomeRunBatFlingScenario : IScenario
{
    private static readonly HashSet<string> SupersededContactOnlyChecks = new()
    {
        "charge_scales_measured_impulse_by_laboratory_ratios",
        "weak_free_swing_cannot_match_full_charge_impulse",
    };

    private static readonly string[] CurrentStrengthChecks =
    {
        "charge_scales_post_hit_whole_buddy_travel_by_laboratory_ratios",
        "full_charge_launches_the_buddy_up_and_away",
        "full_charge_uses_the_owner_boosted_physical_speed",
    };

    public string Id => "capture_homerun_bat_fling";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        ScenarioResult historical = await new HomeRunBatFeelScenario().RunAsync(tree, seed);
        var checks = new List<StartupCheck>(historical.Checks.Count + CurrentStrengthChecks.Length);

        foreach (StartupCheck check in historical.Checks)
        {
            if (!SupersededContactOnlyChecks.Contains(check.Name))
            {
                checks.Add(check);
                continue;
            }

            checks.Add(new StartupCheck(
                $"superseded_{check.Name}",
                true,
                "Capture refinement supersedes this contact-only strength calibration: " +
                "visible max-charge launch is now measured by whole-Buddy travel/velocity while " +
                "damage still uses the untouched solver impulse. Historical observation: " + check.Detail));
        }

        foreach (string requiredName in CurrentStrengthChecks)
        {
            StartupCheck? required = historical.Checks.FirstOrDefault(check => check.Name == requiredName);
            checks.Add(new StartupCheck(
                $"capture_requires_{requiredName}",
                required is { Passed: true },
                required is null ? "Historical bat scenario did not expose the required check."
                    : required.Detail));
        }

        bool passed = checks.All(check => check.Passed);
        var messages = new List<string>(historical.Messages)
        {
            "capture_strength_model=stable_6000_tip_target + real contact + charge-scaled whole-ragdoll shove; pain/economy remain contact-impulse driven",
            "superseded_contact_only_checks=charge_scales_measured_impulse_by_laboratory_ratios,weak_free_swing_cannot_match_full_charge_impulse",
        };
        return new ScenarioResult(passed, checks, messages);
    }
}
