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
using DesktopBuddy.Domain.Content;
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
            bool buildEditsApply = !buildRoom;
            bool buildLinksWork = !buildRoom;
            bool buildLinksSurviveSceneSwitch = !buildRoom;
            bool buildDevicesWork = !buildRoom;
            bool buildLinksSnap = !buildRoom;
            bool buildPreviewPlays = !buildRoom;
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

                // With a real window, picture the palette for the owner: a part, then a link preset.
                if (DisplayServer.GetName() != "headless" && !string.IsNullOrWhiteSpace(args.ArtifactsDir))
                {
                    async Task Picture(string name)
                    {
                        for (int frame = 0; frame < 4; frame++)
                            await sandbox.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        Directory.CreateDirectory(args.ArtifactsDir!);
                        sandbox.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(args.ArtifactsDir!, name));
                    }
                    build.SelectPart(SandboxPartCatalogue.WoodBeam);
                    await Picture("build_palette_part.png");
                    build.SetTool(BuildTool.Hinge);
                    build.TuneLink(1.0f, 0.0f, 0.6f);
                    await Picture("build_palette_hinge.png");
                    build.SetTool(BuildTool.Wire);
                    await Picture("build_palette_wire.png");
                    build.SetTool(BuildTool.Parts);
                }

                // The preview can be played with (owner note 2026-09-11): a wired Button lights its
                // Lamp, a Piston's head comes out, and a weak rope snaps when its block is yanked.
                // Headless drops mouse events, so the preview is handed them directly.
                if (build.FindChild("BuildModePartPreview", recursive: true, owned: false) is SandboxPartPreview playable &&
                    playable.FindChild("PreviewViewport", recursive: true, owned: false) is SandboxModelStage stage)
                {
                    async Task Frames(int count)
                    {
                        for (int frame = 0; frame < count; frame++)
                            await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    }
                    void Mouse(Vector2 model, bool? pressed)
                    {
                        Vector2 at = stage.ToPixel(model) + new Vector2(2.0f, 2.0f);   // the well's inset
                        playable._GuiInput(pressed is { } down
                            ? new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at }
                            : new InputEventMouseMotion { Position = at });
                    }

                    build.SetTool(BuildTool.Wire);
                    await Frames(2);
                    Mouse(new Vector2(-46.0f, -8.0f), true);
                    Mouse(new Vector2(-46.0f, -8.0f), false);
                    await Frames(2);
                    bool lampLit = playable.FindChild("Glow", recursive: true, owned: false) is OmniLight3D { Visible: true };

                    build.SelectPart(SandboxPartCatalogue.Piston);
                    await Frames(2);
                    Mouse(Vector2.Zero, true);
                    Mouse(Vector2.Zero, false);
                    await Frames(4);
                    bool pistonMoved = playable.FindChild("Rod", recursive: true, owned: false) is Node3D { Visible: true };

                    build.SetTool(BuildTool.Rope);
                    build.TuneLink(0.25f, 0.3f, 0.0f);
                    await Frames(2);
                    var block = new Vector2(0.0f, -SandboxPaletteModels.RopeHang(0.3f));
                    Mouse(block, true);
                    Mouse(block + new Vector2(0.0f, -200.0f), null);
                    bool ropeSnapped = false;
                    ulong until = Time.GetTicksMsec() + 3000;
                    while (!ropeSnapped && Time.GetTicksMsec() < until)
                    {
                        await Frames(1);
                        ropeSnapped = playable.FindChild("Cord", recursive: true, owned: false) is Node3D { Visible: false };
                    }
                    Mouse(block, false);
                    build.TuneLink(1.0f, 0.1f, 0.0f);   // back to the Strong Rope the rest of the journey expects
                    build.SetTool(BuildTool.Parts);
                    buildPreviewPlays = lampLit && pistonMoved && ropeSnapped;
                    GD.Print($"[Journey] build preview: lamp {lampLit}, piston {pistonMoved}, rope snapped {ropeSnapped}");
                }

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

                // NF-3 editing through the same controls the room uses: select, move, rotate, tune,
                // duplicate and delete, each landing in the document and on the live body.
                if (sandbox.BuiltParts.Values.FirstOrDefault() is SandboxPartBody kept)
                {
                    SandboxPartId keptId = scenes.ActiveSandbox.Parts[0].PartId;
                    bool selected = build.SelectPlacedPartAt(kept.GlobalPosition) &&
                        build.SelectedPlacedPart == keptId;
                    Vector2 movedTo = kept.GlobalPosition + new Vector2(30.0f, 0.0f);
                    bool moved = build.MoveSelectedPartTo(movedTo) &&
                        kept.GlobalPosition.IsEqualApprox(movedTo);
                    // Gravity 5 is outside the band and must arrive clamped to 4.
                    bool tuned = build.SetSelectedPartOverrides(
                        new SandboxPartOverrides(MassScale: 2.0f, Bounce: 0.5f, GravityScale: 5.0f, Frozen: true));
                    SandboxPartOverrides stored = scenes.ActiveSandbox.Parts[0].Overrides;
                    tuned = tuned && stored.Frozen && stored.MassScale == 2.0f &&
                        stored.GravityScale == SandboxPartOverrides.MaximumGravityScale &&
                        kept.Freeze && Mathf.IsEqualApprox(kept.GravityScale, SandboxPartOverrides.MaximumGravityScale);

                    SandboxPartId? copyId = build.DuplicateSelectedPart();
                    bool duplicated = copyId is { } copy && copy != keptId &&
                        scenes.ActiveSandbox.Count == 2 && sandbox.BuiltParts.Count == 2 &&
                        scenes.ActiveSandbox.TryGet(copy, out PlacedSandboxPart? copied) &&
                        copied!.Overrides == stored && build.SelectedPlacedPart == copy;
                    bool rotated = build.RotateSelectedPart(30.0f) && copyId is { } rotatedId &&
                        scenes.ActiveSandbox.TryGet(rotatedId, out PlacedSandboxPart? turned) &&
                        Mathf.IsEqualApprox(turned!.RotationDegrees, 30.0f) &&
                        Mathf.IsEqualApprox(sandbox.BuiltParts[rotatedId].RotationDegrees, 30.0f);
                    bool deleted = build.DeleteSelectedPart() &&
                        scenes.ActiveSandbox.Count == 1 && sandbox.BuiltParts.Count == 1 &&
                        build.SelectedPlacedPart is null;
                    buildEditsApply = selected && moved && tuned && duplicated && rotated && deleted;
                    Log.Info("BootstrapJourney",
                        $"build edits: selected={selected} moved={moved} tuned={tuned} duplicated={duplicated} rotated={rotated} deleted={deleted}");
                }

                await build.LeaveAsync();
                buildCommitsOnReturnToPlay = !build.IsActive &&
                    !sandbox.Lifecycle.PauseCoordinator.Contains(GameplayPauseReason.BuildMode) &&
                    !scenes.IsDirty;

                // NF-3 links, played for real: a two-wheel hinged cart, a beam hanging on a rope,
                // and a rope from the kept beam to the room that must survive the restart.
                {
                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    SandboxPartId keptBeamId = scenes.ActiveSandbox.Parts[0].PartId;
                    Vector2 cartCentre = bounds.Position + bounds.Size * new Vector2(0.55f, 0.82f);
                    Vector2 hangCentre = bounds.Position + bounds.Size * new Vector2(0.5f, 0.3f);

                    SandboxPartId Place(SemanticDefinitionId definition, Vector2 at)
                    {
                        build.SelectPart(definition);
                        build.PlaceSelectedPartAt(at);
                        return build.SelectedPlacedPart ?? default;
                    }

                    SandboxPartId cartBeam = Place(SandboxPartCatalogue.WoodBeam, cartCentre);
                    SandboxPartId leftWheel = Place(SandboxPartCatalogue.Wheel, cartCentre + new Vector2(-38.0f, 10.0f));
                    SandboxPartId rightWheel = Place(SandboxPartCatalogue.Wheel, cartCentre + new Vector2(38.0f, 10.0f));
                    bool leftAxle = build.HingeAt(cartCentre + new Vector2(-38.0f, 4.0f)).Succeeded;
                    bool rightAxle = build.HingeAt(cartCentre + new Vector2(38.0f, 4.0f)).Succeeded;
                    SandboxPartId hanging = Place(SandboxPartCatalogue.WoodBeam, hangCentre);
                    Vector2 hangPoint = hangCentre + new Vector2(40.0f, 0.0f);
                    Vector2 hangAnchor = hangPoint + new Vector2(0.0f, -50.0f);
                    bool hangRope = build.RopeBetween(hangPoint, hangAnchor).Succeeded;
                    Vector2 keptCentre = sandbox.BuiltParts[keptBeamId].GlobalPosition;
                    bool keptRope = build.RopeBetween(keptCentre, keptCentre + new Vector2(0.0f, -60.0f)).Succeeded;
                    int linksBefore = scenes.ActiveSandbox.Links.Count;
                    bool weldRejected = !build.WeldAt(bounds.Position + new Vector2(4.0f, 4.0f)).Succeeded &&
                        scenes.ActiveSandbox.Links.Count == linksBefore;
                    bool created = leftAxle && rightAxle && hangRope && keptRope && weldRejected &&
                        linksBefore == 4 && sandbox.BuiltLinkCount == 4;

                    // Play: the cart gets a shove and the hanging beam falls onto its rope.
                    await build.LeaveAsync();
                    SandboxPartBody cart = sandbox.BuiltParts[cartBeam];
                    SandboxPartBody wheel = sandbox.BuiltParts[leftWheel];
                    SandboxPartBody hung = sandbox.BuiltParts[hanging];
                    SandboxLink axle = scenes.ActiveSandbox.Links.First(link =>
                        link.Kind == SandboxLinkKind.Hinge && link.Touches(leftWheel));
                    SandboxLink hangLink = scenes.ActiveSandbox.Links.First(link => link.Touches(hanging));
                    Vector2 cartStart = cart.GlobalPosition;
                    // Summed per frame: a body's angle wraps at pi, so start-to-end says nothing
                    // about a wheel that has rolled several turns.
                    float spun = 0.0f;
                    float lastAngle = wheel.Rotation;
                    for (int frame = 0; frame < 20; frame++)
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                    cart.ApplyCentralImpulse(new Vector2(cart.Mass * 260.0f, 0.0f));
                    float worstSeparation = 0.0f;
                    for (int frame = 0; frame < 100; frame++)
                    {
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                        Vector2 onBeam = sandbox.LinkEndWorld(axle.A)!.Value;
                        Vector2 onWheel = sandbox.LinkEndWorld(axle.B)!.Value;
                        worstSeparation = Math.Max(worstSeparation, onBeam.DistanceTo(onWheel));
                        spun += Math.Abs(Mathf.AngleDifference(lastAngle, wheel.Rotation));
                        lastAngle = wheel.Rotation;
                    }
                    float travelled = Math.Abs(cart.GlobalPosition.X - cartStart.X);
                    float ropeStretch = sandbox.LinkEndWorld(hangLink.A)!.Value.DistanceTo(
                        sandbox.LinkEndWorld(hangLink.B)!.Value) - hangLink.Length;
                    bool played = worstSeparation < 4.0f && travelled > 8.0f && spun > 0.3f &&
                        ropeStretch < 8.0f && hung.GlobalPosition.Y < bounds.End.Y - 40.0f;
                    Log.Info("BootstrapJourney",
                        $"build links: created={created} links={linksBefore} built={sandbox.BuiltLinkCount} " +
                        $"weldRejected={weldRejected} separation={worstSeparation:0.00} travelled={travelled:0.0} " +
                        $"spun={spun:0.00} ropeStretch={ropeStretch:0.00} hungY={hung.GlobalPosition.Y:0.0} floor={bounds.End.Y:0.0}");

                    // Clear the cart and the hanging beam; their links must go with them.
                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    foreach (SandboxPartId part in new[] { cartBeam, leftWheel, rightWheel, hanging })
                    {
                        if (build.SelectPlaced(part))
                            build.DeleteSelectedPart();
                    }
                    bool cleaned = scenes.ActiveSandbox.Count == 1 &&
                        scenes.ActiveSandbox.Links.Count == 1 &&
                        scenes.ActiveSandbox.Links[0].Kind == SandboxLinkKind.Rope &&
                        scenes.ActiveSandbox.Links[0].Touches(keptBeamId) &&
                        sandbox.BuiltLinkCount == 1;
                    await build.LeaveAsync();
                    buildLinksWork = created && played && cleaned && !scenes.IsDirty;
                }

                // NF-3 acceptance: switch Scenes and come back. Each room's parts and links are rebuilt
                // from its own document, and the outgoing room's joints go with its parts rather than
                // pinning freed bodies. A Wheel hinged to the room gives the switch a real joint to move.
                {
                    int PinCount() => sandbox.GetChildren().Count(node => node is PinJoint2D);

                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    Vector2 pinPoint = bounds.Position + bounds.Size * new Vector2(0.8f, 0.4f);
                    build.SelectPart(SandboxPartCatalogue.Wheel);
                    build.PlaceSelectedPartAt(pinPoint);
                    SandboxPartId pinnedWheel = build.SelectedPlacedPart ?? default;
                    bool pinned = build.HingeAt(pinPoint).Succeeded;
                    await build.LeaveAsync();

                    SceneId builtSceneId = scenes.ActiveSceneId;
                    int parts = scenes.ActiveSandbox.Count;
                    int links = scenes.ActiveSandbox.Links.Count;
                    int pins = PinCount();
                    SandboxPartBody outgoingWheel = sandbox.BuiltParts[pinnedWheel];

                    bool RoomMatches() =>
                        sandbox.BuiltParts.Count == parts && sandbox.BuiltLinkCount == links &&
                        scenes.ActiveSandbox.Count == parts && scenes.ActiveSandbox.Links.Count == links &&
                        PinCount() == pins;

                    SceneId copyId = strip is null ? default : await strip.DuplicateActiveSceneAsync();
                    bool toCopy = copyId.IsValid && (await sandbox.SwitchSceneAsync(copyId)).Succeeded;
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                    bool copyRoom = toCopy && RoomMatches() &&
                        (!GodotObject.IsInstanceValid(outgoingWheel) || !outgoingWheel.IsInsideTree());

                    bool back = (await sandbox.SwitchSceneAsync(builtSceneId)).Succeeded;
                    for (int frame = 0; frame < 30; frame++)
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                    // Hinged to the room, the wheel may turn but must stay on its pin.
                    bool wheelOnPin = sandbox.BuiltParts.TryGetValue(pinnedWheel, out SandboxPartBody? wheelBack) &&
                        wheelBack!.GlobalPosition.DistanceTo(pinPoint) < 4.0f;
                    bool returned = back && scenes.ActiveSceneId == builtSceneId && RoomMatches() && wheelOnPin;

                    // Take the wheel out again so the restart phase finds the room it expects.
                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    bool removed = build.SelectPlaced(pinnedWheel) && build.DeleteSelectedPart() &&
                        sandbox.BuiltLinkCount == links - 1 && PinCount() == pins - 1;
                    await build.LeaveAsync();
                    runtime = sandbox.ActiveSceneRuntime;

                    buildLinksSurviveSceneSwitch = pinned && pins == 1 && copyRoom && returned && removed &&
                        !scenes.IsDirty;
                    Log.Info("BootstrapJourney",
                        $"build switch: pinned={pinned} parts={parts} links={links} pins={pins} toCopy={toCopy} " +
                        $"copyRoom={copyRoom} back={back} wheelOnPin={wheelOnPin} returned={returned} removed={removed}");
                }

                // NF-4: Button -> Timer -> Lamp, built, wired and pressed through the player's controls.
                {
                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    Vector2 FloorAt(float x) => bounds.Position + bounds.Size * new Vector2(x, 0.93f);
                    SandboxPartId PlaceDevice(SemanticDefinitionId definition, Vector2 at)
                    {
                        build.SelectPart(definition);
                        build.PlaceSelectedPartAt(at);
                        return build.SelectedPlacedPart ?? default;
                    }
                    // The machine stands on a frozen shelf half-way up the room, out of the cast's
                    // reach: Buttons now answer to anything landing on them, and a Buddy wandering
                    // across the test rig would press it for real.
                    const float ShelfY = 0.45f;
                    Vector2 ShelfAt(float x) => bounds.Position + bounds.Size * new Vector2(x, ShelfY) + new Vector2(0.0f, -24.0f);
                    SandboxPartId shelf = PlaceDevice(SandboxPartCatalogue.MetalPlate, bounds.Position + bounds.Size * new Vector2(0.225f, ShelfY));
                    bool shelfBuilt = build.SetSelectedPartOverrides(new SandboxPartOverrides(Frozen: true, Length: 384.0f, Thickness: 12.0f));
                    // NF-4D: one Tool Mount, holding whichever tool it is given and using it whether
                    // or not the player has bought that tool (owner's calls 2026-09-12).
                    bool mountOffered = !sandbox.Economy.IsUnlocked(ContentIds.ToolPistol) &&
                        build.SelectPart(SandboxPartCatalogue.WeaponTrigger);
                    SandboxPartId button = PlaceDevice(SandboxPartCatalogue.Button, ShelfAt(0.19f));
                    SandboxPartId timer = PlaceDevice(SandboxPartCatalogue.Timer, ShelfAt(0.26f));
                    SandboxPartId lamp = PlaceDevice(SandboxPartCatalogue.Lamp, ShelfAt(0.33f));
                    // A Metal Block resting on a Piston's head, for Button -> Piston.
                    SandboxPartId piston = PlaceDevice(SandboxPartCatalogue.Piston, ShelfAt(0.12f));
                    SandboxPartId load = PlaceDevice(SandboxPartCatalogue.MetalBlock, ShelfAt(0.12f) + new Vector2(0.0f, -40.0f));
                    // A mount holding a pistol, nailed down and turned to shoot at the ceiling, so its
                    // rounds cannot cross the room and hit the cast. It stands off the shelf line so
                    // its wire does not run along the Timer's.
                    Vector2 mountAt = ShelfAt(0.40f) + new Vector2(0.0f, -40.0f);
                    SandboxPartId mount = PlaceDevice(SandboxPartCatalogue.WeaponTrigger, mountAt);
                    bool mountAimed = build.RotateSelectedPart(-90.0f) &&
                        build.SetSelectedPartOverrides(new SandboxPartOverrides(
                            Frozen: true, MountedTool: ContentIds.ToolPistol));
                    bool wired = build.WireBetween(ShelfAt(0.19f), ShelfAt(0.26f)).Succeeded &&
                        build.WireBetween(ShelfAt(0.26f), ShelfAt(0.33f)).Succeeded &&
                        build.WireBetween(ShelfAt(0.19f), ShelfAt(0.12f)).Succeeded &&
                        build.WireBetween(ShelfAt(0.19f), mountAt).Succeeded;
                    // A Lamp sends nothing, and a Button receives nothing.
                    bool badRejected = !build.WireBetween(ShelfAt(0.33f), ShelfAt(0.19f)).Succeeded &&
                        !build.WireBetween(ShelfAt(0.26f), ShelfAt(0.19f)).Succeeded &&
                        scenes.ActiveSandbox.Wires.Count == 4;
                    await build.LeaveAsync();
                    for (int frame = 0; frame < 30; frame++)
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);

                    SandboxPartBody lampBody = sandbox.BuiltParts[lamp];
                    SandboxPartBody loadBody = sandbox.BuiltParts[load];
                    float loadRestY = loadBody.GlobalPosition.Y;
                    float loadHighestY = loadRestY;
                    int shovesBefore = sandbox.PistonShoveCount;
                    int mountShotsBefore = sandbox.WeaponMountShots;
                    int roundsBefore = sandbox.CursorGuns.ProjectilesLaunched;
                    bool pressed = build.PressButtonAt(sandbox.BuiltParts[button].GlobalPosition) &&
                        !build.PressButtonAt(lampBody.GlobalPosition);
                    int litAfter = -1;
                    for (int frame = 0; frame < 240 && litAfter < 0; frame++)
                    {
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                        loadHighestY = Mathf.Min(loadHighestY, loadBody.GlobalPosition.Y);
                        if (lampBody.Lit)
                            litAfter = frame;
                    }
                    // The Button's other wire went straight to the Piston: it fired once, at once,
                    // and threw the block off its head.
                    bool shoved = sandbox.PistonShoveCount == shovesBefore + 1 && loadRestY - loadHighestY > 20.0f;
                    // The same press pulled the mounted pistol's trigger: one shot, one round out of
                    // the barrel, and the mount kicked.
                    bool mountFired = sandbox.WeaponMountShots == mountShotsBefore + 1 &&
                        sandbox.CursorGuns.ProjectilesLaunched > roundsBefore;

                    // The press switched the Timer on; its first beat is one interval (default one
                    // second) later: at 120 Hz the lamp lights a little after 120 ticks.
                    bool timed = litAfter >= Engine.PhysicsTicksPerSecond - 5 &&
                        litAfter <= Engine.PhysicsTicksPerSecond + 20;
                    bool running = sandbox.Signals.IsTimerRunning(timer);
                    // Wires are a Build aid: hidden in Play, shown in Build.
                    bool wiresHiddenInPlay = !sandbox.WiresVisible;

                    // Cut the running Timer's wire to the Lamp: the lamp keeps whatever state it
                    // has, because the beats have nowhere to go.
                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    bool wiresShownInBuild = sandbox.WiresVisible;
                    bool litBeforeCut = lampBody.Lit;
                    Vector2 wireMiddle = (sandbox.BuiltParts[timer].GlobalPosition + lampBody.GlobalPosition) * 0.5f;
                    bool cut = build.RemoveLinkAt(wireMiddle) && scenes.ActiveSandbox.Wires.Count == 3;
                    await build.LeaveAsync();
                    for (int frame = 0; frame < Engine.PhysicsTicksPerSecond * 3; frame++)
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                    bool stayedLit = lampBody.Lit == litBeforeCut && sandbox.Signals.IsTimerRunning(timer);

                    // And the Button switches the clock back off.
                    build.PressButtonAt(sandbox.BuiltParts[button].GlobalPosition);
                    for (int frame = 0; frame < 5; frame++)
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                    bool stopped = !sandbox.Signals.IsTimerRunning(timer);

                    // Anything landing on a Button presses it: a block dropped on the cap presses it
                    // once, and resting there holds it down without pressing it again.
                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    SandboxPartBody buttonBody = sandbox.BuiltParts[button];
                    SandboxPartId weight = PlaceDevice(SandboxPartCatalogue.MetalBlock,
                        buttonBody.GlobalPosition + new Vector2(0.0f, -70.0f));
                    await build.LeaveAsync();
                    int contactPressesBefore = sandbox.ButtonContactPresses;
                    for (int frame = 0; frame < Engine.PhysicsTicksPerSecond; frame++)
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                    bool pressedByWeight = sandbox.ButtonContactPresses == contactPressesBefore + 1 &&
                        buttonBody.ButtonPress > 0.5f;

                    // The same mount holding a bat swings it instead, and the bat knocks a block away.
                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    build.SelectPlaced(mount);
                    bool batHeld = build.SetSelectedPartOverrides(new SandboxPartOverrides(
                        Frozen: true, MountedTool: ContentIds.ToolBaseballBat));
                    SandboxPartId target = PlaceDevice(SandboxPartCatalogue.MetalBlock, mountAt + new Vector2(38.0f, 0.0f));
                    await build.LeaveAsync();
                    SandboxPartBody targetBody = sandbox.BuiltParts[target];
                    Vector2 targetRest = targetBody.GlobalPosition;
                    int swingsBefore = sandbox.WeaponMountShots;
                    build.PressButtonAt(sandbox.BuiltParts[button].GlobalPosition);
                    float knocked = 0.0f;
                    float peakSpin = 0.0f;
                    for (int frame = 0; frame < Engine.PhysicsTicksPerSecond * 2; frame++)
                    {
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                        knocked = Mathf.Max(knocked, Mathf.Abs(targetBody.GlobalPosition.X - targetRest.X));
                        if (sandbox.FindChild("mounted-baseball-bat", recursive: true, owned: false) is RigidBody2D spinning)
                            peakSpin = Mathf.Max(peakSpin, Mathf.Abs(spinning.AngularVelocity));
                    }
                    // A struck Metal Block is shoved about ten pixels: enough to prove the swing
                    // really connected (nothing at all moves it when it does not), not a feel target.
                    bool mountSwung = batHeld && sandbox.WeaponMountShots > swingsBefore && knocked > 6.0f;
                    string armState = sandbox.FindChild("mounted-baseball-bat", recursive: true, owned: false) is RigidBody2D bat
                        ? $"at={bat.GlobalPosition} spin={bat.AngularVelocity:F1}" : "no arm";
                    Log.Info("BootstrapJourney", $"mounted bat: held={batHeld} swings={sandbox.WeaponMountShots - swingsBefore} knocked={knocked:F1} peakSpin={peakSpin:F1} mount={sandbox.BuiltParts.GetValueOrDefault(mount)?.GlobalPosition} block={targetRest} {armState}");

                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    // A beam made longer and thicker in Properties: its body and weight follow.
                    SandboxPartId beamPart = PlaceDevice(SandboxPartCatalogue.WoodBeam, FloorAt(0.6f) + new Vector2(0.0f, -60.0f));
                    SandboxPartBody beamBody = sandbox.BuiltParts[beamPart];
                    float beamMass = beamBody.Mass;
                    bool resized = build.SetSelectedPartOverrides(new SandboxPartOverrides(Length: 192.0f, Thickness: 24.0f)) &&
                        beamBody.Definition.Width == 192.0f && beamBody.Definition.Height == 24.0f &&
                        Mathf.IsEqualApprox(beamBody.Mass, beamMass * 3.0f) &&
                        beamBody.ContainsPoint(beamBody.GlobalPosition + new Vector2(90.0f, 0.0f));
                    foreach (SandboxPartId device in new[] { button, timer, lamp, piston, load, mount, target, beamPart, weight, shelf })
                    {
                        if (build.SelectPlaced(device))
                            build.DeleteSelectedPart();
                    }
                    bool cleared = scenes.ActiveSandbox.Wires.Count == 0 && scenes.ActiveSandbox.Count == 1;
                    await build.LeaveAsync();

                    buildDevicesWork = shelfBuilt && mountOffered && mountAimed && mountFired && mountSwung && wired && badRejected && pressed && timed && shoved &&
                        running && wiresHiddenInPlay && wiresShownInBuild && cut && stayedLit && stopped &&
                        pressedByWeight && resized && cleared && !scenes.IsDirty;
                    Log.Info("BootstrapJourney",
                        $"build devices: mountOffered={mountOffered} mountAimed={mountAimed} mountFired={mountFired} mountSwung={mountSwung} wired={wired} badRejected={badRejected} " +
                        $"pressed={pressed} litAfter={litAfter} shoved={shoved} rise={loadRestY - loadHighestY:F1} " +
                        $"running={running} wiresHiddenInPlay={wiresHiddenInPlay} wiresShownInBuild={wiresShownInBuild} " +
                        $"cut={cut} stayedLit={stayedLit} stopped={stopped} pressedByWeight={pressedByWeight} contactPresses={sandbox.ButtonContactPresses} " +
                        $"lastPresser={sandbox.LastButtonPresser} button={sandbox.BuiltParts.GetValueOrDefault(button)?.GlobalPosition} resized={resized} cleared={cleared}");
                }

                // Link strength: a Metal Block on a weak rope snaps it; one on a strong rope hangs.
                {
                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    Vector2 AirAt(float x) => bounds.Position + bounds.Size * new Vector2(x, 0.3f);
                    SandboxPartId Place(Vector2 at)
                    {
                        build.SelectPart(SandboxPartCatalogue.MetalBlock);
                        build.PlaceSelectedPartAt(at);
                        return build.SelectedPlacedPart ?? default;
                    }
                    SandboxPartId weakBlock = Place(AirAt(0.8f));
                    SandboxPartId strongBlock = Place(AirAt(0.9f));
                    build.SetTool(BuildTool.Rope);
                    build.TuneLink(0.25f, 0.3f, 0.0f);
                    SandboxLink? weakRope = build.RopeBetween(AirAt(0.8f) + new Vector2(0.0f, -12.0f), AirAt(0.8f) + new Vector2(0.0f, -70.0f)).Link;
                    build.TuneLink(1.0f, 0.1f, 0.0f);
                    SandboxLink? strongRope = build.RopeBetween(AirAt(0.9f) + new Vector2(0.0f, -12.0f), AirAt(0.9f) + new Vector2(0.0f, -70.0f)).Link;
                    bool tuned = weakRope is { Strength: 0.25f } && strongRope is { IsUnbreakable: true };
                    build.SetTool(BuildTool.Parts);
                    // Counted before Play resumes: the weak rope snaps the moment the block's weight
                    // comes on it, which is while leaving Build is still saving the room.
                    int snappedBefore = sandbox.SnappedLinkCount;
                    int linksBefore = scenes.ActiveSandbox.Links.Count - 2;
                    await build.LeaveAsync();
                    for (int frame = 0; frame < Engine.PhysicsTicksPerSecond * 2; frame++)
                        await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.PhysicsFrame);
                    bool weakSnapped = sandbox.SnappedLinkCount == snappedBefore + 1 && weakRope is not null &&
                        !scenes.ActiveSandbox.TryGetLink(weakRope.LinkId, out _);
                    bool strongHeld = strongRope is not null && scenes.ActiveSandbox.TryGetLink(strongRope.LinkId, out _);

                    build.Toggle();
                    await sandbox.ToSignal(sandbox.GetTree(), SceneTree.SignalName.ProcessFrame);
                    foreach (SandboxPartId block in new[] { weakBlock, strongBlock })
                    {
                        if (build.SelectPlaced(block))
                            build.DeleteSelectedPart();
                    }
                    await build.LeaveAsync();
                    buildLinksSnap = tuned && weakSnapped && strongHeld && scenes.ActiveSandbox.Links.Count == linksBefore &&
                        !scenes.IsDirty;
                    Log.Info("BootstrapJourney",
                        $"build links: tuned={tuned} weakSnapped={weakSnapped} strongHeld={strongHeld} " +
                        $"snapped={sandbox.SnappedLinkCount - snappedBefore} links={scenes.ActiveSandbox.Links.Count}/{linksBefore}");
                }

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
                PlacedSandboxPart? restored = scenes.ActiveSandbox.Count == 1 ? scenes.ActiveSandbox.Parts[0] : null;
                // The tuning set in Build must survive a real restart, and the body must honour it.
                builtRoomRestored = restored is not null &&
                    restored.DefinitionId == SandboxPartCatalogue.WoodBeam &&
                    restored.Overrides.Frozen &&
                    restored.Overrides.MassScale == 2.0f &&
                    restored.Overrides.GravityScale == SandboxPartOverrides.MaximumGravityScale &&
                    sandbox.BuiltParts.Count == 1 &&
                    sandbox.BuiltParts.TryGetValue(restored.PartId, out SandboxPartBody? restoredBody) &&
                    restoredBody!.Freeze &&
                    // The rope tied in Build comes back tied to the same beam, and is live again.
                    scenes.ActiveSandbox.Links.Count == 1 &&
                    scenes.ActiveSandbox.Links[0].Kind == SandboxLinkKind.Rope &&
                    scenes.ActiveSandbox.Links[0].Touches(restored.PartId) &&
                    sandbox.BuiltLinkCount == 1;
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
                ["build_edits_apply"] = buildEditsApply,
                ["build_links_work"] = buildLinksWork,
                ["build_links_survive_scene_switch"] = buildLinksSurviveSceneSwitch,
                ["build_devices_work"] = buildDevicesWork,
                ["build_links_snap"] = buildLinksSnap,
                ["build_preview_plays"] = buildPreviewPlays,
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
