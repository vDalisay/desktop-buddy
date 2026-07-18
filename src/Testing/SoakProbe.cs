using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Testing;

public readonly record struct SoakProbeResult(
    int TickCount, bool Finite, bool Awake, float MaximumStrain, bool Contained, int HardRecoveries);

public static class SoakProbe
{
    /// <param name="onTick">
    /// Optional per-tick observer (zero-based tick index), invoked after the probe's own
    /// checks. Lets callers layer extra sampling (e.g. presentation-look visual stability)
    /// over the one canonical soak loop instead of re-implementing it.
    /// </param>
    public static async Task<SoakProbeResult> RunAsync(
        SceneTree tree, BuddyLab lab, int tickBudget, System.Action<int>? onTick = null)
    {
        bool finite = true;
        bool awake = true;
        float maximumStrain = 0.0f;
        int ticks = 0;
        // A healthy idle buddy never needs a hard reset: the standing detector must
        // keep reporting foot support, so the recovery clock never reaches timeout.
        // Any hard recovery during the soak means the deep-rest foot-contact blind
        // spot (rotated contact normals) has regressed. See PuppetPartBody.
        int hardRecoveriesAtStart = lab.Buddy.Recovery.HardRecoveryCount;
        for (; ticks < tickBudget; ticks++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            finite &= lab.Buddy.Rig.AllBodiesFinite();
            foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
                awake &= !part.Sleeping;
            foreach (LinkTelemetry link in lab.Buddy.Constraints.Telemetry)
                maximumStrain = Mathf.Max(maximumStrain, link.Strain);
            onTick?.Invoke(ticks);
            if (!finite)
            {
                ticks++;
                break;
            }
        }
        return new SoakProbeResult(ticks, finite, awake, maximumStrain,
            lab.Buddy.Recovery.AllBodiesInsideSafeBounds(),
            lab.Buddy.Recovery.HardRecoveryCount - hardRecoveriesAtStart);
    }
}
