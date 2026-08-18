using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// CAP-2/CAP-10 capture gate for the owner-requested simultaneous grenade presentation.
/// It drives two ordinary launcher/grab input chords, verifies each runtime ID keeps its own
/// fuse, then observes the first detonation without sacrificing the second grenade and requires
/// both short-lived 3D blast accents to overlap. No fuse or detonation state is mutated directly.
/// </summary>
public sealed class GrenadeMultiCaptureScenario : IScenario
{
    private const int FuseStaggerTicks = 24;
    private const int InputTimeoutTicks = 40;

    public string Id => "grenade_multi_capture";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("multi_grenade_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        GrenadeComponent grenades = lab.Grenades;
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 firstSpawn = room.GetCenter() + new Vector2(-90.0f, -70.0f);
        Vector2 secondSpawn = room.GetCenter() + new Vector2(90.0f, -70.0f);
        int detonationsBefore = grenades.DetonationCount;

        LooseObjectBody? first = await SpawnNewGrenade(tree, lab, firstSpawn, previousRuntimeId: 0);
        int firstRuntimeId = first?.RuntimeId ?? 0;
        bool firstLive = first is not null && await ArmAndRelease(tree, lab, first);
        await Idle(tree, FuseStaggerTicks);

        LooseObjectBody? second = await SpawnNewGrenade(tree, lab, secondSpawn, firstRuntimeId);
        int secondRuntimeId = second?.RuntimeId ?? 0;
        bool secondLive = second is not null && await ArmAndRelease(tree, lab, second);

        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Mode != PresentationMode.Mii3D || lab.GrenadeVisual.AdditionalGrenadeVisualCount >= 1,
            20);

        bool firstStateExists = grenades.TryGetPresentationState(firstRuntimeId, out GrenadePresentationState firstState);
        bool secondStateExists = grenades.TryGetPresentationState(secondRuntimeId, out GrenadePresentationState secondState);
        int stagger = secondState.FuseTicksRemaining - firstState.FuseTicksRemaining;

        checks.Add(new StartupCheck(
            "two_spawned_grenades_keep_distinct_runtime_and_fuse_state",
            firstLive && secondLive &&
            firstRuntimeId != 0 && secondRuntimeId != 0 && firstRuntimeId != secondRuntimeId &&
            grenades.TrackedCount == 2 &&
            firstStateExists && secondStateExists &&
            firstState.Stage == GrenadeFuseStage.Live && secondState.Stage == GrenadeFuseStage.Live &&
            firstState.PinIsOut && secondState.PinIsOut &&
            stagger >= FuseStaggerTicks,
            $"runtime=({firstRuntimeId},{secondRuntimeId}) tracked={grenades.TrackedCount} " +
            $"stage=({firstState.Stage},{secondState.Stage}) remaining=({firstState.FuseTicksRemaining},{secondState.FuseTicksRemaining}) stagger={stagger}"));

        checks.Add(new StartupCheck(
            "simultaneous_live_grenades_have_two_3d_body_presentations",
            lab.Mode != PresentationMode.Mii3D || lab.GrenadeVisual.AdditionalGrenadeVisualCount == 1,
            $"mode={lab.Mode} additional={lab.GrenadeVisual.AdditionalGrenadeVisualCount} expected_additional={(lab.Mode == PresentationMode.Mii3D ? 1 : 0)}"));

        checks.Add(new StartupCheck(
            "grenade_punctuation_audio_is_polyphonic",
            lab.GrenadeAudio.Player.MaxPolyphony >= 8,
            $"polyphony={lab.GrenadeAudio.Player.MaxPolyphony}"));

        int maxCaptureBursts = lab.GrenadeVisual.ActiveCaptureBurstCount;
        bool firstDetonated = false;
        bool secondSurvivedFirstBlast = false;
        GrenadePresentationState secondAfterFirst = default;
        int firstBlastTick = -1;
        int secondBlastTick = -1;

        int timeout = grenades.Profile.FuseTicks + FuseStaggerTicks + 180;
        for (int tick = 0; tick < timeout && grenades.DetonationCount < detonationsBefore + 2; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            maxCaptureBursts = Mathf.Max(maxCaptureBursts, lab.GrenadeVisual.ActiveCaptureBurstCount);

            if (!firstDetonated && grenades.DetonationCount >= detonationsBefore + 1)
            {
                firstDetonated = true;
                firstBlastTick = tick;
                secondSurvivedFirstBlast =
                    grenades.TryGetPresentationState(secondRuntimeId, out secondAfterFirst) &&
                    secondAfterFirst.Stage == GrenadeFuseStage.Live &&
                    secondAfterFirst.FuseTicksRemaining > 0 &&
                    grenades.TrackedCount == 1;
            }

            if (grenades.DetonationCount >= detonationsBefore + 2)
                secondBlastTick = tick;
        }

        // One render/process turn lets the additional-body reconciler retire the final stale slot
        // without changing gameplay state, and gives the capture burst event subscriber a chance to
        // expose the second active accent in the same frame window as the first.
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        maxCaptureBursts = Mathf.Max(maxCaptureBursts, lab.GrenadeVisual.ActiveCaptureBurstCount);

        checks.Add(new StartupCheck(
            "first_detonation_leaves_second_grenade_fuse_alive",
            firstDetonated && secondSurvivedFirstBlast,
            $"first_tick={firstBlastTick} tracked_after_first={(secondSurvivedFirstBlast ? 1 : grenades.TrackedCount)} " +
            $"second_stage={secondAfterFirst.Stage} second_remaining={secondAfterFirst.FuseTicksRemaining}"));

        checks.Add(new StartupCheck(
            "staggered_grenades_both_detonate_without_singleton_clobber",
            grenades.DetonationCount == detonationsBefore + 2 &&
            secondBlastTick > firstBlastTick &&
            grenades.TrackedCount == 0 &&
            !grenades.TryGetPresentationState(firstRuntimeId, out _) &&
            !grenades.TryGetPresentationState(secondRuntimeId, out _),
            $"detonations={detonationsBefore}->{grenades.DetonationCount} ticks=({firstBlastTick},{secondBlastTick}) tracked={grenades.TrackedCount}"));

        checks.Add(new StartupCheck(
            "staggered_detonations_overlap_pooled_3d_blast_accents",
            lab.Mode != PresentationMode.Mii3D || maxCaptureBursts >= 2,
            $"mode={lab.Mode} max_active_capture_bursts={maxCaptureBursts}"));

        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;

        messages.Add($"runtime_ids=({firstRuntimeId},{secondRuntimeId}) fuse_stagger={stagger}");
        messages.Add($"detonation_ticks=({firstBlastTick},{secondBlastTick}) max_capture_bursts={maxCaptureBursts}");
        messages.Add($"grenade_audio_polyphony={lab.GrenadeAudio.Player.MaxPolyphony}");
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<LooseObjectBody?> SpawnNewGrenade(
        SceneTree tree,
        BuddyLab lab,
        Vector2 at,
        int previousRuntimeId)
    {
        // Drive the actual selected-tool gesture. Key7 is intentionally a legacy isolated-case
        // seam for GrenadeFuseScenario and clears the room before spawning, so using it here would
        // make a simultaneous-grenade test delete grenade #1 before grenade #2 even exists.
        lab.Pipeline.SelectTool(ToolId.Grenade);
        await M4ObjectScenarioSupport.MovePointer(tree, lab, at, 0);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, at, MouseButton.Right, pressed: true, MouseButtonMask.Right);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, at, MouseButton.Right, pressed: false, 0);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Launcher.CurrentLaunchable is { } body &&
                  body.SemanticContentId == ContentIds.ToolGrenade &&
                  body.RuntimeId != 0 && body.RuntimeId != previousRuntimeId,
            InputTimeoutTicks);
        return lab.Launcher.CurrentLaunchable;
    }

    private static async Task<bool> ArmAndRelease(
        SceneTree tree,
        BuddyLab lab,
        LooseObjectBody grenade)
    {
        Vector2 at = grenade.GlobalPosition;
        await M4ObjectScenarioSupport.MovePointer(tree, lab, at, 0);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, at, MouseButton.Left, pressed: true, MouseButtonMask.Left);
        bool grabbed = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Grab.IsGrabbing && lab.Grab.CurrentGrab.Target == grenade,
            InputTimeoutTicks);
        if (!grabbed)
            return false;

        await M4ObjectScenarioSupport.SetButton(
            tree,
            lab,
            at,
            MouseButton.Right,
            pressed: true,
            MouseButtonMask.Left | MouseButtonMask.Right);
        bool pinOut = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Grenades.TryGetPresentationState(grenade.RuntimeId, out GrenadePresentationState state) && state.PinIsOut,
            InputTimeoutTicks);

        await M4ObjectScenarioSupport.SetButton(
            tree, lab, at, MouseButton.Right, pressed: false, MouseButtonMask.Left);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, at, MouseButton.Left, pressed: false, 0);
        bool live = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Grenades.TryGetPresentationState(grenade.RuntimeId, out GrenadePresentationState state) &&
                  state.Stage == GrenadeFuseStage.Live && state.FuseTicksRemaining > 0,
            InputTimeoutTicks);
        return pinOut && live;
    }

    private static async Task Idle(SceneTree tree, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }
}