using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopBuddy.Achievements;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Sandbox;
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
            bool customizeFocused = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("customize_focused_buddy", out JsonElement customizeElement) &&
                customizeElement.ValueKind == JsonValueKind.True;
            bool buildRoom = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("build_room", out JsonElement buildElement) &&
                buildElement.ValueKind == JsonValueKind.True;
            bool expectBuiltRoom = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("expect_built_room", out JsonElement builtElement) &&
                builtElement.ValueKind == JsonValueKind.True;
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
            bool focusFollowsSelection = !changeCast;
            bool focusRecoversAfterRemoval = !changeCast;
            bool achievementObserversFollowRoster = !changeCast;

            if (changeCast)
            {
                SceneProgressCoordinator scenes = context.SceneProgress
                    ?? throw new InvalidOperationException("Cast phase requires Scene progress.");
                if (strip is null)
                    throw new InvalidOperationException("Cast phase requires the player-facing Scene strip.");

                int before = sandbox.ActiveSceneRuntime?.Actors.Count ?? 0;
                // A fresh room focuses its first cast member until the player says otherwise.
                bool focusStartsOnFirstActor = sandbox.FocusedActor is not null &&
                    ReferenceEquals(sandbox.FocusedActor, sandbox.ActiveSceneRuntime?.Actors[0]);
                BuddyIdentityId added = await strip.AddCastMemberAsync(
                    default,
                    characterId: null,
                    label: "Journey Buddy",
                    new CanonicalRoomPosition(0.0f, 1.0f));
                runtime = sandbox.ActiveSceneRuntime;
                castAddComposed = added.IsValid &&
                    runtime is not null &&
                    runtime.Actors.Count == before + 1 &&
                    runtime.Actors.Any(actor => actor.BuddyIdentityId == added) &&
                    scenes.ActiveScene.BuddyPlacements.Any(p => p.BuddyIdentityId == added);

                BuddyActorRuntime? addedActor = runtime?.Actors
                    .FirstOrDefault(actor => actor.BuddyIdentityId == added);
                castAddComposed &= addedActor is not null &&
                    addedActor.Buddy.Recovery.AllBodiesInsideSafeBounds();
                focusFollowsSelection = focusStartsOnFirstActor &&
                    addedActor is not null &&
                    // Adding a Buddy must not steal the player's selection; choosing one must.
                    !ReferenceEquals(sandbox.FocusedActor, addedActor) &&
                    sandbox.TryFocusActor(addedActor.PlacementId) &&
                    ReferenceEquals(sandbox.FocusedActor, addedActor) &&
                    sandbox.FocusedBuddyCharacterId is null;

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
                // Qualifying actions must keep being observed on exactly the live cast.
                achievementObserversFollowRoster = sandbox.GetTree().Root.FindChild(
                        nameof(AchievementBootstrap), recursive: true, owned: false) is AchievementBootstrap achievements &&
                    runtime is not null &&
                    achievements.ObservedActorCount == runtime.Actors.Count;
                focusRecoversAfterRemoval = sandbox.FocusedActor is not null &&
                    sandbox.FocusedActor.BuddyIdentityId != added &&
                    runtime is not null &&
                    runtime.Actors.Contains(sandbox.FocusedActor);
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
                string userRoot = saveRoot;
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

            bool focusedCustomizationTargetsSelection = !customizeFocused;

            if (customizeFocused)
            {
                SceneProgressCoordinator scenes = context.SceneProgress
                    ?? throw new InvalidOperationException("Customization phase requires Scene progress.");
                CharacterStore characters = context.Characters
                    ?? throw new InvalidOperationException("Customization phase requires the Character store.");
                SceneRuntimeHost live = sandbox.ActiveSceneRuntime
                    ?? throw new InvalidOperationException("Customization phase requires a live Scene roster.");
                if (live.Actors.Count < 2)
                    throw new InvalidOperationException("Customization phase requires a second cast member.");

                Guid look = Guid.NewGuid();
                await characters.SaveAsync(CharacterDocument.CreateDefault(look, "Journey Look"), default);

                BuddyActorRuntime authored = live.Actors[0];
                BuddyActorRuntime secondary = live.Actors[1];
                Guid? authoredCharacterBefore = live.ProgressFor(authored).BuddyProgress?.CharacterId;

                bool focusedSecondary = sandbox.TryFocusActor(secondary.PlacementId);
                bool appliedToSecondary = await sandbox.TryApplyCharacterToFocusedBuddyAsync(look);
                // Customization opens against whatever this seam reports, so it must name the
                // focused Buddy's Character rather than the compatibility selection.
                bool customizationOpensOnSelection =
                    sandbox.TryGetFocusedBuddyCharacterId(out Guid? focusedCharacter) &&
                    focusedCharacter == look &&
                    context.CharacterSelection?.ActiveCharacterId != look;
                // The authored actor keeps the one compatibility selection path; only a secondary
                // Buddy is dressed through the focused seam.
                bool authoredUsesCompatibilityPath = sandbox.TryFocusActor(authored.PlacementId) &&
                    !await sandbox.TryApplyCharacterToFocusedBuddyAsync(look);

                focusedCustomizationTargetsSelection = focusedSecondary && appliedToSecondary &&
                    customizationOpensOnSelection && authoredUsesCompatibilityPath &&
                    live.ProgressFor(secondary).BuddyProgress?.CharacterId == look &&
                    live.ProgressFor(authored).BuddyProgress?.CharacterId == authoredCharacterBefore &&
                    !scenes.IsDirty;
            }

            bool buildModePausesAndPlaces = !buildRoom;
            bool buildRemovalPicksThePartUnderThePointer = !buildRoom;
            bool buildCommitsOnReturnToPlay = !buildRoom;
            bool buildPreviewRightOfList = !buildRoom;
            bool buildSurfaceSupportsBuddy = !buildRoom;
            bool builtRoomRestored = !expectBuiltRoom;

            if (buildRoom)
            {
                SceneProgressCoordinator scenes = context.SceneProgress
                    ?? throw new InvalidOperationException("Build phase requires Scene progress.");
                BuildModeController build = sandbox.GetTree().Root.FindChild(
                        nameof(BuildModeController), recursive: true, owned: false) as BuildModeController
                    ?? throw new InvalidOperationException("Build phase requires the player-facing Build controls.");

                Rect2 bounds = sandbox.Boundaries.InnerBounds;
                Vector2 beamPoint = bounds.Position + bounds.Size * new Vector2(0.35f, 0.7f);
                Vector2 wheelPoint = bounds.Position + bounds.Size * new Vector2(0.65f, 0.7f);

                build.Toggle();
                await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                bool entered = build.IsActive &&
                    sandbox.Lifecycle.PauseCoordinator.Contains(GameplayPauseReason.BuildMode);
                Control? list = build.FindChild("BuildModePartList", recursive: true, owned: false) as Control;
                Control? preview = build.FindChild("BuildModePartPreview", recursive: true, owned: false) as Control;
                buildPreviewRightOfList = list is not null && preview is not null &&
                    preview.GlobalPosition.X >= list.GlobalPosition.X + list.Size.X;

                build.SelectPart(SandboxPartCatalogue.WoodBeam);
                build.PlaceSelectedPartAt(beamPoint);
                build.SelectPart(SandboxPartCatalogue.Wheel);
                build.PlaceSelectedPartAt(wheelPoint);

                buildModePausesAndPlaces = entered &&
                    scenes.ActiveSandbox.Count == 2 &&
                    sandbox.BuiltParts.Count == 2 &&
                    scenes.ActiveSandbox.Parts.All(part => sandbox.BuiltParts.ContainsKey(part.PartId));

                // Right-clicking a part removes that part, not merely the last one placed.
                build.RemovePartAt(wheelPoint);
                buildRemovalPicksThePartUnderThePointer =
                    scenes.ActiveSandbox.Count == 1 &&
                    sandbox.BuiltParts.Count == 1 &&
                    scenes.ActiveSandbox.Parts[0].DefinitionId == SandboxPartCatalogue.WoodBeam;

                await build.LeaveAsync();
                buildCommitsOnReturnToPlay = !build.IsActive &&
                    !sandbox.Lifecycle.PauseCoordinator.Contains(GameplayPauseReason.BuildMode) &&
                    !scenes.IsDirty;

                if (runtime is { Actors.Count: > 0 } &&
                    sandbox.BuiltParts.Values.FirstOrDefault() is SandboxPartBody beam)
                {
                    BuddyActorRuntime actor = runtime.Actors[0];
                    beam.Freeze = true;
                    // Buddies collide with each other by default now, so a neighbour that wandered
                    // onto the beam would catch the drop and this would measure that Buddy's head
                    // instead of the build surface. Park the rest of the cast at the far wall.
                    Vector2 farWall = sandbox.PlannedSceneBuddyOrigin(
                        new Vector2(bounds.End.X, beam.GlobalPosition.Y));
                    foreach (BuddyActorRuntime other in runtime.Actors.Skip(1))
                    {
                        other.Buddy.Recovery.SafePoseOrigin = farWall;
                        other.Buddy.Rig.ResetToSafePose(farWall);
                    }
                    Vector2 origin = new(
                        beam.GlobalPosition.X,
                        beam.GlobalPosition.Y - 8.0f - 17.0f - 55.0f);
                    actor.Buddy.Recovery.SafePoseOrigin = origin;
                    actor.Buddy.Rig.ResetToSafePose(origin);
                    for (int frame = 0; frame < 30; frame++)
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                    buildSurfaceSupportsBuddy =
                        actor.Buddy.Rig.LeftFoot.HasSupportContact ||
                        actor.Buddy.Rig.RightFoot.HasSupportContact;
                    Log.Info("BootstrapJourney",
                        $"support probe: beam={beam.GlobalPosition} torso={actor.Buddy.Rig.Torso.GlobalPosition} " +
                        $"left=[{string.Join(",", actor.Buddy.Rig.LeftFoot.GetCollidingBodies().Select(b => (b is PuppetPartBody lp && actor.OwnsPart(lp) ? "own:" : "other:") + b.Name))}] " +
                        $"right=[{string.Join(",", actor.Buddy.Rig.RightFoot.GetCollidingBodies().Select(b => (b is PuppetPartBody rp && actor.OwnsPart(rp) ? "own:" : "other:") + b.Name))}] " +
                        $"others=[{string.Join(",", runtime.Actors.Skip(1).Select(a => a.Buddy.Rig.Torso.GlobalPosition.ToString()))}]");
                }
            }

            if (expectBuiltRoom)
            {
                SceneProgressCoordinator scenes = context.SceneProgress
                    ?? throw new InvalidOperationException("Built-room phase requires Scene progress.");
                builtRoomRestored = scenes.ActiveSandbox.Count == 1 &&
                    scenes.ActiveSandbox.Parts[0].DefinitionId == SandboxPartCatalogue.WoodBeam &&
                    sandbox.BuiltParts.Count == 1 &&
                    sandbox.BuiltParts.ContainsKey(scenes.ActiveSandbox.Parts[0].PartId);
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
                    .ForScene(new CharacterFileSystem(), saveRoot, scenes.ActiveSceneId)
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
                    .ForScene(new CharacterFileSystem(), saveRoot, active.SceneId)
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
            bool sceneActorsFullyInitialized = runtime is not null && runtime.Actors.All(actor =>
                actor.Buddy.RoutedTicks > 0 &&
                actor.Buddy.ActiveDrive.IsInitialized &&
                actor.Buddy.Constraints.IsInitialized &&
                actor.VisualPresenter.PosePipeline is { IsInitialized: true } &&
                actor.VisualPresenter.Facing is { IsInitialized: true } &&
                actor.VisualPresenter.Activities is { IsInitialized: true } &&
                actor.VisualPresenter.HeadLookAt is { IsInitialized: true } &&
                actor.VisualPresenter.Face is { IsInitialized: true } &&
                actor.VisualPresenter.ImpactVisualOffset is { IsInitialized: true } &&
                actor.Buddy.Rig.Parts.All(actor.Buddy.Rig.OwnsPart) &&
                runtime.Actors.Where(other => !ReferenceEquals(other, actor))
                    .All(other => other.Buddy.Rig.Parts.All(part => !actor.Buddy.Rig.OwnsPart(part))));

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
                // One Scene button now opens the workspace that owns every Scene and cast control;
                // the tabs, the "Scene ▾" dropdown and the separate "+" are gone (2026-09-10).
                ["scene_management_controls"] = strip is not null &&
                    strip.SceneStripRoot.FindChild("SceneMenu", recursive: true, owned: false) is Button,
                ["scene_manifest_exists"] = System.IO.File.Exists(Path.Combine(saveRoot, SceneProgressTransactionStore.ManifestFileName)),
                ["wallet_preserved"] = expectedWallet < 0 || context.PlayerProgress.BalanceMilliCredits == expectedWallet,
                ["work_preserved"] = expectedKeyboard < 0 ||
                    (context.WorkProgress is not null && context.WorkProgress.Lifetime.KeyboardPresses == expectedKeyboard),
                ["scene_actor_count_matches"] = actorCountMatches,
                ["scene_actor_bindings_match"] = actorBindingsMatch,
                ["scene_actors_share_player"] = actorsSharePlayer,
                ["scene_actor_buddy_states_independent"] = actorBuddyStatesIndependent,
                ["scene_actor_positions_distinct"] = actorPositionsDistinct,
                ["scene_actors_fully_initialized"] = sceneActorsFullyInitialized,
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
                ["scene_focus_follows_selection"] = focusFollowsSelection,
                ["scene_focus_recovers_after_removal"] = focusRecoversAfterRemoval,
                ["scene_achievement_observers_follow_roster"] = achievementObserversFollowRoster,
                ["scene_duplicate_independent"] = duplicateIsIndependent,
                ["scene_duplicate_copied_background"] = duplicateCopiedBackground,
                ["scene_delete_switched_safely"] = deleteSwitchedSafely,
                ["scene_delete_removed_assets"] = deleteRemovedAssets,
                ["scene_last_scene_protected"] = lastSceneProtected,
                ["scene_focused_customization_targets_selection"] = focusedCustomizationTargetsSelection,
                ["build_mode_pauses_and_places"] = buildModePausesAndPlaces,
                ["build_removal_picks_pointed_part"] = buildRemovalPicksThePartUnderThePointer,
                ["build_commits_on_return_to_play"] = buildCommitsOnReturnToPlay,
                ["build_preview_right_of_list"] = buildPreviewRightOfList,
                ["build_surface_supports_buddy"] = buildSurfaceSupportsBuddy,
                ["built_room_restored"] = builtRoomRestored,
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
