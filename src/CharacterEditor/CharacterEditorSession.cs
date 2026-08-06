using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.CharacterEditor;

public enum UnsavedDecision { Save, Discard, Cancel }
public enum CharacterEditorPendingAction { None, Close, Select, New, Duplicate, Delete }

public readonly record struct CharacterEditorActionResult(
    bool Completed,
    bool NeedsUnsavedDecision = false,
    string? Detail = null);

/// <summary>
/// Character-editor working-copy state machine. Document and paint edits remain preview-only
/// until Save/Use. Paint pixels participate in the same dirty, discard and failure semantics.
/// Never use ConfigureAwait(false): callers resume into Godot UI code on the main thread.
/// </summary>
public sealed class CharacterEditorSession
{
    private const string PaletteKey = "palette";

    private readonly CharacterStore _store;
    private readonly CharacterLibraryIndex _library;
    private readonly CharacterSelectionCoordinator _selection;
    private readonly BuddyVisualRigView _preview;
    private readonly Func<Guid> _newGuid;
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
        ((_savedDocument is null || !string.Equals(
            CharacterDocumentEditor.Canonical(WorkingDocument),
            CharacterDocumentEditor.Canonical(_savedDocument),
            StringComparison.Ordinal)) || (_paintWorkspace?.IsDirty ?? false));
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
            await LoadPaintAsync(WorkingDocument.Id, token);
        Changed?.Invoke();
    }

    public async Task RefreshPageAsync(int offset, int count = 24, CancellationToken token = default)
    {
        CurrentPage = await _library.ReadPageAsync(offset, count, token);
        PageOffset = offset;
        PageSize = count;
        LibraryChanged?.Invoke();
    }

    public async Task<CharacterEditorActionResult> SelectAsync(Guid characterId, CancellationToken token = default)
    {
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.Select, characterId);
        return await SelectCoreAsync(characterId, token);
    }

    public CharacterEditorActionResult NewCharacter(string displayName = "New Character")
    {
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.New, null);
        ClearPaint(saved: false);
        SetWorking(CharacterDocument.CreateDefault(_newGuid(), displayName), saved: null);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult Duplicate(string? displayName = null)
    {
        if (WorkingDocument is null) return Failure("Select a character before duplicating it.");
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.Duplicate, null);
        string name = string.IsNullOrWhiteSpace(displayName)
            ? $"{WorkingDocument.DisplayName} Copy" : displayName.Trim();
        CharacterDocument duplicate = CharacterDocumentEditor.WithIdentity(WorkingDocument, _newGuid(), name);
        SetWorking(duplicate with { Paint = CharacterPaintManifest.Empty }, saved: null);
        if (_paintWorkspace is not null)
            _paintWorkspace.MarkDirty();
        return new CharacterEditorActionResult(true);
    }

    /// <summary>
    /// Per-character paint palette, carried in the document's extension data so it saves,
    /// loads and participates in the dirty/discard rules without a schema change.
    /// </summary>
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
        if (_savedDocument is null) return Failure("This character has not been saved yet.");
        RestoreSavedPaint();
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
    public CharacterEditorActionResult SetFeatureTransform(CharacterFeatureSlot slot, NormalizedFeatureTransform transform) =>
        Mutate(document => CharacterDocumentEditor.SetFeatureTransform(document, slot, transform));
    public CharacterEditorActionResult SetFeatureColor(CharacterFeatureSlot slot, Rgba32 color) =>
        Mutate(document => CharacterDocumentEditor.SetFeatureColor(document, slot, color));

    public async Task<CharacterEditorActionResult> SaveAsync(CancellationToken token = default)
    {
        if (WorkingDocument is null) return Failure("There is no working character to save.");

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
        SetWorking(saved.Document, saved.Document);
        await RefreshPageAsync(PageOffset, PageSize, token);
        return new CharacterEditorActionResult(true);
    }

    public async Task<CharacterEditorActionResult> UseCharacterAsync(CancellationToken token = default)
    {
        CharacterEditorActionResult saved = IsDirty ? await SaveAsync(token) : new CharacterEditorActionResult(true);
        if (!saved.Completed || WorkingDocument is null) return saved;
        CharacterActivationResult activation = await _selection.QueueUseCharacterAsync(WorkingDocument.Id, token);
        return activation.WasQueued
            ? new CharacterEditorActionResult(true)
            : Failure(activation.Detail ?? $"Character activation failed: {activation.Status}.");
    }

    public async Task<CharacterEditorActionResult> DeleteAsync(CancellationToken token = default)
    {
        if (WorkingDocument is null) return Failure("Select a character before deleting it.");
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.Delete, WorkingDocument.Id);
        Guid id = WorkingDocument.Id;
        CharacterDeleteResult deleted = await _selection.DeleteCharacterAsync(id, token);
        if (!deleted.IsSuccess) return Failure(deleted.Detail ?? $"Character deletion failed: {deleted.Status}.");
        WorkingDocument = null;
        _savedDocument = null;
        ClearPaint(saved: true);
        RefreshPreview();
        Changed?.Invoke();
        await RefreshPageAsync(PageOffset, PageSize, token);
        return new CharacterEditorActionResult(true);
    }

    public CharacterEditorActionResult RequestClose()
    {
        if (IsDirty) return RequireDecision(CharacterEditorPendingAction.Close, null);
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

        if (decision == UnsavedDecision.Cancel) return new CharacterEditorActionResult(false);
        if (decision == UnsavedDecision.Save)
        {
            CharacterEditorActionResult saved = await SaveAsync(token);
            if (!saved.Completed) return saved;
        }
        else if (_savedDocument is not null)
        {
            RestoreSavedPaint();
            SetWorking(_savedDocument, _savedDocument);
        }
        else
        {
            WorkingDocument = null;
            _savedDocument = null;
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
            _ => new CharacterEditorActionResult(true),
        };
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
        SetWorking(loaded.Document, loaded.Document);
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
        CharacterCompileResult compiled = CharacterCompiler.Compile(WorkingDocument, CharacterFeatureCatalog.Shipped);
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
