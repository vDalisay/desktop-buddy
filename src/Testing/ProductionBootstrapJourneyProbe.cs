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

            SceneRuntimeHost? runtime = sandbox.ActiveSceneRuntime;
            BuddyIdentityId outgoingFirstIdentity = runtime is { Actors.Count: > 0 }
                ? runtime.Actors[0].BuddyIdentityId
                : default;
            SceneId outgoingSceneId = context.SceneProgress?.ActiveSceneId ?? default;
            BuddyActorRuntime[] outgoingSecondaryActors = runtime is null
                ? []
                : runtime.Actors.Skip(1).ToArray();

            bool sceneSwitchSucceeded = !switchToOtherScene;
            bool sceneSwitchRuntimeMatches = !switchToOtherScene;
            bool sceneSwitchFirstIdentityChanged = !switchToOtherScene;
            bool sceneSwitchSecondaryTeardown = !switchToOtherScene;
            bool sceneSwitchCommitted = !switchToOtherScene;

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
                sceneSwitchFirstIdentityChanged = runtime is { Actors.Count: > 0 } &&
                    runtime.Actors[0].BuddyIdentityId != outgoingFirstIdentity;
                sceneSwitchSecondaryTeardown = outgoingSecondaryActors.All(actor =>
                    !GodotObject.IsInstanceValid(actor.Buddy) || !actor.Buddy.IsInsideTree());
                sceneSwitchCommitted = switchResult.Succeeded && !scenes.IsDirty &&
                    outgoingSceneId != scenes.ActiveSceneId;
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
                ["scene_switch_first_identity_changed"] = sceneSwitchFirstIdentityChanged,
                ["scene_switch_outgoing_secondary_torn_down"] = sceneSwitchSecondaryTeardown,
                ["scene_switch_committed"] = sceneSwitchCommitted,
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
