using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Persistence;
using DesktopBuddy.Scenes;
using Godot;
using FileAccess = Godot.FileAccess;

namespace DesktopBuddy.Testing;

/// <summary>
/// Verification hook invoked only after the normal production Bootstrap has fully composed the
/// sandbox. Unlike TestRunner journeys this never substitutes a persistence store or constructs a
/// parallel coordinator; it observes and, for explicit switch phases, drives the exact RunContext
/// and SandboxRoot production created for the tagged debug export. Shipping exports compile the
/// entire Testing tree out.
/// </summary>
public static class ProductionBootstrapJourneyProbe
{
    private const string RestartRoomName = "Restart Room";
    private const float RestartAnchorX = 0.25f;
    private const float RestartAnchorY = 0.5f;

    public static async Task<bool> RunAsync(
        RunnerArguments args,
        SandboxRoot sandbox,
        RunContext context,
        string saveRoot)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(context);

        string id = args.BootstrapJourneyId ?? string.Empty;
        int phaseIndex = args.BootstrapJourneyPhase ?? 0;
        ulong seed = args.Seed ?? 0;
        var stopwatch = Stopwatch.StartNew();
        var checks = new List<StartupCheck>();
        bool passed = true;

        try
        {
            string path = $"res://tests/journeys/{id}.json";
            if (!FileAccess.FileExists(path))
                return Finish(false, "journey_file_exists", path);

            string text;
            using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read))
                text = file.GetAsText();
            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("phases", out JsonElement phases) ||
                phases.ValueKind != JsonValueKind.Array ||
                phaseIndex < 0 || phaseIndex >= phases.GetArrayLength())
            {
                return Finish(false, "journey_phase_exists", phaseIndex.ToString());
            }

            JsonElement phase = phases[phaseIndex];
            JsonElement setup = phase.TryGetProperty("setup", out JsonElement configuredSetup)
                ? configuredSetup
                : default;
            long expectedWallet = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("expected_wallet_milli", out JsonElement wallet) && wallet.TryGetInt64(out long walletValue)
                    ? walletValue
                    : -1;
            long expectedKeyboard = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("expected_work_keyboard", out JsonElement keyboard) && keyboard.TryGetInt64(out long keyboardValue)
                    ? keyboardValue
                    : -1;
            int expectedActorCount = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("expected_actor_count", out JsonElement actorCount) && actorCount.TryGetInt32(out int actorCountValue)
                    ? actorCountValue
                    : -1;
            bool switchToOtherScene = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("switch_to_other_scene", out JsonElement switchElement) &&
                switchElement.ValueKind == JsonValueKind.True;
            bool changeCast = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("change_active_cast", out JsonElement castElement) &&
                castElement.ValueKind == JsonValueKind.True;
            bool changeLibrary = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("change_scene_library", out JsonElement libraryElement) &&
                libraryElement.ValueKind == JsonValueKind.True;
            bool prepareRestart = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("prepare_restart_room", out JsonElement prepareElement) &&
                prepareElement.ValueKind == JsonValueKind.True;
            bool expectRestart = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("expect_restart_room", out JsonElement expectElement) &&
                expectElement.ValueKind == JsonValueKind.True;

            SceneStripController? strip = sandbox.GetTree().Root.FindChild(
                nameof(SceneStripController), recursive: true, owned: false) as SceneStripController;
            SceneRuntimeHost? runtime = sandbox.ActiveSceneRuntime;
            SceneId outgoingSceneId = context.SceneProgress?.ActiveSceneId ?? default;
            BuddyActorRuntime[] outgoingSecondaryActors = runtime is null
                ? []
                : runtime.Actors.Skip(1).ToArray();

            bool sceneSwitchSucceeded = !switchToOtherScene;
            bool sceneSwitchRuntimeMatches = !switchToOtherScene;
            bool sceneSwitchSecondaryTeardown = !switchToOtherScene;
            bool sceneSwitchCommitted = !switchToOtherScene;
            bool sceneSwitchTargetEmpty = !switchToOtherScene;

            if (switchToOtherScene)
            {
                SceneProgressCoordinator scenes = context.SceneProgress
                    ?? throw new InvalidOperationException("Scene-switch phase requires Scene progress.");
                SceneDocument target = scenes.Scenes.FirstOrDefault(scene => scene.SceneId != scenes.ActiveSceneId)
                    ?? throw new InvalidOperationException("Scene-switch phase fixture has no inactive target Scene.");

                SceneRuntimeSwitchResult switchResult = await sandbox.SwitchSceneAsync(target.SceneId);
                runtime = sandbox.ActiveSceneRuntime;
                sceneSwitchSucceeded = switchResult.Succeeded && scenes.ActiveSceneId == target.SceneId;
                sceneSwitchRuntimeMatches = runtime is not null &&
                    runtime.Scene.SceneId == target.SceneId &&
                    runtime.ProgressBindings?.Scene.SceneId == target.SceneId;
                sceneSwitchSecondaryTeardown = outgoingSecondaryActors.All(actor =>
                    !GodotObject.IsInstanceValid(actor.Buddy) || !actor.Buddy.IsInsideTree());
                sceneSwitchCommitted = switchResult.Succeeded && !scenes.IsDirty &&
                    outgoingSceneId != scenes.ActiveSceneId;
                sceneSwitchTargetEmpty = runtime is { Actors.Count: 0 } &&
                    sandbox.Buddy.Rig.Parts.All(part =>
                        part.Freeze && part.CollisionLayer == 0 && part.CollisionMask == 0 && !part.Visible) &&
                    !sandbox.VisualPresenter.Visible;
            }

            bool castAddComposed = !changeCast;
            bool castRemoveComposed = !changeCast;
            bool castIdentityPreserved = !changeCast;
            bool castCommitted = !changeCast;

            if (changeCast)
            {
                SceneProgressCoordinator scenes = context.SceneProgress
                    ?? throw new InvalidOperationException("Cast phase requires Scene progress.");
                if (strip is null)
                    throw new InvalidOperationException("Cast phase requires the player-facing Scene strip.");

                int before = sandbox.ActiveSceneRuntime?.Actors.Count ?? 0;
                BuddyIdentityId added = await strip.AddCastMemberAsync(
                    default,
                    characterId: null,
                    label: "Journey Buddy",
                    new CanonicalRoomPosition(0.25f, 0.5f));
                runtime = sandbox.ActiveSceneRuntime;
                castAddComposed = added.IsValid &&
                    runtime is not null &&
                    runtime.Actors.Count == before + 1 &&
                    runtime.Actors.Any(actor => actor.BuddyIdentityId == added) &&
                    scenes.ActiveScene.BuddyPlacements.Any(p => p.BuddyIdentityId == added);

                bool removed = await strip.RemoveCastMemberAsync(added);
                runtime = sandbox.ActiveSceneRuntime;
                castRemoveComposed = removed &&
                    runtime is not null &&
                    runtime.Actors.Count == before &&
                    runtime.Actors.All(actor => actor.BuddyIdentityId != added) &&
                    scenes.ActiveScene.BuddyPlacements.All(p => p.BuddyIdentityId != added);
                // Removing a placement must never delete the Buddy itself.
                castIdentityPreserved = scenes.TryGetBuddy(added, out BuddyIdentityState? keptBuddy) &&
                    keptBuddy is not null;
                castCommitted = !scenes.IsDirty;
            }

            bool duplicateIsIndependent = !changeLibrary;
            bool duplicateCopiedBackground = !changeLibrary;
            bool deleteSwitchedSafely = !changeLibrary;
            bool deleteRemovedAssets = !changeLibrary;
            bool lastSceneProtected = !changeLibrary;

            if (changeLibrary)
            {
                SceneProgressCoordinator scenes = context.SceneProgress
                    ?? throw new InvalidOperationException("Scene library phase requires Scene progress.");
                if (strip is null)
                    throw new InvalidOperationException("Scene library phase requires the player-facing Scene strip.");

                var files = new CharacterFileSystem();
                string userRoot = ProjectSettings.GlobalizePath("user://");
                SceneId sourceId = scenes.ActiveSceneId;
                SceneDocument source = scenes.ActiveScene;
                int scenesBefore = scenes.SceneCount;

                // A painted background is the Scene-owned mutable asset the duplicate must copy
                // rather than share.
                byte[] painted = FixturePaint();
                await EnvironmentPaintStore.ForScene(files, userRoot, sourceId).SaveAsync(painted);

                SceneId copyId = await strip.DuplicateActiveSceneAsync();
                SceneDocument? copy = scenes.Scenes.FirstOrDefault(scene => scene.SceneId == copyId);
                duplicateIsIndependent = copyId.IsValid && copy is not null &&
                    scenes.SceneCount == scenesBefore + 1 &&
                    scenes.ActiveSceneId == sourceId &&
                    SceneIndexOf(scenes, copy.SceneId) == SceneIndexOf(scenes, source.SceneId) + 1 &&
                    copy.BuddyPlacements.Count == source.BuddyPlacements.Count &&
                    copy.BuddyPlacements.All(placement =>
                        source.BuddyPlacements.Any(original =>
                            original.BuddyIdentityId == placement.BuddyIdentityId) &&
                        source.BuddyPlacements.All(original => original.PlacementId != placement.PlacementId));

                EnvironmentPaintStore copyPaint = EnvironmentPaintStore.ForScene(files, userRoot, copyId);
                byte[]? copied = copyPaint.Load();
                duplicateCopiedBackground = copied is not null &&
                    copied.AsSpan().SequenceEqual(painted) &&
                    !string.Equals(
                        copyPaint.PaintPath,
                        EnvironmentPaintStore.ForScene(files, userRoot, sourceId).PaintPath,
                        StringComparison.OrdinalIgnoreCase);

                // Deleting the active Scene must leave a usable committed room behind it.
                bool deleted = await strip.DeleteActiveSceneAsync();
                runtime = sandbox.ActiveSceneRuntime;
                deleteSwitchedSafely = deleted &&
                    scenes.SceneCount == scenesBefore &&
                    scenes.Scenes.All(scene => scene.SceneId != sourceId) &&
                    scenes.ActiveSceneId != sourceId &&
                    runtime is not null && runtime.Scene.SceneId == scenes.ActiveSceneId &&
                    !scenes.IsDirty;
                deleteRemovedAssets = !files.DirectoryExists(Path.Combine(
                    userRoot,
                    SceneStoragePaths.SceneRoot(sourceId).Replace('/', Path.DirectorySeparatorChar)));

                while (scenes.SceneCount > 1)
                {
                    if (!await strip.DeleteActiveSceneAsync())
                        break;
                }
                lastSceneProtected = scenes.SceneCount == 1 && !await strip.DeleteActiveSceneAsync();
                // Every teardown above freed its actors; the shared checks below must not read a
                // roster from before the last deletion.
                runtime = sandbox.ActiveSceneRuntime;
            }

            bool restartPrepared = !prepareRestart;
            bool restartRestored = !expectRestart;

            if (prepareRestart)
            {
                SceneProgressCoordinator scenes = context.SceneProgress
                    ?? throw new InvalidOperationException("Restart phase requires Scene progress.");
                if (strip is null)
                    throw new InvalidOperationException("Restart phase requires the player-facing Scene strip.");

                SceneLibraryResult renamed = scenes.RenameScene(scenes.ActiveSceneId, RestartRoomName);
                BuddyIdentityId added = await strip.AddCastMemberAsync(
                    default,
                    characterId: null,
                    label: "Restart Buddy",
                    new CanonicalRoomPosition(RestartAnchorX, RestartAnchorY));
                await EnvironmentPaintStore
                    .ForScene(new CharacterFileSystem(), ProjectSettings.GlobalizePath("user://"), scenes.ActiveSceneId)
                    .SaveAsync(FixturePaint());
                await scenes.FlushAsync(force: true);
                runtime = sandbox.ActiveSceneRuntime;
                restartPrepared = renamed.Succeeded && added.IsValid && !scenes.IsDirty &&
                    runtime is not null && runtime.Actors.Count == 3;
            }

            if (expectRestart)
            {
                SceneProgressCoordinator scenes = context.SceneProgress
                    ?? throw new InvalidOperationException("Restart phase requires Scene progress.");
                SceneDocument active = scenes.ActiveScene;
                Rect2 bounds = sandbox.Boundaries.InnerBounds;
                byte[]? restoredPaint = EnvironmentPaintStore
                    .ForScene(new CharacterFileSystem(), ProjectSettings.GlobalizePath("user://"), active.SceneId)
                    .Load();

                restartRestored = string.Equals(active.Name, RestartRoomName, StringComparison.Ordinal) &&
                    runtime is not null &&
                    runtime.Actors.Count == active.BuddyPlacements.Count &&
                    active.BuddyPlacements.All(placement =>
                        scenes.TryGetBuddy(placement.BuddyIdentityId, out BuddyIdentityState? identity) &&
                        identity is not null &&
                        runtime.Actors.Any(actor => actor.BuddyIdentityId == placement.BuddyIdentityId)) &&
                    active.BuddyPlacements.Any(placement =>
                        Mathf.IsEqualApprox(placement.Position.X, RestartAnchorX) &&
                        Mathf.IsEqualApprox(placement.Position.Y, RestartAnchorY)) &&
                    runtime.Actors.All(actor => bounds.HasPoint(actor.Buddy.Rig.Torso.GlobalPosition)) &&
                    restoredPaint is not null && restoredPaint.AsSpan().SequenceEqual(FixturePaint());
            }

            bool actorCountMatches = expectedActorCount < 0 ||
                (runtime is not null && runtime.Actors.Count == expectedActorCount);
            bool actorBindingsMatch = runtime is not null && runtime.ProgressBindings is not null;
            bool actorsSharePlayer = runtime is not null && context.SceneProgress is not null;
            bool actorBuddyStatesIndependent = runtime is not null;
            bool actorPositionsDistinct = runtime is not null;
            bool firstActorNotLegacyPrimary = runtime is not null &&
                runtime.Actors.Count > 0 &&
                runtime.Actors[0].BuddyIdentityId != BuddyIdentityId.LegacyPrimary;
            bool characterSelectionMatchesFirstActor = runtime is not null &&
                runtime.Actors.Count > 0 &&
                context.CharacterSelection is not null;

            if (runtime is not null && runtime.ProgressBindings is { } progressBindings)
            {
                var seenBuddyStates = new HashSet<BuddyIdentityState>();
                for (int index = 0; index < runtime.Actors.Count; index++)
                {
                    BuddyActorRuntime actor = runtime.Actors[index];
                    BuddyRuntimeProgressBinding runtimeBinding = runtime.ProgressFor(actor);
                    SceneBuddyProgressBinding sceneBinding = progressBindings.ForPlacement(actor.PlacementId);
                    actorBindingsMatch &=
                        sceneBinding.Placement.PlacementId == actor.PlacementId &&
                        sceneBinding.Placement.BuddyIdentityId == actor.BuddyIdentityId &&
                        ReferenceEquals(sceneBinding.Progress.BuddyProgress, runtimeBinding.BuddyProgress) &&
                        ReferenceEquals(runtimeBinding.BuddyProgress, actor.Damage.ProgressBinding.BuddyProgress);
                    actorsSharePlayer &=
                        ReferenceEquals(runtimeBinding.PlayerProgress, context.SceneProgress?.Player) &&
                        ReferenceEquals(actor.Damage.ProgressBinding.PlayerProgress, context.SceneProgress?.Player);
                    BuddyIdentityState? buddyState = actor.Damage.ProgressBinding.BuddyProgress;
                    actorBuddyStatesIndependent &= buddyState is not null && seenBuddyStates.Add(buddyState);

                    for (int other = 0; other < index; other++)
                    {
                        actorPositionsDistinct &=
                            actor.Buddy.Rig.Torso.GlobalPosition.DistanceTo(
                                runtime.Actors[other].Buddy.Rig.Torso.GlobalPosition) > 8.0f;
                    }
                }

                if (runtime.Actors.Count > 0 && context.CharacterSelection is not null)
                {
                    BuddyRuntimeProgressBinding firstBinding = runtime.ProgressFor(runtime.Actors[0]);
                    characterSelectionMatchesFirstActor =
                        firstBinding.BuddyProgress?.CharacterId == context.CharacterSelection.ActiveCharacterId;
                }
            }

            var state = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["sandbox_composed"] = sandbox.IsInsideTree(),
                ["tag_initial_demo"] = DemoScope.IsSteamDemo && !DemoScope.IsNextFestDemo && !DemoScope.IncludesScenes,
                ["tag_next_fest"] = DemoScope.IsSteamDemo && DemoScope.IsNextFestDemo && DemoScope.IncludesScenes,
                ["split_scene_progress"] = context.SceneProgress is not null && context.UsesSplitSceneProgress,
                ["legacy_scene_progress"] = context.SceneProgress is null && !context.UsesSplitSceneProgress,
                ["scene_strip_composed"] = strip is not null,
                ["scene_management_controls"] = strip is not null &&
                    strip.SceneStripRoot.FindChild("SceneCreateButton", recursive: true, owned: false) is Button &&
                    strip.SceneStripRoot.FindChild("SceneMenu", recursive: true, owned: false) is MenuButton,
                ["scene_manifest_exists"] = System.IO.File.Exists(Path.Combine(saveRoot, SceneProgressTransactionStore.ManifestFileName)),
                ["wallet_preserved"] = expectedWallet < 0 || context.PlayerProgress.BalanceMilliCredits == expectedWallet,
                ["work_preserved"] = expectedKeyboard < 0 ||
                    (context.WorkProgress is not null && context.WorkProgress.Lifetime.KeyboardPresses == expectedKeyboard),
                ["scene_actor_count_matches"] = actorCountMatches,
                ["scene_actor_bindings_match"] = actorBindingsMatch,
                ["scene_actors_share_player"] = actorsSharePlayer,
                ["scene_actor_buddy_states_independent"] = actorBuddyStatesIndependent,
                ["scene_actor_positions_distinct"] = actorPositionsDistinct,
                ["scene_first_actor_not_legacy_primary"] = firstActorNotLegacyPrimary,
                ["character_selection_matches_first_actor"] = characterSelectionMatchesFirstActor,
                ["scene_switch_succeeded"] = sceneSwitchSucceeded,
                ["scene_switch_runtime_matches_target"] = sceneSwitchRuntimeMatches,
                ["scene_switch_outgoing_secondary_torn_down"] = sceneSwitchSecondaryTeardown,
                ["scene_switch_committed"] = sceneSwitchCommitted,
                ["scene_switch_target_empty"] = sceneSwitchTargetEmpty,
                ["scene_cast_add_composed"] = castAddComposed,
                ["scene_cast_remove_composed"] = castRemoveComposed,
                ["scene_cast_identity_preserved"] = castIdentityPreserved,
                ["scene_cast_committed"] = castCommitted,
                ["scene_duplicate_independent"] = duplicateIsIndependent,
                ["scene_duplicate_copied_background"] = duplicateCopiedBackground,
                ["scene_delete_switched_safely"] = deleteSwitchedSafely,
                ["scene_delete_removed_assets"] = deleteRemovedAssets,
                ["scene_last_scene_protected"] = lastSceneProtected,
                ["scene_restart_prepared"] = restartPrepared,
                ["scene_restart_restored"] = restartRestored,
            };

            if (phase.TryGetProperty("assertions", out JsonElement assertions) && assertions.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement assertion in assertions.EnumerateArray())
                {
                    string predicate = assertion.TryGetProperty("predicate", out JsonElement p)
                        ? p.GetString() ?? string.Empty
                        : string.Empty;
                    bool expected = !assertion.TryGetProperty("equals", out JsonElement e) || e.GetBoolean();
                    bool known = state.TryGetValue(predicate, out bool actual);
                    bool ok = known && actual == expected;
                    checks.Add(new StartupCheck(
                        $"assert:{predicate}",
                        ok,
                        known ? $"expected={expected} actual={actual}" : "unknown production-bootstrap predicate"));
                    passed &= ok;
                }
            }
            else
            {
                checks.Add(new StartupCheck("journey_has_assertions", false, "phase has no assertions array"));
                passed = false;
            }
        }
        catch (Exception exception)
        {
            checks.Add(new StartupCheck("bootstrap_probe_exception", false, exception.ToString()));
            passed = false;
        }

        return Finish(passed, null, null);

        static byte[] FixturePaint()
        {
            var pixels = new byte[EnvironmentCanvasPolicy.Bytes];
            for (int index = 0; index < pixels.Length; index += EnvironmentCanvasPolicy.BytesPerPixel)
            {
                pixels[index] = 12;
                pixels[index + 1] = 34;
                pixels[index + 2] = 56;
                pixels[index + 3] = 255;
            }
            return pixels;
        }

        static int SceneIndexOf(SceneProgressCoordinator scenes, SceneId sceneId)
        {
            for (int index = 0; index < scenes.Scenes.Count; index++)
            {
                if (scenes.Scenes[index].SceneId == sceneId)
                    return index;
            }
            return -1;
        }

        bool Finish(bool ok, string? extraName, string? detail)
        {
            if (extraName is not null)
                checks.Add(new StartupCheck(extraName, ok, detail ?? string.Empty));
            stopwatch.Stop();
            VerdictWriter.Write(
                "journey",
                $"{id}_production_phase_{phaseIndex + 1}",
                seed,
                ok,
                checks,
                new[] { $"seed={seed}", $"saveRoot={saveRoot}" },
                stopwatch.ElapsedMilliseconds,
                args.ArtifactsDir);
            Log.Info("BootstrapJourney", $"Production Bootstrap phase {phaseIndex + 1} {(ok ? "PASSED" : "FAILED")}.");
            return ok;
        }
    }
}
