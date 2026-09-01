using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.CharacterEditor;

public enum UnsavedDecision { Save, Discard, Cancel }
public enum CharacterEditorPendingAction { None, Close, Select, New, Duplicate, Delete, NewPrompt }

public readonly record struct CharacterEditorActionResult(
    bool Completed,
    bool NeedsUnsavedDecision = false,
    string? Detail = null);

/// <summary>
/// Character-editor working-copy state machine. The feature catalogue is injected and immutable
/// for the lifetime of the session so shipped and Asset Forge-generated definitions are resolved
/// through the same validation/preview/purchase/save path without mutable global registration.
/// </summary>
public sealed class CharacterEditorSession
{
    private const string PaletteKey = "palette";

    private readonly CharacterStore _store;
    private readonly CharacterLibraryIndex _library;
    private readonly CharacterSelectionCoordinator _selection;
    private readonly BuddyVisualRigView _preview;
    private readonly Func<Guid> _newGuid;
    private readonly EconomyService? _economy;
    private readonly CharacterFeatureCatalog _featureCatalog;
    private readonly Dictionary<CharacterFeatureSlot, CharacterFeatureDocument> _unownedPreviews = [];
    private readonly Dictionary<CharacterFeatureSlot, CharacterFeatureDocument> _ownedPreviews = [];
    private CharacterDocument? _savedDocument;
    private CharacterEditorPendingAction _pendingAction;
    private Guid? _pendingCharacterId;
    private CharacterPaintStore? _paintStore;
    private PaintWorkspace? _paintWorkspace;
    private Dictionary<PaintPart, byte[]> _savedPaint = [];

    public CharacterEditorSession(
        CharacterStore store,
        CharacterLibraryIndex library,
        CharacterSelectionCoordinator selection,
        BuddyVisualRigView preview,
        Func<Guid>? newGuid = null,
        EconomyService? economy = null,
        CharacterFeatureCatalog? featureCatalog = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _newGuid = newGuid ?? Guid.NewGuid;
        _economy = economy;
        _featureCatalog = featureCatalog ?? BuddyGeneratedCosmeticRegistry.Current.FeatureCatalog;
    }

    public event Action? Changed;
    public event Action? LibraryChanged;
    public event Action<bool>? CloseResolved;

    public CharacterFeatureCatalog FeatureCatalog => _featureCatalog;
    public CharacterDocument? WorkingDocument { get; private set; }
    public CharacterDocument? PreviewDocument => BuildPreviewDocument();
    public Guid? SelectedCharacterId => WorkingDocument?.Id;
    public bool IsDirty => WorkingDocument is not null &&
        ((_savedDocument is null || !string.Equals(
            CharacterDocumentEditor.Canonical(WorkingDocument),
            CharacterDocumentEditor.Canonical(_savedDocument),
            StringComparison.Ordinal)) || (_paintWorkspace?.IsDirty ?? false));
    public bool HasUnownedPreviews => _unownedPreviews.Count > 0;
    public bool HasOwnedPreviews => _ownedPreviews.Count > 0;
    public bool CanSave => WorkingDocument is not null;
    public IReadOnlyCollection<CharacterFeatureSlot> UnownedPreviewSlots =>
        _unownedPreviews.Keys.OrderBy(static slot => slot).ToArray();
    public bool HasOwnedPreview(CharacterFeatureSlot slot) =>
        _ownedPreviews.ContainsKey(CanonicalSlot(slot));
    public bool HasUnownedPreview(CharacterFeatureSlot slot) =>
        _unownedPreviews.ContainsKey(CanonicalSlot(slot));
    public PurchaseResult? LastCosmeticPurchase { get; private set; }
    public CharacterEditorPendingAction PendingAction => _pendingAction;
    public IReadOnlyList<CharacterIndexEntry> CurrentPage { get; private set; } = [];
    public int PageOffset { get; private set; }
    public int PageSize { get; private set; } = 24;
    public ulong LastRandomSeed { get; private set; }
    public string? LastError { get; private set; }

    public async Task AttachPaintingAsync(
        CharacterPaintStore paintStore,
        PaintWorkspace workspace,
        CancellationToken token = default)
    {
        _paintStore = paintStore ?? throw new ArgumentNullException(nameof(paintStore));
        _paintWorkspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        if (WorkingDocument is not null)
        {
            if (OperatingSystem.IsBrowser())
                LoadPaintBrowserSynchronously(WorkingDocument.Id, token);
            else
                await LoadPaintAsync(WorkingDocument.Id, token);
        }
        Changed?.Invoke();
    }

    public async Task RefreshPageAsync(int offset, int count = 24, CancellationToken token = default)
    {
        if (OperatingSystem.IsBrowser())
        {
            RefreshPageBrowserSynchronously(offset, count, token);
            return;
        }

        CurrentPage = await _library.ReadPageAsync(offset, count, token);
        PageOffset = offset;
        PageSize = count;
        LibraryChanged?.Invoke();
    }

    public async Task<CharacterEditorActionResult> SelectAsync(Guid characterId, CancellationToken token = default)
    {
        CancelTransientPaintPreview();
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.Select, characterId);
        if (OperatingSystem.IsBrowser())
            return SelectCoreBrowserSynchronously(characterId, token);
        return await SelectCoreAsync(characterId, token);
    }

    public async Task<CharacterEditorActionResult> OpenActiveAsync(
        Guid? activeCharacterId,
        CancellationToken token = default)
    {
        CancelTransientPaintPreview();
        if (activeCharacterId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(activeCharacterId));
        if (activeCharacterId.HasValue)
        {
            if (WorkingDocument?.Id == activeCharacterId.Value)
                return new CharacterEditorActionResult(true);
            if (IsDirty)
                return RequireDecision(CharacterEditorPendingAction.Select, activeCharacterId);
            if (OperatingSystem.IsBrowser())
                return SelectCoreBrowserSynchronously(activeCharacterId.Value, token);
            return await SelectCoreAsync(activeCharacterId.Value, token);
        }

        if (IsDirty)
            return RequireDecision(CharacterEditorPendingAction.New, null);
        ClearPaint(saved: false);
        SetWorking(
            CharacterDocument.CreateDefault(_newGuid(), "Built-in Buddy"),
            saved: null,
            clearPreviews: true);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult NewCharacter(string displayName = "New Character")
    {
        CancelTransientPaintPreview();
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.New, null);
        ClearPaint(saved: false);
        SetWorking(CharacterDocument.CreateDefault(_newGuid(), displayName), saved: null, clearPreviews: true);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult RequestNewCharacterPrompt()
    {
        CancelTransientPaintPreview();
        return IsDirty
            ? RequireDecision(CharacterEditorPendingAction.NewPrompt, null)
            : new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult Duplicate(string? displayName = null)
    {
        CancelTransientPaintPreview();
        if (WorkingDocument is null) return Failure("Select a character before duplicating it.");
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.Duplicate, null);
        string name = string.IsNullOrWhiteSpace(displayName)
            ? $"{WorkingDocument.DisplayName} Copy" : displayName.Trim();
        CharacterDocument duplicate = CharacterDocumentEditor.WithIdentity(WorkingDocument, _newGuid(), name);
        SetWorking(duplicate with { Paint = CharacterPaintManifest.Empty }, saved: null, clearPreviews: true);
        if (_paintWorkspace is not null)
            _paintWorkspace.MarkDirty();
        return new CharacterEditorActionResult(true);
    }

    public IReadOnlyList<string> Palette =>
        WorkingDocument is not null &&
        WorkingDocument.ExtensionData.TryGetValue(PaletteKey, out System.Text.Json.JsonElement stored) &&
        stored.ValueKind == System.Text.Json.JsonValueKind.Array
            ? stored.EnumerateArray()
                .Where(item => item.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToArray()
            : [];

    public CharacterEditorActionResult SetPalette(IReadOnlyList<string> hexColors) => Mutate(document =>
    {
        var extensionData = new Dictionary<string, System.Text.Json.JsonElement>(
            document.ExtensionData,
            StringComparer.Ordinal)
        {
            [PaletteKey] = System.Text.Json.JsonSerializer.SerializeToElement(hexColors),
        };
        return document with { ExtensionData = extensionData };
    });

    public CharacterEditorActionResult Rename(string displayName) =>
        WorkingDocument is null
            ? Failure("Select a character before renaming it.")
            : Mutate(document => CharacterDocumentEditor.Rename(document, displayName));

    public CharacterEditorActionResult ResetWorkingCopy()
    {
        CancelTransientPaintPreview();
        if (_savedDocument is null) return Failure("This character has not been saved yet.");
        RestoreSavedPaint();
        SetWorking(_savedDocument, _savedDocument, clearPreviews: true);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult Randomize(ulong seed)
    {
        if (WorkingDocument is null)
            return Failure("There is no working character to randomize.");
        LastRandomSeed = seed;
        try
        {
            var owned = new HashSet<string>(StringComparer.Ordinal);
            if (_economy is not null)
            {
                foreach (string cosmeticId in _featureCatalog.AllIds)
                    if (_featureCatalog.TryGetDefinition(cosmeticId, out CosmeticDefinition definition) &&
                        definition.OwnershipContentId is string contentId &&
                        _economy.IsUnlocked(contentId))
                        owned.Add(contentId);
            }
            CharacterDocument randomized = CharacterRandomizer.Randomize(
                WorkingDocument,
                _featureCatalog,
                owned,
                seed);
            _unownedPreviews.Clear();
            _ownedPreviews.Clear();
            SetWorking(randomized, _savedDocument);
            return new CharacterEditorActionResult(true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            return Failure(exception.Message);
        }
    }

    public CharacterEditorActionResult SetPartColor(CharacterPartSlot slot, Rgba32 color) =>
        Mutate(document => CharacterDocumentEditor.SetPartColor(document, slot, color));
    public CharacterEditorActionResult SetFeatureId(CharacterFeatureSlot slot, string id) =>
        Mutate(document => CharacterDocumentEditor.SetFeatureId(document, slot, id));
    public CharacterEditorActionResult SetFeatureTransform(CharacterFeatureSlot slot, NormalizedFeatureTransform transform) =>
        MutateFeature(slot, document => CharacterDocumentEditor.SetFeatureTransform(document, slot, transform));
    public CharacterEditorActionResult SetFeatureColor(CharacterFeatureSlot slot, Rgba32 color) =>
        MutateFeature(slot, document => CharacterDocumentEditor.SetFeatureColor(document, slot, color));

    public bool IsCosmeticOwned(string cosmeticId)
    {
        if (!_featureCatalog.TryGetDefinition(cosmeticId, out CosmeticDefinition definition))
            return false;
        return definition.IsFreeDefault ||
            (definition.OwnershipContentId is string contentId &&
             (_economy?.IsUnlocked(contentId) ?? false));
    }

    public CharacterEditorActionResult SelectCosmetic(CharacterFeatureSlot slot, string cosmeticId)
    {
        if (WorkingDocument is null)
            return Failure("There is no working character to edit.");
        if (!_featureCatalog.TryGetDefinition(cosmeticId, out CosmeticDefinition definition) ||
            definition.Slot != CanonicalSlot(slot))
            return Failure($"Cosmetic '{cosmeticId}' does not belong to {slot}.");

        string currentId = CharacterDocumentEditor.ReadFeatureId(WorkingDocument, slot);
        if (!_unownedPreviews.ContainsKey(CanonicalSlot(slot)) &&
            string.Equals(currentId, cosmeticId, StringComparison.Ordinal))
            return new CharacterEditorActionResult(true);

        CharacterDocument selected = ApplyDefinition(WorkingDocument, definition);
        CharacterFeatureSlot canonical = CanonicalSlot(slot);
        if (IsCosmeticOwned(cosmeticId))
        {
            _unownedPreviews.Remove(canonical);
            SetWorking(selected, _savedDocument);
        }
        else
        {
            _unownedPreviews[canonical] = CharacterDocumentEditor.ReadFeatureDocument(selected, canonical);
            LastError = null;
            RefreshPreview();
            Changed?.Invoke();
        }
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult PreviewCosmetic(CharacterFeatureSlot slot, string cosmeticId)
    {
        if (WorkingDocument is null)
            return Failure("There is no working character to edit.");
        if (!_featureCatalog.TryGetDefinition(cosmeticId, out CosmeticDefinition definition) ||
            definition.Slot != CanonicalSlot(slot))
            return Failure($"Cosmetic '{cosmeticId}' does not belong to {slot}.");

        CharacterFeatureSlot canonical = CanonicalSlot(slot);
        string equippedId = CharacterDocumentEditor.ReadFeatureId(WorkingDocument, canonical);
        if (string.Equals(equippedId, cosmeticId, StringComparison.Ordinal))
        {
            _ownedPreviews.Remove(canonical);
            _unownedPreviews.Remove(canonical);
        }
        else
        {
            CharacterFeatureDocument preview = CharacterDocumentEditor.ReadFeatureDocument(
                ApplyDefinition(WorkingDocument, definition), canonical);
            if (IsCosmeticOwned(cosmeticId))
            {
                _unownedPreviews.Remove(canonical);
                _ownedPreviews[canonical] = preview;
            }
            else
            {
                _ownedPreviews.Remove(canonical);
                _unownedPreviews[canonical] = preview;
            }
        }
        LastError = null;
        RefreshPreview();
        Changed?.Invoke();
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult EquipPreviewedCosmetic(CharacterFeatureSlot slot)
    {
        CharacterFeatureSlot canonical = CanonicalSlot(slot);
        if (!_ownedPreviews.TryGetValue(canonical, out CharacterFeatureDocument? preview))
            return Failure($"{slot} has no owned cosmetic preview to equip.");
        if (!IsCosmeticOwned(preview.FeatureId))
            return Failure($"Cosmetic '{preview.FeatureId}' is not owned.");

        _ownedPreviews.Remove(canonical);
        _unownedPreviews.Remove(canonical);
        SetWorking(CharacterDocumentEditor.SetFeatureDocument(WorkingDocument!, canonical, preview), _savedDocument);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult BuyPreviewedCosmetic(CharacterFeatureSlot slot)
    {
        CharacterFeatureSlot canonical = CanonicalSlot(slot);
        if (!_unownedPreviews.TryGetValue(canonical, out CharacterFeatureDocument? preview))
            return Failure($"{slot} has no unowned cosmetic preview to buy.");
        if (_economy is null)
            return Failure("Cosmetic purchases are unavailable in this editor context.");

        CosmeticDefinition definition = _featureCatalog.ResolveDefinition(
            canonical,
            preview.FeatureId,
            out bool known);
        if (!known)
            return Failure($"Cosmetic '{preview.FeatureId}' is not available.");

        if (definition.OwnershipContentId is not string contentId)
            return Failure($"Cosmetic '{definition.Id}' has no authored purchase content ID.");
        PurchaseResult result = _economy.Purchase(contentId);
        LastCosmeticPurchase = result;
        if (!result.Succeeded && result.Status != PurchaseStatus.AlreadyOwned)
            return Failure($"Cosmetic purchase failed: {result.Status}.");

        _unownedPreviews.Remove(canonical);
        _ownedPreviews[canonical] = preview;
        LastError = null;
        RefreshPreview();
        Changed?.Invoke();
        return new CharacterEditorActionResult(true);
    }

    /// <summary>Returns every catalogue-only preview to the equipped working appearance.</summary>
    public void CancelCosmeticPreviews()
    {
        if (_unownedPreviews.Count == 0 && _ownedPreviews.Count == 0) return;
        _unownedPreviews.Clear();
        _ownedPreviews.Clear();
        LastError = null;
        RefreshPreview();
        Changed?.Invoke();
    }

    public async Task<CharacterEditorActionResult> SaveAsync(CancellationToken token = default)
    {
        if (OperatingSystem.IsBrowser())
            return SaveBrowserSynchronously(token);

        CancelTransientPaintPreview();
        if (WorkingDocument is null) return Failure("There is no working character to save.");
        CancelCosmeticPreviews();

        CharacterSaveResult saved;
        if (_paintStore is not null && _paintWorkspace is not null)
        {
            var surfaces = _paintWorkspace.Surfaces.ToDictionary(
                pair => pair.Key,
                pair => (ReadOnlyMemory<byte>)pair.Value.ClonePixels());
            CharacterPaintSaveResult paintSaved = await _paintStore.SaveAsync(WorkingDocument, surfaces, token);
            saved = paintSaved.Character;
        }
        else
        {
            saved = await _store.SaveAsync(WorkingDocument, token);
        }

        if (!saved.IsSuccess || saved.Document is null)
            return Failure(saved.Detail ?? $"Character save failed: {saved.Status}.");

        CaptureSavedPaint();
        _paintWorkspace?.MarkSaved();
        SetWorking(saved.Document, saved.Document, clearPreviews: true);

        // Saving the character the world is currently wearing has to reach the world. Nothing
        // else re-reads it: the runtime activates a character's document and paint once, so
        // painting a buddy, saving it and then leaving without pressing Use Character left the
        // live buddy on the pixels it loaded at startup (owner report 2026-08-19). Re-activating
        // is a no-op for any other character being edited.
        if (_selection.ActiveCharacterId == saved.Document.Id)
            await _selection.QueueUseCharacterAsync(saved.Document.Id, token);

        await RefreshPageAsync(PageOffset, PageSize, token);
        return new CharacterEditorActionResult(true);
    }

    public async Task<CharacterEditorActionResult> UseCharacterAsync(CancellationToken token = default)
    {
        if (OperatingSystem.IsBrowser())
            return UseCharacterBrowserSynchronously(token);

        CancelTransientPaintPreview();
        CommitOwnedCosmeticPreviews();
        CancelCosmeticPreviews();
        CharacterEditorActionResult saved = IsDirty ? await SaveAsync(token) : new CharacterEditorActionResult(true);
        if (!saved.Completed || WorkingDocument is null) return saved;
        CharacterActivationResult activation = await _selection.QueueUseCharacterAsync(WorkingDocument.Id, token);
        return activation.WasQueued
            ? new CharacterEditorActionResult(true)
            : Failure(activation.Detail ?? $"Character activation failed: {activation.Status}.");
    }

    public async Task<CharacterEditorActionResult> DeleteAsync(CancellationToken token = default)
    {
        if (OperatingSystem.IsBrowser())
            return DeleteBrowserSynchronously(token);

        CancelTransientPaintPreview();
        if (WorkingDocument is null) return Failure("Select a character before deleting it.");
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.Delete, WorkingDocument.Id);
        Guid id = WorkingDocument.Id;
        CharacterDeleteResult deleted = await _selection.DeleteCharacterAsync(id, token);
        if (!deleted.IsSuccess) return Failure(deleted.Detail ?? $"Character deletion failed: {deleted.Status}.");
        WorkingDocument = null;
        _savedDocument = null;
        _unownedPreviews.Clear();
        _ownedPreviews.Clear();
        ClearPaint(saved: true);
        RefreshPreview();
        Changed?.Invoke();
        await RefreshPageAsync(PageOffset, PageSize, token);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult RequestClose()
    {
        CancelTransientPaintPreview();
        CancelCosmeticPreviews();
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.Close, null);
        CloseResolved?.Invoke(true);
        return new CharacterEditorActionResult(true);
    }

    public async Task<CharacterEditorActionResult> ResolveUnsavedAsync(
        UnsavedDecision decision,
        CancellationToken token = default)
    {
        if (OperatingSystem.IsBrowser())
            return ResolveUnsavedBrowserSynchronously(decision, token);

        CancelTransientPaintPreview();
        CharacterEditorPendingAction action = _pendingAction;
        Guid? characterId = _pendingCharacterId;
        _pendingAction = CharacterEditorPendingAction.None;
        _pendingCharacterId = null;

        if (decision == UnsavedDecision.Cancel) return new CharacterEditorActionResult(false);
        if (decision == UnsavedDecision.Save)
        {
            CharacterEditorActionResult saved = await SaveAsync(token);
            if (!saved.Completed) return saved;
        }
        else if (_savedDocument is not null)
        {
            RestoreSavedPaint();
            SetWorking(_savedDocument, _savedDocument, clearPreviews: true);
        }
        else
        {
            WorkingDocument = null;
            _savedDocument = null;
            _unownedPreviews.Clear();
            _ownedPreviews.Clear();
            ClearPaint(saved: true);
            RefreshPreview();
            Changed?.Invoke();
        }

        return action switch
        {
            CharacterEditorPendingAction.Close => ResolveClose(),
            CharacterEditorPendingAction.Select when characterId.HasValue => await SelectCoreAsync(characterId.Value, token),
            CharacterEditorPendingAction.New => NewCharacter(),
            CharacterEditorPendingAction.Duplicate => Duplicate(),
            CharacterEditorPendingAction.Delete => await DeleteAsync(token),
            CharacterEditorPendingAction.NewPrompt => new CharacterEditorActionResult(true),
            _ => new CharacterEditorActionResult(true),
        };
    }

    /// <summary>
    /// The experimental single-threaded browser runtime has repeatedly stranded continuations
    /// immediately after a completed persistence await. Browser editor actions therefore execute
    /// the already-inline persistence cores synchronously and return through the public Task API
    /// without ever yielding. Native builds retain the existing worker-thread/async behavior.
    /// </summary>
    private CharacterEditorActionResult SaveBrowserSynchronously(CancellationToken token)
    {
        CancelTransientPaintPreview();
        if (WorkingDocument is null) return Failure("There is no working character to save.");
        CancelCosmeticPreviews();

        CharacterSaveResult saved;
        if (_paintStore is not null && _paintWorkspace is not null)
        {
            var surfaces = _paintWorkspace.Surfaces.ToDictionary(
                pair => pair.Key,
                pair => (ReadOnlyMemory<byte>)pair.Value.ClonePixels());
            CharacterPaintSaveResult paintSaved =
                _paintStore.SaveBrowserSynchronously(WorkingDocument, surfaces, token);
            saved = paintSaved.Character;
        }
        else
        {
            saved = _store.SaveBrowserSynchronously(WorkingDocument, token);
        }

        if (!saved.IsSuccess || saved.Document is null)
            return Failure(saved.Detail ?? $"Character save failed: {saved.Status}.");

        CaptureSavedPaint();
        _paintWorkspace?.MarkSaved();
        SetWorking(saved.Document, saved.Document, clearPreviews: true);

        if (_selection.ActiveCharacterId == saved.Document.Id)
            _selection.QueueUseCharacterBrowserSynchronously(saved.Document.Id, token);

        RefreshPageBrowserSynchronously(PageOffset, PageSize, token);
        return new CharacterEditorActionResult(true);
    }

    private CharacterEditorActionResult UseCharacterBrowserSynchronously(CancellationToken token)
    {
        CancelTransientPaintPreview();
        CommitOwnedCosmeticPreviews();
        CancelCosmeticPreviews();
        CharacterEditorActionResult saved = IsDirty
            ? SaveBrowserSynchronously(token)
            : new CharacterEditorActionResult(true);
        if (!saved.Completed || WorkingDocument is null) return saved;

        CharacterActivationResult activation =
            _selection.QueueUseCharacterBrowserSynchronously(WorkingDocument.Id, token);
        return activation.WasQueued
            ? new CharacterEditorActionResult(true)
            : Failure(activation.Detail ?? $"Character activation failed: {activation.Status}.");
    }

    private CharacterEditorActionResult DeleteBrowserSynchronously(CancellationToken token)
    {
        CancelTransientPaintPreview();
        if (WorkingDocument is null) return Failure("Select a character before deleting it.");
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.Delete, WorkingDocument.Id);
        Guid id = WorkingDocument.Id;
        CharacterDeleteResult deleted = _selection.DeleteCharacterBrowserSynchronously(id, token);
        if (!deleted.IsSuccess) return Failure(deleted.Detail ?? $"Character deletion failed: {deleted.Status}.");
        WorkingDocument = null;
        _savedDocument = null;
        _unownedPreviews.Clear();
        _ownedPreviews.Clear();
        ClearPaint(saved: true);
        RefreshPreview();
        Changed?.Invoke();
        RefreshPageBrowserSynchronously(PageOffset, PageSize, token);
        return new CharacterEditorActionResult(true);
    }

    private CharacterEditorActionResult ResolveUnsavedBrowserSynchronously(
        UnsavedDecision decision,
        CancellationToken token)
    {
        CancelTransientPaintPreview();
        CharacterEditorPendingAction action = _pendingAction;
        Guid? characterId = _pendingCharacterId;
        _pendingAction = CharacterEditorPendingAction.None;
        _pendingCharacterId = null;

        if (decision == UnsavedDecision.Cancel) return new CharacterEditorActionResult(false);
        if (decision == UnsavedDecision.Save)
        {
            CharacterEditorActionResult saved = SaveBrowserSynchronously(token);
            if (!saved.Completed) return saved;
        }
        else if (_savedDocument is not null)
        {
            RestoreSavedPaint();
            SetWorking(_savedDocument, _savedDocument, clearPreviews: true);
        }
        else
        {
            WorkingDocument = null;
            _savedDocument = null;
            _unownedPreviews.Clear();
            _ownedPreviews.Clear();
            ClearPaint(saved: true);
            RefreshPreview();
            Changed?.Invoke();
        }

        return action switch
        {
            CharacterEditorPendingAction.Close => ResolveClose(),
            CharacterEditorPendingAction.Select when characterId.HasValue =>
                SelectCoreBrowserSynchronously(characterId.Value, token),
            CharacterEditorPendingAction.New => NewCharacter(),
            CharacterEditorPendingAction.Duplicate => Duplicate(),
            CharacterEditorPendingAction.Delete => DeleteBrowserSynchronously(token),
            CharacterEditorPendingAction.NewPrompt => new CharacterEditorActionResult(true),
            _ => new CharacterEditorActionResult(true),
        };
    }

    private CharacterEditorActionResult SelectCoreBrowserSynchronously(Guid characterId, CancellationToken token)
    {
        CharacterLoadResult loaded;
        if (_paintStore is not null && _paintWorkspace is not null)
        {
            CharacterPaintLoadResult paintLoaded = _paintStore.LoadBrowserSynchronously(characterId, token);
            loaded = paintLoaded.Character;
            if (loaded.IsSuccess && loaded.Document is not null)
                ApplyLoadedPaint(paintLoaded.Surfaces);
        }
        else
        {
            loaded = _store.LoadBrowserSynchronously(characterId, token);
        }
        if (!loaded.IsSuccess || loaded.Document is null)
            return Failure(loaded.Detail ?? $"Character load failed: {loaded.Status}.");
        SetWorking(loaded.Document, loaded.Document, clearPreviews: true);
        return new CharacterEditorActionResult(true);
    }

    private void LoadPaintBrowserSynchronously(Guid characterId, CancellationToken token)
    {
        if (_paintStore is null || _paintWorkspace is null) return;
        CharacterPaintLoadResult result = _paintStore.LoadBrowserSynchronously(characterId, token);
        if (!result.IsSuccess)
        {
            LastError = result.Detail ?? result.Character.Detail;
            ClearPaint(saved: true);
            return;
        }
        ApplyLoadedPaint(result.Surfaces);
    }

    private void RefreshPageBrowserSynchronously(int offset, int count, CancellationToken token)
    {
        CurrentPage = _library.ReadPageBrowserSynchronously(offset, count, token);
        PageOffset = offset;
        PageSize = count;
        LibraryChanged?.Invoke();
    }

    private async Task<CharacterEditorActionResult> SelectCoreAsync(Guid characterId, CancellationToken token)
    {
        CharacterLoadResult loaded;
        if (_paintStore is not null && _paintWorkspace is not null)
        {
            CharacterPaintLoadResult paintLoaded = await _paintStore.LoadAsync(characterId, token);
            loaded = paintLoaded.Character;
            if (loaded.IsSuccess && loaded.Document is not null)
                ApplyLoadedPaint(paintLoaded.Surfaces);
        }
        else
        {
            loaded = await _store.LoadAsync(characterId, token);
        }
        if (!loaded.IsSuccess || loaded.Document is null)
            return Failure(loaded.Detail ?? $"Character load failed: {loaded.Status}.");
        SetWorking(loaded.Document, loaded.Document, clearPreviews: true);
        return new CharacterEditorActionResult(true);
    }

    private async Task LoadPaintAsync(Guid characterId, CancellationToken token)
    {
        if (_paintStore is null || _paintWorkspace is null) return;
        CharacterPaintLoadResult result = await _paintStore.LoadAsync(characterId, token);
        if (!result.IsSuccess)
        {
            LastError = result.Detail ?? result.Character.Detail;
            ClearPaint(saved: true);
            return;
        }
        ApplyLoadedPaint(result.Surfaces);
    }

    private void ApplyLoadedPaint(IReadOnlyDictionary<PaintPart, byte[]> surfaces)
    {
        if (_paintWorkspace is null) return;
        byte[] blank = new byte[PaintPolicy.SurfaceBytes];
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
            _paintWorkspace.Load(part, surfaces.TryGetValue(part, out byte[]? pixels) ? pixels : blank);
        CaptureSavedPaint();
        _paintWorkspace.MarkSaved();
    }

    private void RestoreSavedPaint()
    {
        if (_paintWorkspace is null) return;
        byte[] blank = new byte[PaintPolicy.SurfaceBytes];
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
            _paintWorkspace.Load(part, _savedPaint.TryGetValue(part, out byte[]? pixels) ? pixels : blank);
        _paintWorkspace.MarkSaved();
    }

    private void CaptureSavedPaint()
    {
        _savedPaint = _paintWorkspace is null
            ? []
            : _paintWorkspace.Surfaces.ToDictionary(pair => pair.Key, pair => pair.Value.ClonePixels());
    }

    private void ClearPaint(bool saved)
    {
        if (_paintWorkspace is null) return;
        byte[] blank = new byte[PaintPolicy.SurfaceBytes];
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
            _paintWorkspace.Load(part, blank);
        if (saved)
        {
            _savedPaint = [];
            _paintWorkspace.MarkSaved();
        }
        else
        {
            _savedPaint = [];
            _paintWorkspace.MarkDirty();
        }
    }

    private void CancelTransientPaintPreview() => _paintWorkspace?.CancelPreviewTransaction();

    /// <summary>
    /// "Use Character" means the appearance currently visible in the editor. Owned catalogue
    /// previews are therefore promoted into the working document before save/activation. Unowned
    /// previews remain transient and are still discarded. This prevents a generated torso selected
    /// in Buddy Studio from disappearing when Paint Buddy activates the same visible character.
    /// </summary>
    private void CommitOwnedCosmeticPreviews()
    {
        if (WorkingDocument is null || _ownedPreviews.Count == 0)
            return;

        CharacterDocument committed = WorkingDocument;
        foreach ((CharacterFeatureSlot slot, CharacterFeatureDocument feature) in
                 _ownedPreviews.OrderBy(static pair => pair.Key))
            committed = CharacterDocumentEditor.SetFeatureDocument(committed, slot, feature);
        _ownedPreviews.Clear();
        SetWorking(committed, _savedDocument);
    }

    private CharacterEditorActionResult Mutate(Func<CharacterDocument, CharacterDocument> mutation)
    {
        if (WorkingDocument is null) return Failure("There is no working character to edit.");
        try
        {
            SetWorking(mutation(WorkingDocument), _savedDocument);
            return new CharacterEditorActionResult(true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            return Failure(exception.Message);
        }
    }

    private CharacterEditorActionResult MutateFeature(
        CharacterFeatureSlot slot,
        Func<CharacterDocument, CharacterDocument> mutation)
    {
        CharacterFeatureSlot canonical = CanonicalSlot(slot);
        Dictionary<CharacterFeatureSlot, CharacterFeatureDocument>? previews =
            _unownedPreviews.ContainsKey(canonical) ? _unownedPreviews :
            _ownedPreviews.ContainsKey(canonical) ? _ownedPreviews : null;
        if (previews is null)
            return Mutate(mutation);
        try
        {
            CharacterDocument preview = BuildPreviewDocument()!;
            CharacterDocument mutated = mutation(preview);
            previews[canonical] = CharacterDocumentEditor.ReadFeatureDocument(mutated, canonical);
            LastError = null;
            RefreshPreview();
            Changed?.Invoke();
            return new CharacterEditorActionResult(true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            return Failure(exception.Message);
        }
    }

    private CharacterDocument? BuildPreviewDocument()
    {
        if (WorkingDocument is null)
            return null;
        CharacterDocument preview = WorkingDocument;
        foreach ((CharacterFeatureSlot slot, CharacterFeatureDocument feature) in _unownedPreviews)
            preview = CharacterDocumentEditor.SetFeatureDocument(preview, slot, feature);
        foreach ((CharacterFeatureSlot slot, CharacterFeatureDocument feature) in _ownedPreviews)
            preview = CharacterDocumentEditor.SetFeatureDocument(preview, slot, feature);
        return preview;
    }

    private static CharacterDocument ApplyDefinition(
        CharacterDocument document,
        CosmeticDefinition definition)
    {
        Rgba32 legacyColor = definition.ColorChannels.Count > 0
            ? definition.ColorChannels[0].DefaultColor
            : CharacterDocumentEditor.ReadFeatureColor(document, definition.Slot);
        var colors = definition.ColorChannels.ToDictionary(
            channel => channel.Id,
            channel => channel.DefaultColor,
            StringComparer.Ordinal);
        var selected = new CharacterFeatureDocument
        {
            FeatureId = definition.Id,
            OffsetX = definition.DefaultTransform.OffsetX,
            OffsetY = definition.DefaultTransform.OffsetY,
            Scale = definition.DefaultTransform.Scale,
            Color = legacyColor,
            Colors = colors,
        };
        return CharacterDocumentEditor.SetFeatureDocument(document, definition.Slot, selected);
    }

    private static CharacterFeatureSlot CanonicalSlot(CharacterFeatureSlot slot) =>
        slot == CharacterFeatureSlot.TorsoAccent ? CharacterFeatureSlot.Accessories : slot;

    private void SetWorking(
        CharacterDocument working,
        CharacterDocument? saved,
        bool clearPreviews = false)
    {
        WorkingDocument = working;
        _savedDocument = saved;
        if (clearPreviews)
        {
            _unownedPreviews.Clear();
            _ownedPreviews.Clear();
        }
        LastError = null;
        RefreshPreview();
        Changed?.Invoke();
    }

    private void RefreshPreview()
    {
        if (WorkingDocument is null)
        {
            _preview.ApplyBuiltInAppearance();
            _preview.RefreshCharacterCompositors();
            return;
        }
        CharacterCompileResult compiled = CharacterCompiler.Compile(
            BuildPreviewDocument()!,
            _featureCatalog);
        if (!compiled.IsSuccess || compiled.Appearance is null)
        {
            LastError = string.Join("; ", compiled.Errors);
            _preview.ApplyBuiltInAppearance();
        }
        else
        {
            _preview.ApplyAppearance(compiled.Appearance);
        }
        _preview.RefreshCharacterCompositors();
    }

    private CharacterEditorActionResult RequireDecision(CharacterEditorPendingAction action, Guid? target)
    {
        _pendingAction = action;
        _pendingCharacterId = target;
        return new CharacterEditorActionResult(false, NeedsUnsavedDecision: true);
    }

    private CharacterEditorActionResult ResolveClose()
    {
        CloseResolved?.Invoke(true);
        return new CharacterEditorActionResult(true);
    }

    private CharacterEditorActionResult Failure(string detail)
    {
        LastError = detail;
        Changed?.Invoke();
        return new CharacterEditorActionResult(false, Detail: detail);
    }
}
