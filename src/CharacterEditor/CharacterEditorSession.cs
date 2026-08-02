using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.CharacterEditor;

public enum UnsavedDecision
{
    Save,
    Discard,
    Cancel,
}

public enum CharacterEditorPendingAction
{
    None,
    Close,
    Select,
    New,
    Duplicate,
    Delete,
}

public readonly record struct CharacterEditorActionResult(
    bool Completed,
    bool NeedsUnsavedDecision = false,
    string? Detail = null);

/// <summary>
/// Phase A working-copy state machine. Every edit mutates only the preview document and
/// preview rig; the live buddy changes exclusively through UseCharacterAsync and the A6
/// fixed-tick selection coordinator.
/// </summary>
public sealed class CharacterEditorSession
{
    private readonly CharacterStore _store;
    private readonly CharacterLibraryIndex _library;
    private readonly CharacterSelectionCoordinator _selection;
    private readonly BuddyVisualRigView _preview;
    private readonly Func<Guid> _newGuid;
    private CharacterDocument? _savedDocument;
    private CharacterEditorPendingAction _pendingAction;
    private Guid? _pendingCharacterId;

    public CharacterEditorSession(
        CharacterStore store,
        CharacterLibraryIndex library,
        CharacterSelectionCoordinator selection,
        BuddyVisualRigView preview,
        Func<Guid>? newGuid = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _newGuid = newGuid ?? Guid.NewGuid;
    }

    public event Action? Changed;
    public event Action? LibraryChanged;
    public event Action<bool>? CloseResolved;

    public CharacterDocument? WorkingDocument { get; private set; }
    public Guid? SelectedCharacterId => WorkingDocument?.Id;
    public bool IsDirty => WorkingDocument is not null &&
        (_savedDocument is null ||
         !string.Equals(
             CharacterDocumentEditor.Canonical(WorkingDocument),
             CharacterDocumentEditor.Canonical(_savedDocument),
             StringComparison.Ordinal));
    public CharacterEditorPendingAction PendingAction => _pendingAction;
    public IReadOnlyList<CharacterIndexEntry> CurrentPage { get; private set; } = [];
    public int PageOffset { get; private set; }
    public int PageSize { get; private set; } = 24;
    public ulong LastRandomSeed { get; private set; }
    public string? LastError { get; private set; }

    public async Task RefreshPageAsync(
        int offset,
        int count = 24,
        CancellationToken token = default)
    {
        CurrentPage = await _library.ReadPageAsync(offset, count, token).ConfigureAwait(false);
        PageOffset = offset;
        PageSize = count;
        LibraryChanged?.Invoke();
    }

    public async Task<CharacterEditorActionResult> SelectAsync(
        Guid characterId,
        CancellationToken token = default)
    {
        if (IsDirty)
            return RequireDecision(CharacterEditorPendingAction.Select, characterId);
        return await SelectCoreAsync(characterId, token).ConfigureAwait(false);
    }

    public CharacterEditorActionResult NewCharacter(string displayName = "New Character")
    {
        if (IsDirty)
            return RequireDecision(CharacterEditorPendingAction.New, null);
        SetWorking(CharacterDocument.CreateDefault(_newGuid(), displayName), saved: null);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult Duplicate(string? displayName = null)
    {
        if (WorkingDocument is null)
            return Failure("Select a character before duplicating it.");
        if (IsDirty)
            return RequireDecision(CharacterEditorPendingAction.Duplicate, null);

        string name = string.IsNullOrWhiteSpace(displayName)
            ? $"{WorkingDocument.DisplayName} Copy"
            : displayName.Trim();
        CharacterDocument duplicate = CharacterDocumentEditor.WithIdentity(
            WorkingDocument,
            _newGuid(),
            name);
        SetWorking(duplicate, saved: null);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult Rename(string displayName)
    {
        if (WorkingDocument is null)
            return Failure("Select a character before renaming it.");
        return Mutate(document => CharacterDocumentEditor.Rename(document, displayName));
    }

    public CharacterEditorActionResult ResetWorkingCopy()
    {
        if (_savedDocument is null)
            return Failure("This character has not been saved yet.");
        SetWorking(_savedDocument, _savedDocument);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult Randomize(ulong seed)
    {
        LastRandomSeed = seed;
        return Mutate(document => CharacterRandomizer.Randomize(document, seed));
    }

    public CharacterEditorActionResult SetPartColor(CharacterPartSlot slot, Rgba32 color) =>
        Mutate(document => CharacterDocumentEditor.SetPartColor(document, slot, color));

    public CharacterEditorActionResult SetFeatureId(CharacterFeatureSlot slot, string id) =>
        Mutate(document => CharacterDocumentEditor.SetFeatureId(document, slot, id));

    public CharacterEditorActionResult SetFeatureTransform(
        CharacterFeatureSlot slot,
        in NormalizedFeatureTransform transform) =>
        Mutate(document => CharacterDocumentEditor.SetFeatureTransform(document, slot, transform));

    public CharacterEditorActionResult SetFeatureColor(CharacterFeatureSlot slot, Rgba32 color) =>
        Mutate(document => CharacterDocumentEditor.SetFeatureColor(document, slot, color));

    public async Task<CharacterEditorActionResult> SaveAsync(
        CancellationToken token = default)
    {
        if (WorkingDocument is null)
            return Failure("There is no working character to save.");

        CharacterSaveResult saved = await _store.SaveAsync(WorkingDocument, token)
            .ConfigureAwait(false);
        if (!saved.IsSuccess || saved.Document is null)
            return Failure(saved.Detail ?? $"Character save failed: {saved.Status}.");

        SetWorking(saved.Document, saved.Document);
        await RefreshPageAsync(PageOffset, PageSize, token).ConfigureAwait(false);
        return new CharacterEditorActionResult(true);
    }

    public async Task<CharacterEditorActionResult> UseCharacterAsync(
        CancellationToken token = default)
    {
        CharacterEditorActionResult saved = IsDirty
            ? await SaveAsync(token).ConfigureAwait(false)
            : new CharacterEditorActionResult(true);
        if (!saved.Completed || WorkingDocument is null)
            return saved;

        CharacterActivationResult activation = await _selection.QueueUseCharacterAsync(
            WorkingDocument.Id,
            token).ConfigureAwait(false);
        return activation.WasQueued
            ? new CharacterEditorActionResult(true)
            : Failure(activation.Detail ?? $"Character activation failed: {activation.Status}.");
    }

    public async Task<CharacterEditorActionResult> DeleteAsync(
        CancellationToken token = default)
    {
        if (WorkingDocument is null)
            return Failure("Select a character before deleting it.");
        if (IsDirty)
            return RequireDecision(CharacterEditorPendingAction.Delete, WorkingDocument.Id);

        Guid id = WorkingDocument.Id;
        CharacterDeleteResult deleted = await _selection.DeleteCharacterAsync(id, token)
            .ConfigureAwait(false);
        if (!deleted.IsSuccess)
            return Failure(deleted.Detail ?? $"Character deletion failed: {deleted.Status}.");

        WorkingDocument = null;
        _savedDocument = null;
        RefreshPreview();
        Changed?.Invoke();
        await RefreshPageAsync(PageOffset, PageSize, token).ConfigureAwait(false);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult RequestClose()
    {
        if (IsDirty)
            return RequireDecision(CharacterEditorPendingAction.Close, null);
        CloseResolved?.Invoke(true);
        return new CharacterEditorActionResult(true);
    }

    public async Task<CharacterEditorActionResult> ResolveUnsavedAsync(
        UnsavedDecision decision,
        CancellationToken token = default)
    {
        CharacterEditorPendingAction action = _pendingAction;
        Guid? characterId = _pendingCharacterId;
        _pendingAction = CharacterEditorPendingAction.None;
        _pendingCharacterId = null;

        if (decision == UnsavedDecision.Cancel)
            return new CharacterEditorActionResult(false);
        if (decision == UnsavedDecision.Save)
        {
            CharacterEditorActionResult saved = await SaveAsync(token).ConfigureAwait(false);
            if (!saved.Completed)
                return saved;
        }
        else if (_savedDocument is not null)
        {
            SetWorking(_savedDocument, _savedDocument);
        }
        else
        {
            WorkingDocument = null;
            RefreshPreview();
            Changed?.Invoke();
        }

        return action switch
        {
            CharacterEditorPendingAction.Close => ResolveClose(),
            CharacterEditorPendingAction.Select when characterId.HasValue =>
                await SelectCoreAsync(characterId.Value, token).ConfigureAwait(false),
            CharacterEditorPendingAction.New => NewCharacter(),
            CharacterEditorPendingAction.Duplicate => Duplicate(),
            CharacterEditorPendingAction.Delete => await DeleteAsync(token).ConfigureAwait(false),
            _ => new CharacterEditorActionResult(true),
        };
    }

    private async Task<CharacterEditorActionResult> SelectCoreAsync(
        Guid characterId,
        CancellationToken token)
    {
        CharacterLoadResult loaded = await _store.LoadAsync(characterId, token)
            .ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Document is null)
            return Failure(loaded.Detail ?? $"Character load failed: {loaded.Status}.");
        SetWorking(loaded.Document, loaded.Document);
        return new CharacterEditorActionResult(true);
    }

    private CharacterEditorActionResult Mutate(Func<CharacterDocument, CharacterDocument> mutation)
    {
        if (WorkingDocument is null)
            return Failure("There is no working character to edit.");
        try
        {
            SetWorking(mutation(WorkingDocument), _savedDocument);
            return new CharacterEditorActionResult(true);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or FormatException)
        {
            return Failure(exception.Message);
        }
    }

    private void SetWorking(CharacterDocument working, CharacterDocument? saved)
    {
        WorkingDocument = working;
        _savedDocument = saved;
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
            WorkingDocument,
            CharacterFeatureCatalog.Shipped);
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

    private CharacterEditorActionResult RequireDecision(
        CharacterEditorPendingAction action,
        Guid? target)
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
