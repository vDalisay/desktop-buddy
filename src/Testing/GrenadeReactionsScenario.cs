using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// What the rest of the sandbox can do to a grenade (owner instruction 2026-08-21). Every
/// rule in <see cref="GrenadeComponent.NotifyStruck"/> gets its own case, plus the two that
/// do not come through a strike at all: one grenade setting off the next, and the fire
/// sprayer cooking one off over three seconds.
///
/// <para>The wiring is checked as well as the rules. The first build of this shipped with the
/// tool and gun strike events connected in <see cref="BuddyLab"/> only, and the game runs
/// <c>SandboxRoot</c> — so every one of these worked in a scenario and none of them worked in
/// the game. <c>flame_source_is_wired</c> is the cheap standing guard against that repeating.</para>
/// </summary>
public sealed class GrenadeReactionsScenario : IScenario
{
    public string Id => "grenade_reactions";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("grenade_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        GrenadeComponent grenades = lab.Grenades;

        checks.Add(new StartupCheck(
            "flame_source_is_wired",
            GodotObject.IsInstanceValid(grenades.Flame),
            $"flame={(GodotObject.IsInstanceValid(grenades.Flame) ? "wired" : "null")}"));

        // --- a strike knocks the pin out and the fuse starts where it lies ---
        foreach (string striker in new[]
                 {
                     ContentIds.ToolBaseballBat,
                     ContentIds.ToolBoxingGlove,
                     ContentIds.ToolNerfBlaster,
                 })
        {
            ClearField(lab);
            LooseObjectBody? grenade = await PlaceGrenade(tree, lab);
            if (grenade is null)
            {
                checks.Add(new StartupCheck($"strike_{Slug(striker)}_pulls_the_pin", false, "no grenade"));
                continue;
            }

            bool pinnedBefore = Stage(grenades, grenade) == GrenadeFuseStage.Pinned;
            grenades.NotifyStruck(grenade, striker);
            await Ticks(tree, 2);
            GrenadeFuseStage after = Stage(grenades, grenade);
            checks.Add(new StartupCheck(
                $"strike_{Slug(striker)}_pulls_the_pin",
                pinnedBefore && after == GrenadeFuseStage.Live,
                $"pinned_before={pinnedBefore} after={after}"));
        }

        // --- one shell, or three rounds, and it goes off outright ---
        ClearField(lab);
        LooseObjectBody? shotgunTarget = await PlaceGrenade(tree, lab);
        int detonationsBefore = grenades.DetonationCount;
        if (shotgunTarget is not null)
            grenades.NotifyStruck(shotgunTarget, ContentIds.ToolShotgun);
        await Ticks(tree, 2);
        checks.Add(new StartupCheck(
            "one_shotgun_shell_detonates_it",
            shotgunTarget is not null && grenades.DetonationCount == detonationsBefore + 1,
            $"detonations={detonationsBefore}->{grenades.DetonationCount}"));

        ClearField(lab);
        LooseObjectBody? pistolTarget = await PlaceGrenade(tree, lab);
        detonationsBefore = grenades.DetonationCount;
        var pistolStages = new List<GrenadeFuseStage>();
        for (int round = 0; round < 3 && pistolTarget is not null; round++)
        {
            grenades.NotifyStruck(pistolTarget, ContentIds.ToolPistol);
            await Ticks(tree, 2);
            pistolStages.Add(Stage(grenades, pistolTarget));
        }

        checks.Add(new StartupCheck(
            "three_pistol_rounds_detonate_it_and_fewer_do_not",
            pistolTarget is not null &&
            pistolStages.Count == 3 &&
            pistolStages[0] != GrenadeFuseStage.Detonated &&
            pistolStages[1] != GrenadeFuseStage.Detonated &&
            grenades.DetonationCount == detonationsBefore + 1,
            $"stages=[{string.Join(',', pistolStages)}] " +
            $"detonations={detonationsBefore}->{grenades.DetonationCount}"));

        // --- a blast sets off its neighbours ---
        ClearField(lab);
        LooseObjectBody? first = await PlaceGrenade(tree, lab);
        LooseObjectBody? second = await PlaceGrenade(tree, lab, new Vector2(40.0f, 0.0f));
        bool bothPlaced = first is not null && second is not null && first != second;
        detonationsBefore = grenades.DetonationCount;
        int chainedBefore = grenades.ChainedDetonationCount;
        if (bothPlaced)
            grenades.NotifyStruck(first, ContentIds.ToolShotgun);
        // One tick to go off, a few more for the neighbour it armed on the way out.
        await Ticks(tree, 8);
        checks.Add(new StartupCheck(
            "a_blast_sets_off_the_grenade_beside_it",
            bothPlaced &&
            grenades.DetonationCount >= detonationsBefore + 2 &&
            grenades.ChainedDetonationCount == chainedBefore + 1,
            $"placed={bothPlaced} detonations={detonationsBefore}->{grenades.DetonationCount} " +
            $"chained={chainedBefore}->{grenades.ChainedDetonationCount}"));

        // --- heat comes from the flame and from nothing else ---
        ClearField(lab);
        int forcedBeforeSoak = grenades.ForcedDetonationCount;
        LooseObjectBody? cooked = await PlaceGrenade(tree, lab);
        float heatCold = cooked is null ? -1.0f : grenades.HeatOf(cooked.RuntimeId);
        checks.Add(new StartupCheck(
            "an_unlit_grenade_stays_cold",
            cooked is not null && Mathf.IsZeroApprox(heatCold),
            $"heat={heatCold:F3}"));

        // A grenade is not something the fuse ever heats on its own: without flame the heat
        // must still be zero after a full cook window has gone by.
        await Ticks(tree, grenades.Profile.FlameCookTicks + 30);
        float heatAfterSoak = cooked is not null && GodotObject.IsInstanceValid(cooked)
            ? grenades.HeatOf(cooked.RuntimeId)
            : 0.0f;
        checks.Add(new StartupCheck(
            "three_seconds_without_flame_is_not_a_cook",
            Mathf.IsZeroApprox(heatAfterSoak) &&
            grenades.ForcedDetonationCount == forcedBeforeSoak,
            $"heat={heatAfterSoak:F3} forced={forcedBeforeSoak}->{grenades.ForcedDetonationCount}"));

        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// Empties the field between cases. A chained blast is a feature, so leaving live grenades
    /// lying around from an earlier case makes every detonation count ambiguous.
    /// </summary>
    private static void ClearField(BuddyLab lab)
    {
        if (lab.Grab.IsGrabbing)
            lab.Grab.Release(countsAsThrow: false);
        lab.Launcher.CancelImmediately();
        lab.Grenades.CancelImmediately();
        for (int slot = 0; slot < LooseObjectRegistry.Capacity; slot++)
        {
            LooseObjectBody? body = lab.Objects.BodyAt(slot);
            if (body is null)
                continue;
            lab.Objects.Unregister(body);
            if (GodotObject.IsInstanceValid(body))
                body.QueueFree();
        }
    }

    private static string Slug(string contentId) => contentId.Replace("tool.", string.Empty);

    private static GrenadeFuseStage Stage(GrenadeComponent grenades, LooseObjectBody? body) =>
        body is not null && GodotObject.IsInstanceValid(body) &&
        grenades.TryGetPresentationState(body.RuntimeId, out GrenadePresentationState state)
            ? state.Stage
            : GrenadeFuseStage.Detonated;

    /// <summary>
    /// Places one grenade through the launcher the way the grenade tool does, and returns the
    /// body the component adopted for it.
    /// </summary>
    private static async Task<LooseObjectBody?> PlaceGrenade(SceneTree tree, BuddyLab lab, Vector2 offset = default)
    {
        lab.Pipeline.SelectTool(ToolId.Grenade);
        Vector2 at = lab.Boundaries.InnerBounds.GetCenter() + offset + new Vector2(0.0f, -60.0f);
        lab.Launcher.RequestSpawn(ContentIds.ToolGrenade, at);
        await Ticks(tree, 3);

        LooseObjectBody? newest = null;
        for (int slot = 0; slot < LooseObjectRegistry.Capacity; slot++)
        {
            LooseObjectBody? body = lab.Objects.BodyAt(slot);
            if (GodotObject.IsInstanceValid(body) &&
                body!.SemanticContentId == ContentIds.ToolGrenade &&
                lab.Grenades.TryGetPresentationState(body.RuntimeId, out GrenadePresentationState state) &&
                state.Stage == GrenadeFuseStage.Pinned)
            {
                newest = body;
            }
        }

        return newest;
    }

    private static async Task Ticks(SceneTree tree, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }
}
