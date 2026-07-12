using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Testing;

public readonly record struct SoakProbeResult(int TickCount, bool Finite, bool Awake, float MaximumStrain, bool Contained);

public static class SoakProbe
{
    public static async Task<SoakProbeResult> RunAsync(SceneTree tree, BuddyLab lab, int tickBudget)
    {
        bool finite = true;
        bool awake = true;
        float maximumStrain = 0.0f;
        int ticks = 0;
        for (; ticks < tickBudget; ticks++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            finite &= lab.Buddy.Rig.AllBodiesFinite();
            foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
                awake &= !part.Sleeping;
            foreach (LinkTelemetry link in lab.Buddy.Constraints.Telemetry)
                maximumStrain = Mathf.Max(maximumStrain, link.Strain);
            if (!finite)
            {
                ticks++;
                break;
            }
        }
        return new SoakProbeResult(ticks, finite, awake, maximumStrain,
            lab.Buddy.Recovery.AllBodiesInsideSafeBounds());
    }
}
