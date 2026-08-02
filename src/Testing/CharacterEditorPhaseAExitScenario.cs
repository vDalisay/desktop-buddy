using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Phase A release journey core: create, edit every category, deterministic randomize,
/// save, fixed-tick use, reaction-overlay retention, and restart selection persistence.
/// </summary>
public sealed class CharacterEditorPhaseAExitScenario : IScenario
{
    public string Id => "character_editor_create_use_and_react";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context =
            await CharacterEditorScenarioSupport.Create(tree, Id);
        try
        {
            BuddyVisualRigTrustSnapshot liveTrust =
                context.Lab.VisualPresenter.RigView.CaptureTrustSnapshot();
            CharacterEditorActionResult created = context.Session.NewCharacter("Phase A Buddy");
            bool touchedAll = created.Completed;

            int colorIndex = 0;
            foreach (CharacterPartSlot part in Enum.GetValues<CharacterPartSlot>())
            {
                touchedAll &= context.Session.SetPartColor(
                    part,
                    new Rgba32(
                        (byte)(40 + colorIndex * 20),
                        (byte)(90 + colorIndex * 12),
                        (byte)(160 - colorIndex * 10))).Completed;
                colorIndex++;
            }

            foreach (CharacterFeatureSlot slot in Enum.GetValues<CharacterFeatureSlot>())
            {
                string prefix = slot switch
                {
                    CharacterFeatureSlot.Eyes => "eyes.",
                    CharacterFeatureSlot.Brows => "brows.",
                    CharacterFeatureSlot.Mouth => "mouth.",
                    _ => "accent.",
                };
                string id = FeatureIds(prefix)
                    .First(value => slot != CharacterFeatureSlot.TorsoAccent ||
                        !string.Equals(value, CharacterFeatureIds.AccentNone, StringComparison.Ordinal));
                touchedAll &= context.Session.SetFeatureId(slot, id).Completed;
                touchedAll &= context.Session.SetFeatureTransform(
                    slot,
                    new NormalizedFeatureTransform(0.15, -0.2, 1.1)).Completed;
                touchedAll &= context.Session.SetFeatureColor(
                    slot,
                    new Rgba32(28, 44, 72)).Completed;
            }
            checks.Add(new StartupCheck("a9_all_editor_categories_editable", touchedAll,
                "six part colors and four feature id/transform/color groups"));

            context.Session.Randomize(7);
            string randomized = CharacterDocumentEditor.Canonical(
                context.Session.WorkingDocument
                ?? throw new InvalidOperationException("Randomization lost the working document."));
            string replay = CharacterDocumentEditor.Canonical(
                CharacterRandomizer.Randomize(
                    CharacterDocument.CreateDefault(
                        context.Session.WorkingDocument.Id,
                        context.Session.WorkingDocument.DisplayName),
                    7));
            bool deterministic = string.Equals(randomized, replay, StringComparison.Ordinal);
            checks.Add(new StartupCheck("a9_fixed_seed_randomize", deterministic,
                $"seed=7 length={randomized.Length}"));

            CharacterEditorActionResult saved = await context.Session.SaveAsync();
            Guid id = context.Session.WorkingDocument?.Id
                ?? throw new InvalidOperationException("Save lost the working document.");
            bool saveCleared = saved.Completed && !context.Session.IsDirty &&
                (await context.Store.LoadAsync(id, CancellationToken.None)).IsSuccess;
            checks.Add(new StartupCheck("a9_save_clears_dirty_and_persists", saveCleared,
                $"id={id} dirty={context.Session.IsDirty}"));

            CharacterEditorActionResult use = await context.Session.UseCharacterAsync();
            bool beforeTick = context.Selection.ActiveCharacterId is null &&
                context.Lab.VisualPresenter.RigView.ActiveAppearance is null;
            context.Coordinator.PhysicsTick();
            await context.Saves.FlushSelectionImmediatelyAsync();
            bool activated = use.Completed && beforeTick &&
                context.Selection.ActiveCharacterId == id &&
                context.Lab.VisualPresenter.RigView.ActiveAppearance?.CharacterId == id &&
                context.Lab.VisualPresenter.RigView.TrustedGeometryMatches(liveTrust);
            checks.Add(new StartupCheck("a9_use_commits_at_fixed_tick", activated,
                $"before={beforeTick} selected={context.Selection.ActiveCharacterId}"));

            context.Lab.VisualPresenter.SetPartScorch(
                BuddyPartId.Head,
                0.35f,
                Colors.Black);
            bool reactionRetained =
                context.Lab.VisualPresenter.RigView.PartScorchAmount(BuddyPartId.Head) > 0.0f &&
                context.Lab.VisualPresenter.RigView.ActiveAppearance?.CharacterId == id &&
                context.Lab.VisualPresenter.RigView.TrustedGeometryMatches(liveTrust);
            checks.Add(new StartupCheck("a9_reaction_overlay_retains_character", reactionRetained,
                $"scorch={context.Lab.VisualPresenter.RigView.PartScorchAmount(BuddyPartId.Head):F2}"));

            var restartSelection = new CharacterSelectionState(id);
            var restartMemory = new InMemoryProgressStore();
            var restartProgress = new DesktopBuddy.Domain.Persistence.BuddyProgressState(1.0);
            var restartSaves = new SaveCoordinator(
                restartProgress,
                restartMemory,
                restartProgress.Revision,
                restartSelection,
                restartSelection.Revision);
            var restartCoordinator = new CharacterSelectionCoordinator(
                context.Store,
                restartSelection,
                context.Lab.VisualPresenter.RigView,
                restartSaves);
            CharacterActivationResult startup = await restartCoordinator.LoadStartupAsync(
                CancellationToken.None);
            restartCoordinator.PhysicsTick();
            bool restart = startup.WasQueued &&
                restartSelection.ActiveCharacterId == id &&
                context.Lab.VisualPresenter.RigView.ActiveAppearance?.CharacterId == id;
            checks.Add(new StartupCheck("a9_restart_restores_selected_character", restart,
                $"startup={startup.Status} selected={restartSelection.ActiveCharacterId}"));
        }
        finally
        {
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }

    private static string[] FeatureIds(string prefix) =>
        typeof(CharacterFeatureIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.IsLiteral ? field.GetRawConstantValue() as string : field.GetValue(null) as string)
            .Where(value => value is not null && value.StartsWith(prefix, StringComparison.Ordinal))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
}
