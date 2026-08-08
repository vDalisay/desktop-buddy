using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class BuddyStudioOwnershipPreviewScenario : IScenario
{
    public string Id => "buddy_studio_ownership_preview";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        CharacterEditorScenarioSupport.Context context =
            await CharacterEditorScenarioSupport.Create(tree, Id);
        try
        {
            CosmeticDefinition glasses = CharacterFeatureCatalog.Shipped.ResolveDefinition(
                CharacterFeatureSlot.Glasses,
                CharacterFeatureIds.GlassesWorkClassic,
                out bool known);
            checks.Add(new StartupCheck(
                "bs4_existing_work_glasses_content_id_preserved",
                known && string.Equals(
                    glasses.OwnershipContentId,
                    ContentIds.CosmeticWorkGlasses,
                    StringComparison.Ordinal),
                $"definition={glasses.OwnershipContentId} existing={ContentIds.CosmeticWorkGlasses}"));

            var progress = new BuddyProgressState(
                cashPerPain: 0.01,
                initialBalanceMilliCredits: 5000);
            var progressStore = new InMemoryProgressStore();
            var saves = new SaveCoordinator(progress, progressStore);
            var catalogue = new ToolCatalogue([
                new CatalogueEntry(
                    ContentIds.CosmeticWorkGlasses,
                    CatalogueEntryKind.Cosmetic,
                    2000,
                    0,
                    true,
                    "cosmetic.work_glasses.name",
                    "cosmetic.work_glasses.description"),
            ]);
            var economy = new EconomyService(progress, catalogue);
            var library = new CharacterLibraryIndex(new CharacterFileSystem(), context.Root);
            Guid baselineId = Guid.Parse("8b400000-0000-4000-8000-000000000001");
            await context.Store.SaveAsync(
                CharacterDocument.CreateDefault(baselineId, "Studio Baseline"),
                CancellationToken.None);
            var session = new CharacterEditorSession(
                context.Store,
                library,
                context.Coordinator,
                context.Preview,
                economy: economy);
            await session.SelectAsync(baselineId);

            CharacterEditorActionResult previewed = session.SelectCosmetic(
                CharacterFeatureSlot.Glasses,
                CharacterFeatureIds.GlassesWorkClassic);
            Rgba32 savedColor = CharacterDocumentEditor.ReadFeatureColor(
                session.WorkingDocument!, CharacterFeatureSlot.Glasses);
            var previewTint = new Rgba32(90, 45, 120);
            CharacterEditorActionResult tinted = session.SetFeatureColor(
                CharacterFeatureSlot.Glasses,
                previewTint);
            CharacterEditorActionResult blockedSave = await session.SaveAsync();
            bool previewOnly =
                previewed.Completed && tinted.Completed && session.HasUnownedPreviews && !session.CanSave &&
                session.IsDirty && !blockedSave.Completed &&
                CharacterDocumentEditor.ReadFeatureId(
                    session.WorkingDocument!, CharacterFeatureSlot.Glasses) == CharacterFeatureIds.GlassesNone &&
                CharacterDocumentEditor.ReadFeatureId(
                    session.PreviewDocument!, CharacterFeatureSlot.Glasses) == CharacterFeatureIds.GlassesWorkClassic &&
                CharacterDocumentEditor.ReadFeatureColor(
                    session.WorkingDocument!, CharacterFeatureSlot.Glasses) == savedColor &&
                CharacterDocumentEditor.ReadFeatureColor(
                    session.PreviewDocument!, CharacterFeatureSlot.Glasses) == previewTint;
            checks.Add(new StartupCheck(
                "bs4_unowned_selection_is_preview_only_and_blocks_save",
                previewOnly,
                $"preview={session.HasUnownedPreviews} can_save={session.CanSave} error={blockedSave.Detail}"));

            CharacterEditorActionResult bought = session.BuyPreviewedCosmetic(CharacterFeatureSlot.Glasses);
            await saves.FlushProgressAsync();
            bool purchaseCommitted =
                bought.Completed && economy.IsUnlocked(ContentIds.CosmeticWorkGlasses) &&
                !session.HasUnownedPreviews && session.CanSave &&
                session.LastCosmeticPurchase?.Status == PurchaseStatus.Purchased &&
                CharacterDocumentEditor.ReadFeatureId(
                    session.WorkingDocument!, CharacterFeatureSlot.Glasses) == CharacterFeatureIds.GlassesWorkClassic &&
                progressStore.Progress?.UnlockedToolIds.Contains(
                    ContentIds.CosmeticWorkGlasses,
                    StringComparer.Ordinal) == true;
            checks.Add(new StartupCheck(
                "bs4_preview_buy_uses_existing_economy_and_durable_unlock",
                purchaseCommitted,
                $"owned={economy.IsUnlocked(ContentIds.CosmeticWorkGlasses)} writes={progressStore.ProgressWriteCount}"));

            CharacterEditorActionResult close = session.RequestClose();
            await session.ResolveUnsavedAsync(UnsavedDecision.Discard);
            bool purchaseSurvivesCancel =
                close.NeedsUnsavedDecision && economy.IsUnlocked(ContentIds.CosmeticWorkGlasses) &&
                CharacterDocumentEditor.ReadFeatureId(
                    session.WorkingDocument!, CharacterFeatureSlot.Glasses) == CharacterFeatureIds.GlassesNone;
            checks.Add(new StartupCheck(
                "bs4_cancel_reverts_appearance_but_never_refunds_purchase",
                purchaseSurvivesCancel,
                $"owned={economy.IsUnlocked(ContentIds.CosmeticWorkGlasses)} glasses={CharacterDocumentEditor.ReadFeatureId(session.WorkingDocument!, CharacterFeatureSlot.Glasses)}"));

            Guid wornId = Guid.Parse("8b400000-0000-4000-8000-000000000002");
            CharacterDocument worn = CharacterDocumentEditor.SetFeatureId(
                CharacterDocument.CreateDefault(wornId, "Worn After Reset"),
                CharacterFeatureSlot.Glasses,
                CharacterFeatureIds.GlassesWorkClassic);
            await context.Store.SaveAsync(worn, CancellationToken.None);
            var resetProgress = new BuddyProgressState(cashPerPain: 0.01);
            var resetEconomy = new EconomyService(resetProgress, catalogue);
            var resetSession = new CharacterEditorSession(
                context.Store,
                library,
                context.Coordinator,
                context.Preview,
                economy: resetEconomy);
            await resetSession.SelectAsync(wornId);
            CharacterEditorActionResult wornSaved = await resetSession.SaveAsync();
            bool wornSurvivesOwnershipLoss =
                !resetSession.IsCosmeticOwned(CharacterFeatureIds.GlassesWorkClassic) &&
                resetSession.CanSave && wornSaved.Completed &&
                CharacterDocumentEditor.ReadFeatureId(
                    resetSession.WorkingDocument!, CharacterFeatureSlot.Glasses) == CharacterFeatureIds.GlassesWorkClassic;
            checks.Add(new StartupCheck(
                "bs4_worn_cosmetic_survives_ownership_loss",
                wornSurvivesOwnershipLoss,
                $"owned={resetSession.IsCosmeticOwned(CharacterFeatureIds.GlassesWorkClassic)} can_save={resetSession.CanSave}"));

            resetSession.SelectCosmetic(CharacterFeatureSlot.Glasses, CharacterFeatureIds.GlassesNone);
            resetSession.SelectCosmetic(CharacterFeatureSlot.Glasses, CharacterFeatureIds.GlassesWorkClassic);
            bool deselectionIsOneWay =
                resetSession.HasUnownedPreviews && !resetSession.CanSave &&
                CharacterDocumentEditor.ReadFeatureId(
                    resetSession.WorkingDocument!, CharacterFeatureSlot.Glasses) == CharacterFeatureIds.GlassesNone;
            checks.Add(new StartupCheck(
                "bs4_deselected_unowned_worn_item_requires_repurchase",
                deselectionIsOneWay,
                $"preview={resetSession.HasUnownedPreviews} working={CharacterDocumentEditor.ReadFeatureId(resetSession.WorkingDocument!, CharacterFeatureSlot.Glasses)}"));

            long balanceBeforeRandomize = resetEconomy.BalanceMilliCredits;
            long progressRevisionBeforeRandomize = resetProgress.Revision;
            CharacterPaintManifest paintBeforeRandomize = resetSession.WorkingDocument!.Paint;
            CharacterEditorActionResult randomized = resetSession.Randomize(125);
            bool sessionRandomizeSafe = randomized.Completed &&
                !resetSession.HasUnownedPreviews && resetSession.CanSave &&
                resetSession.LastRandomSeed == 125 &&
                !resetSession.IsCosmeticOwned(CharacterFeatureIds.GlassesWorkClassic) &&
                resetEconomy.BalanceMilliCredits == balanceBeforeRandomize &&
                resetProgress.Revision == progressRevisionBeforeRandomize &&
                resetSession.WorkingDocument.Paint == paintBeforeRandomize;
            foreach (CharacterFeatureSlot slot in Enum.GetValues<CharacterFeatureSlot>().Distinct())
            {
                CosmeticDefinition selected = CharacterFeatureCatalog.Shipped.ResolveDefinition(
                    slot,
                    CharacterDocumentEditor.ReadFeatureId(resetSession.WorkingDocument, slot),
                    out bool selectedKnown);
                sessionRandomizeSafe &= selectedKnown && selected.IsFreeDefault;
            }
            checks.Add(new StartupCheck(
                "bs5_session_randomize_discards_preview_without_purchase",
                sessionRandomizeSafe,
                $"preview={resetSession.HasUnownedPreviews} balance={balanceBeforeRandomize}->{resetEconomy.BalanceMilliCredits} revision={progressRevisionBeforeRandomize}->{resetProgress.Revision}"));
        }
        finally
        {
            await CharacterEditorScenarioSupport.Cleanup(tree, context);
        }
        return CharacterEditorScenarioSupport.Result(checks, seed);
    }
}
