using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// What the rest of the sandbox can do to a grenade (owner instruction 2026-08-21). Every
/// rule that turns an outside event into a pin pull or a blast lives here, in one table, so
/// the tools do not each carry their own opinion about grenades:
///
/// <list type="bullet">
///   <item>A swung tool or a Nerf dart <b>knocks the pin out</b>. The grenade is not being
///   held, so the countdown starts on the same tick and it goes off where it lands.</item>
///   <item>Three pistol rounds, or one shotgun shell, <b>set it off outright</b>.</item>
///   <item>Another grenade's blast sets off every grenade inside
///   <see cref="GrenadeProfile.ChainRadiusPx"/>, one tick later, so a pile goes up as a chain
///   rather than as one recursive call stack.</item>
///   <item>The fire sprayer <b>cooks</b> it: heat climbs only while flame is actually on the
///   body and falls back when it is not, so holding the trigger is the whole mechanic.</item>
/// </list>
/// </summary>
public partial class GrenadeComponent
{
    /// <summary>How far past its own edge a droplet still counts as flame on the body.</summary>
    private const float FlameContactSlackPx = 6.0f;

    /// <summary>
    /// The fire sprayer whose droplets can cook a grenade. Wired by the sandbox root after
    /// both components exist rather than exported, so no scene has to be re-saved and a
    /// scenario that never builds a sprayer simply leaves it null.
    /// </summary>
    public FireSprayerComponent? Flame { get; set; }

    /// <summary>Grenades set off by something other than their own fuse, for readouts.</summary>
    public int ForcedDetonationCount { get; private set; }

    /// <summary>Grenades set off by another grenade's blast, for readouts.</summary>
    public int ChainedDetonationCount { get; private set; }

    /// <summary>Pins knocked out by a strike rather than by the player, for readouts.</summary>
    public int StruckPinPullCount { get; private set; }

    /// <summary>
    /// How cooked one grenade is, 0 to 1, for the presenter's heat tint. Unknown or untracked
    /// runtime IDs read as stone cold.
    /// </summary>
    public float HeatOf(int runtimeId) =>
        runtimeId != 0 && _tracked.TryGetValue(runtimeId, out TrackedGrenadeState? state)
            ? Mathf.Clamp(state.HeatTicks / Mathf.Max(1.0f, Profile.FlameCookTicks), 0.0f, 1.0f)
            : 0.0f;

    /// <summary>
    /// Something hit a loose object hard. If it was a grenade, this is where that becomes a
    /// pin pull or a blast; anything else is ignored, so callers may report every strike.
    /// </summary>
    public void NotifyStruck(LooseObjectBody? body, string byContentId)
    {
        if (!IsInitialized || !IsLiveGrenade(body) ||
            !_tracked.TryGetValue(body!.RuntimeId, out TrackedGrenadeState? state))
        {
            return;
        }

        switch (byContentId)
        {
            // A swung tool or a dart knocks the pin out. Nothing about the impact is strong
            // enough to set off the filler — it is the pin that gives.
            case ContentIds.ToolBaseballBat:
            case ContentIds.ToolBoxingGlove:
            case ContentIds.ToolNerfBlaster:
                state.StruckPinPull = true;
                break;

            case ContentIds.ToolShotgun:
                state.ForcedDetonation = true;
                break;

            case ContentIds.ToolPistol:
                state.PistolHits++;
                if (state.PistolHits >= Profile.PistolHitsToDetonate)
                    state.ForcedDetonation = true;
                break;
        }
    }

    /// <summary>
    /// Heat for one grenade this tick. Rises while a live droplet is touching it and falls
    /// back at <see cref="GrenadeProfile.FlameCoolFactor"/> of that rate when it is not.
    /// </summary>
    private void TickFlameCook(TrackedGrenadeState state)
    {
        bool burning = IsUnderFlame(state.Body);
        state.HeatTicks = Mathf.Max(
            0.0f,
            state.HeatTicks + (burning ? 1.0f : -Profile.FlameCoolFactor));
        if (state.HeatTicks >= Profile.FlameCookTicks)
            state.ForcedDetonation = true;
    }

    private bool IsUnderFlame(LooseObjectBody body)
    {
        FireSprayerComponent? flame = Flame;
        if (!GodotObject.IsInstanceValid(flame) || !flame!.IsInitialized || !flame.IsSpraying)
            return false;

        Vector2 center = body.GlobalPosition;
        foreach (SprayDropletBody droplet in flame.Droplets)
        {
            if (!GodotObject.IsInstanceValid(droplet) || droplet.State != SprayDropletState.Live)
                continue;

            float reach = body.Radius + droplet.Radius + FlameContactSlackPx;
            if (center.DistanceSquaredTo(droplet.GlobalPosition) <= reach * reach)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Arms every other live grenade inside this blast. They go off on the next tick rather
    /// than inside this call, which keeps a pile of grenades a chain of single detonations
    /// instead of a recursion the registry is being mutated underneath.
    /// </summary>
    private void ChainNeighbours(LooseObjectBody source, Vector2 center)
    {
        float radius = Profile.ChainRadiusPx;
        if (radius <= 0.0f)
            return;

        foreach (TrackedGrenadeState state in _tracked.Values)
        {
            if (state.Body == source || !IsLiveGrenade(state.Body) ||
                state.Phase.Stage == Domain.Tools.GrenadeFuseStage.Detonated ||
                state.ForcedDetonation)
            {
                continue;
            }

            float distance = Mathf.Max(
                0.0f, center.DistanceTo(state.Body.GlobalPosition) - state.Body.Radius);
            if (distance > radius)
                continue;

            state.ForcedDetonation = true;
            state.ChainedByBlast = true;
        }
    }

    /// <summary>Drains the one-shot strike flags a fuse tick is about to consume.</summary>
    private (bool StruckPinPull, bool ForcedDetonation) ConsumeStrikeFlags(TrackedGrenadeState state)
    {
        bool struck = state.StruckPinPull;
        bool forced = state.ForcedDetonation;
        state.StruckPinPull = false;
        if (forced)
        {
            ForcedDetonationCount++;
            if (state.ChainedByBlast)
                ChainedDetonationCount++;
        }
        if (struck)
            StruckPinPullCount++;
        return (struck, forced);
    }
}
