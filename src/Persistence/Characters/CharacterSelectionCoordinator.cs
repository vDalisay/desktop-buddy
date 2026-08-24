using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Persistence;

namespace DesktopBuddy.Persistence.Characters;

public enum CharacterActivationStatus
{
    Queued,
    BuiltInQueued,
    NotFoundFallback,
    InvalidFallback,
    FutureVersionFallback,
    CompileFailedFallback,
    Cancelled,
}

public readonly record struct CharacterActivationResult(
    CharacterActivationStatus Status,
    Guid? RequestedCharacterId,
    string? Detail = null)
{
    public bool WasQueued => Status is CharacterActivationStatus.Queued or CharacterActivationStatus.BuiltInQueued;
}

/// <summary>
/// Loads, validates, decodes and compiles outside the physics tick, then atomically swaps the
/// narrow visual appearance at the next fixed tick. Prepared paint bytes are published for a
/// main-thread render-frame bridge; no PNG or file work reaches PhysicsTick.
/// </summary>
public sealed class CharacterSelectionCoordinator
{
    private readonly object _sync = new();
    private readonly CharacterStore _store;
    private readonly CharacterPaintStore _paintStore;
    private readonly CharacterSelectionState _selection;
    private readonly BuddyVisualRigView _rigView;
    private readonly CharacterFeatureCatalog _catalog;
    private PendingActivation? _pending;
    private long _nextSequence;

    public CharacterSelectionCoordinator(
        CharacterStore store,
        CharacterSelectionState selection,
        BuddyVisualRigView rigView,
        SaveCoordinator saves,
        CharacterFeatureCatalog? catalog = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _paintStore = store.CreatePaintStore();
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _rigView = rigView ?? throw new ArgumentNullException(nameof(rigView));
        ArgumentNullException.ThrowIfNull(saves);
        if (!ReferenceEquals(saves.CharacterSelection, selection))
            throw new ArgumentException("Coordinator requires the same character selection state.", nameof(saves));
        _catalog = catalog ?? store.FeatureCatalog;
    }

    /// <summary>
    /// The character the save file says is active, which is what the world goes back to
    /// wearing. Not the same as <see cref="AppliedCharacterId"/> while an activation is
    /// queued but not yet swapped in on the fixed tick.
    /// </summary>
    public Guid? ActiveCharacterId => _selection.ActiveCharacterId;

    public long AppliedSequence { get; private set; }
    public Guid? AppliedCharacterId { get; private set; }
    public CharacterActivationStatus? LastFallbackStatus { get; private set; }
    public IReadOnlyDictionary<PaintPart, byte[]> AppliedPaintPayload { get; private set; } =
        new Dictionary<PaintPart, byte[]>();
    public long AppliedPaintSequence { get; private set; }

    public async Task<CharacterActivationResult> QueueUseCharacterAsync(
        Guid? characterId,
        CancellationToken token)
    {
        if (characterId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(characterId));
        long sequence = Interlocked.Increment(ref _nextSequence);
        if (characterId is null)
        {
            Queue(BuiltIn(sequence, persistSelection: true));
            return new CharacterActivationResult(CharacterActivationStatus.BuiltInQueued, null);
        }
        return await PrepareAsync(characterId.Value, sequence, persistSelection: true, token);
    }

    public async Task<CharacterActivationResult> LoadStartupAsync(CancellationToken token)
    {
        Guid? selected = _selection.ActiveCharacterId;
        long sequence = Interlocked.Increment(ref _nextSequence);
        if (selected is null)
        {
            Queue(BuiltIn(sequence, persistSelection: false));
            return new CharacterActivationResult(CharacterActivationStatus.BuiltInQueued, null);
        }
        return await PrepareAsync(selected.Value, sequence, persistSelection: false, token);
    }

    public async Task<CharacterDeleteResult> DeleteCharacterAsync(Guid characterId, CancellationToken token)
    {
        CharacterDeleteResult result = await _store.DeleteAsync(characterId, token).ConfigureAwait(false);
        if (result.IsSuccess && _selection.ActiveCharacterId == characterId)
            Queue(BuiltIn(Interlocked.Increment(ref _nextSequence), persistSelection: true));
        return result;
    }

    /// <summary>
    /// Drops back to the built-in buddy without touching storage. Reset Progress deletes every
    /// character document, so the rig must stop showing one that no longer exists — otherwise
    /// the reset looks like it did nothing at all (owner report 2026-08-21).
    /// </summary>
    public void RevertToBuiltIn() =>
        Queue(BuiltIn(Interlocked.Increment(ref _nextSequence), persistSelection: true));

    /// <summary>Call exactly once from the authoritative fixed-tick route.</summary>
    public void PhysicsTick()
    {
        PendingActivation? pending;
        lock (_sync)
        {
            pending = _pending;
            _pending = null;
        }
        if (pending is null) return;

        PendingActivation activation = pending.Value;
        if (activation.Appearance is null) _rigView.ApplyBuiltInAppearance();
        else _rigView.ApplyAppearance(activation.Appearance);
        _rigView.RefreshCharacterCompositors();

        AppliedPaintPayload = activation.Paint;
        AppliedPaintSequence = activation.Sequence;
        if (activation.PersistSelection) _selection.SetActive(activation.CharacterId);
        AppliedCharacterId = activation.CharacterId;
        AppliedSequence = activation.Sequence;
        LastFallbackStatus = activation.FallbackStatus;
    }

    private async Task<CharacterActivationResult> PrepareAsync(
        Guid characterId,
        long sequence,
        bool persistSelection,
        CancellationToken token)
    {
        CharacterPaintLoadResult loaded = await _paintStore.LoadAsync(characterId, token).ConfigureAwait(false);
        if (loaded.Character.Status == CharacterLoadStatus.Cancelled)
            return new CharacterActivationResult(CharacterActivationStatus.Cancelled, characterId);
        if (!loaded.IsSuccess || loaded.Character.Document is null)
        {
            CharacterActivationStatus fallback = loaded.Character.Status switch
            {
                CharacterLoadStatus.NotFound => CharacterActivationStatus.NotFoundFallback,
                CharacterLoadStatus.UnsupportedFutureVersion => CharacterActivationStatus.FutureVersionFallback,
                _ => CharacterActivationStatus.InvalidFallback,
            };
            Queue(BuiltIn(sequence, persistSelection: false, fallback));
            return new CharacterActivationResult(fallback, characterId, loaded.Detail ?? loaded.Character.Detail);
        }

        CharacterCompileResult compiled = CharacterCompiler.Compile(loaded.Character.Document, _catalog);
        if (!compiled.IsSuccess || compiled.Appearance is null)
        {
            Queue(BuiltIn(sequence, persistSelection: false, CharacterActivationStatus.CompileFailedFallback));
            return new CharacterActivationResult(
                CharacterActivationStatus.CompileFailedFallback,
                characterId,
                string.Join("; ", compiled.Errors));
        }

        Queue(new PendingActivation(
            sequence,
            characterId,
            compiled.Appearance,
            ClonePaint(loaded.Surfaces),
            persistSelection));
        return new CharacterActivationResult(CharacterActivationStatus.Queued, characterId);
    }

    private static PendingActivation BuiltIn(
        long sequence,
        bool persistSelection,
        CharacterActivationStatus? fallback = null) =>
        new(sequence, null, null, new Dictionary<PaintPart, byte[]>(), persistSelection, fallback);

    private static IReadOnlyDictionary<PaintPart, byte[]> ClonePaint(
        IReadOnlyDictionary<PaintPart, byte[]> source)
    {
        var clone = new Dictionary<PaintPart, byte[]>();
        foreach ((PaintPart part, byte[] bytes) in source)
            clone.Add(part, (byte[])bytes.Clone());
        return clone;
    }

    private void Queue(in PendingActivation activation)
    {
        lock (_sync)
        {
            if (_pending is null || activation.Sequence >= _pending.Value.Sequence)
                _pending = activation;
        }
    }

    private readonly record struct PendingActivation(
        long Sequence,
        Guid? CharacterId,
        CompiledCharacterAppearance? Appearance,
        IReadOnlyDictionary<PaintPart, byte[]> Paint,
        bool PersistSelection,
        CharacterActivationStatus? FallbackStatus = null);
}