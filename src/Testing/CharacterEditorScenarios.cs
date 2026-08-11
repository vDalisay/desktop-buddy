using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

internal static class CharacterEditorScenarioSupport
{
    public sealed record Context(
        BuddyLab Lab,
        string Root,
        CharacterStore Store,
        CharacterSelectionState Selection,
        SaveCoordinator Saves,
        CharacterSelectionCoordinator Coordinator,
        BuddyVisualRigView Preview,
        CharacterEditorSession Session);

    public static async Task<Context> Create(SceneTree tree, string scenario)
    {
        (BuddyLab lab, string root, CharacterStore store) =
            await CharacterSelectionScenarioSupport.CreateLabAsync(tree, scenario);
        var selection = new CharacterSelectionState();
        var memory = new InMemoryProgressStore();
        SaveCoordinator saves = CharacterSelectionScenarioSupport.Saves(selection, memory, out _);
        var coordinator = new CharacterSelectionCoordinator(
            store, selection, lab.VisualPresenter.RigView, saves);
        var source = new StaticBuddyVisualTransformSource(lab.Buddy.Rig.Profile, Vector2.Zero);
        var preview = new BuddyVisualRigView { Name = "EditorPreview", ProcessMode = Node.ProcessModeEnum.Always };
        lab.AddChild(preview);
        preview.Initialize(lab.Buddy.VisualProfile, source);
        preview.ApplyPose(Frame(source));
        var library = new CharacterLibraryIndex(new CharacterFileSystem(), root);
        int nextGuid = 100;
        var session = new CharacterEditorSession(
            store,
            library,
            coordinator,
            preview,
            () => GuidFromInt(nextGuid++));
        return new Context(lab, root, store, selection, saves, coordinator, preview, session);
    }

    public static async Task Cleanup(SceneTree tree, Context context)
    {
        CharacterSelectionScenarioSupport.Cleanup(context.Lab, context.Root);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    public static ScenarioResult Result(IReadOnlyList<StartupCheck> checks, ulong seed) =>
        new(checks.All(static check => check.Passed), checks, [$"seed={seed}"]);

    private static BuddyVisualPoseFrame Frame(StaticBuddyVisualTransformSource source)
    {
        BuddyVisualPartPose Pose(BuddyPartId id)
        {
            BuddyVisualTransform transform = source.ReadTransform(id);
            return new BuddyVisualPartPose(
                transform,
                WorldPlaneMapping.To3D(transform.Position),
                Vector3.Zero);
        }
        return new BuddyVisualPoseFrame(
            Pose(BuddyPartId.Head),
            Pose(BuddyPartId.Torso),
            Pose(BuddyPartId.LeftHand),
            Pose(BuddyPartId.RightHand),
            Pose(BuddyPartId.LeftFoot),
            Pose(BuddyPartId.RightFoot),
            0.0f,
            BuiltInCharacterAppearance.NeutralFaceState,
            string.Empty,
            0.0f);
    }

    private static Guid GuidFromInt(int value)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, value);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

public sealed class CharacterEditorStateMachineScenario : IScenario
{
    public string Id => "character_editor_state_machine";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context =
            await CharacterEditorScenarioSupport.Create(tree, Id);
        try
        {
            Guid first = Guid.Parse("81000000-0000-4000-8000-000000000001");
            Guid second = Guid.Parse("81000000-0000-4000-8000-000000000002");
            await context.Store.SaveAsync(CharacterDocument.CreateDefault(first, "First"), CancellationToken.None);
            await context.Store.SaveAsync(CharacterDocument.CreateDefault(second, "Second"), CancellationToken.None);
            await context.Session.RefreshPageAsync(0, 24);
            await context.Session.SelectAsync(first);
            context.Session.SetPartColor(CharacterPartSlot.Head, new Rgba32(20, 40, 80));

            CharacterEditorActionResult blocked = await context.Session.SelectAsync(second);
            bool prompt = blocked.NeedsUnsavedDecision &&
                context.Session.PendingAction == CharacterEditorPendingAction.Select &&
                context.Session.SelectedCharacterId == first && context.Session.IsDirty;
            checks.Add(new StartupCheck("a8_unsaved_selection_prompts", prompt,
                $"pending={context.Session.PendingAction} selected={context.Session.SelectedCharacterId}"));

            await context.Session.ResolveUnsavedAsync(UnsavedDecision.Cancel);
            bool cancel = context.Session.SelectedCharacterId == first && context.Session.IsDirty;
            checks.Add(new StartupCheck("a8_unsaved_cancel_preserves_working_copy", cancel,
                $"selected={context.Session.SelectedCharacterId} dirty={context.Session.IsDirty}"));

            await context.Session.SelectAsync(second);
            await context.Session.ResolveUnsavedAsync(UnsavedDecision.Discard);
            bool discard = context.Session.SelectedCharacterId == second && !context.Session.IsDirty;
            checks.Add(new StartupCheck("a8_unsaved_discard_continues_action", discard,
                $"selected={context.Session.SelectedCharacterId} dirty={context.Session.IsDirty}"));

            context.Session.Rename("Renamed Second");
            CharacterEditorActionResult saved = await context.Session.SaveAsync();
            bool saveClears = saved.Completed && !context.Session.IsDirty &&
                (await context.Store.LoadAsync(second, CancellationToken.None)).Document?.DisplayName == "Renamed Second";
            checks.Add(new StartupCheck("a8_save_clears_dirty", saveClears,
                $"saved={saved.Completed} dirty={context.Session.IsDirty}"));

            context.Session.Rename("Unsaved Before New");
            CharacterEditorActionResult newPrompt = context.Session.RequestNewCharacterPrompt();
            bool newPromptBlocked = newPrompt.NeedsUnsavedDecision &&
                context.Session.PendingAction == CharacterEditorPendingAction.NewPrompt;
            await context.Session.ResolveUnsavedAsync(UnsavedDecision.Cancel);
            bool newPromptCancel = context.Session.SelectedCharacterId == second && context.Session.IsDirty;
            context.Session.RequestNewCharacterPrompt();
            CharacterEditorActionResult newPromptDiscard =
                await context.Session.ResolveUnsavedAsync(UnsavedDecision.Discard);
            bool newPromptContinuesWithoutCreating = newPromptDiscard.Completed &&
                context.Session.SelectedCharacterId == second && !context.Session.IsDirty;
            checks.Add(new StartupCheck(
                "a8_new_name_prompt_runs_after_unsaved_resolution",
                newPromptBlocked && newPromptCancel && newPromptContinuesWithoutCreating,
                $"blocked={newPromptBlocked} cancel={newPromptCancel} continued={newPromptContinuesWithoutCreating}"));

            CharacterEditorActionResult duplicate = context.Session.Duplicate();
            bool freshDuplicate = duplicate.Completed && context.Session.IsDirty &&
                context.Session.SelectedCharacterId != second;
            checks.Add(new StartupCheck("a8_duplicate_gets_fresh_guid", freshDuplicate,
                $"source={second} duplicate={context.Session.SelectedCharacterId}"));

            Guid? duplicateId = context.Session.SelectedCharacterId;
            await context.Session.SaveAsync();
            CharacterEditorActionResult deleted = await context.Session.DeleteAsync();
            bool deleteSelectedOnly = deleted.Completed && context.Session.WorkingDocument is null &&
                duplicateId.HasValue &&
                (await context.Store.LoadAsync(duplicateId.Value, CancellationToken.None)).Status == CharacterLoadStatus.NotFound &&
                (await context.Store.LoadAsync(first, CancellationToken.None)).IsSuccess;
            checks.Add(new StartupCheck("a8_delete_removes_only_selected_character", deleteSelectedOnly,
                $"deleted={duplicateId}"));
        }
        finally
        {
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }
        return CharacterEditorScenarioSupport.Result(checks, seed);
    }
}

public sealed class CharacterEditorRandomizationScenario : IScenario
{
    public string Id => "character_editor_randomization";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterDocument source = CharacterDocument.CreateDefault(
            Guid.Parse("82000000-0000-4000-8000-000000000001"),
            "Random");
        CharacterDocument first = CharacterRandomizer.Randomize(source, 77);
        CharacterDocument replay = CharacterRandomizer.Randomize(source, 77);
        CharacterDocument different = CharacterRandomizer.Randomize(source, 78);
        bool deterministic = string.Equals(
            CharacterDocumentEditor.Canonical(first),
            CharacterDocumentEditor.Canonical(replay),
            StringComparison.Ordinal);
        bool varied = !string.Equals(
            CharacterDocumentEditor.Canonical(first),
            CharacterDocumentEditor.Canonical(different),
            StringComparison.Ordinal);
        checks.Add(new StartupCheck("a8_randomization_seed_deterministic", deterministic && varied,
            $"deterministic={deterministic} varied={varied}"));

        string accent = CharacterDocumentEditor.ReadFeatureId(first, CharacterFeatureSlot.TorsoAccent);
        bool accentPresent = !string.Equals(accent, CharacterFeatureIds.AccentNone, StringComparison.Ordinal);
        checks.Add(new StartupCheck("a8_randomization_excludes_no_accent", accentPresent,
            $"accent={accent}"));

        bool bounded = true;
        foreach (CharacterFeatureSlot slot in Enum.GetValues<CharacterFeatureSlot>())
        {
            NormalizedFeatureTransform transform = CharacterDocumentEditor.ReadFeatureTransform(first, slot);
            bounded &= transform.OffsetX is >= NormalizedFeatureTransform.MinimumOffset and <= NormalizedFeatureTransform.MaximumOffset &&
                transform.OffsetY is >= NormalizedFeatureTransform.MinimumOffset and <= NormalizedFeatureTransform.MaximumOffset &&
                transform.Scale is >= NormalizedFeatureTransform.MinimumScale and <= NormalizedFeatureTransform.MaximumScale;
        }
        checks.Add(new StartupCheck("a8_randomization_respects_bounds", bounded, "four feature transforms"));
        return Task.FromResult(CharacterEditorScenarioSupport.Result(checks, seed));
    }
}

public sealed class EditorPreviewHasNoPhysicsScenario : IScenario
{
    public string Id => "editor_preview_has_no_physics";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context =
            await CharacterEditorScenarioSupport.Create(tree, Id);
        try
        {
            BuddyVisualRigTrustSnapshot liveTrust = context.Lab.VisualPresenter.RigView.CaptureTrustSnapshot();
            CompiledCharacterAppearance? liveAppearance = context.Lab.VisualPresenter.RigView.ActiveAppearance;
            context.Session.NewCharacter("Preview Only");
            context.Session.Randomize(99);
            int physics = CountPhysics(context.Preview);
            bool isolated = physics == 0 &&
                context.Preview.ActiveAppearance is not null &&
                context.Lab.VisualPresenter.RigView.ActiveAppearance == liveAppearance &&
                context.Lab.VisualPresenter.RigView.TrustedGeometryMatches(liveTrust);
            checks.Add(new StartupCheck("a8_preview_has_no_physics_authority", isolated,
                $"physics={physics} preview={context.Preview.ActiveAppearance?.CharacterId} " +
                $"live={context.Lab.VisualPresenter.RigView.ActiveAppearance?.CharacterId}"));
        }
        finally
        {
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }
        return CharacterEditorScenarioSupport.Result(checks, seed);
    }

    private static int CountPhysics(Node node)
    {
        int count = node is CollisionObject2D or Joint2D or BuddyRoot or PuppetRig ? 1 : 0;
        foreach (Node child in node.GetChildren())
            count += CountPhysics(child);
        return count;
    }
}
