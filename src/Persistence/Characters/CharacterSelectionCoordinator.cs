using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Characters;
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
    public bool WasQueued => Status is
        CharacterActivationStatus.Queued or CharacterActivationStatus.BuiltInQueued;
}

public sealed class CharacterSelectionCoordinator
{
    private readonly object _sync = new();
    private readonly CharacterStore _store;
    private readonly CharacterSelectionState _selection;
    private readonly BuddyVisualRigView _rigView;
    private readonly SaveCoordinator _saves;
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
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _rigView = rigView ?? throw new ArgumentNullException(nameof(rigView));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _catalog = catalog ?? CharacterFeatureCatalog.Shipped;
    }

    public long AppliedSequence { get; private set; }
    public Guid? AppliedCharacterId { get; private set; }
    public CharacterActivationStatus? LastFallbackStatus { get; private set; }

    public async Task<CharacterActivationResult> QueueUseCharacterAsync(
        Guid? characterId,
        CancellationToken token)
    {
        if (characterId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        long sequence = Interlocked.Increment(ref _nextSequence);

        if (characterId is null)
        {
            Queue(new PendingActivation(sequence, null, null, PersistSelection: true));
            return new CharacterActivationResult(CharacterActivationStatus.BuiltInQueued, null);
        }

        CharacterLoadResult loaded = await _store.LoadAsync(characterId.Value, token)
            .ConfigureAwait(false);
        if (loaded.Status == CharacterLoadStatus.Cancelled)
            return new CharacterActivationResult(CharacterActivationStatus.Cancelled, characterId);

        if (!loaded.IsSuccess || loaded.Document is null)
        {
            CharacterActivationStatus fallback = loaded.Status switch
            {
                CharacterLoadStatus.NotFound => CharacterActivationStatus.NotFoundFallback,
                CharacterLoadStatus.UnsupportedFutureVersion => CharacterActivationStatus.FutureVersionFallback,
                _ => CharacterActivationStatus.InvalidFallback,
            };
            Queue(new PendingActivation(sequence, null, null, PersistSelection: false, fallback));
            return new CharacterActivationResult(fallback, characterId, loaded.Detail);
        }

        CharacterCompileResult compiled = CharacterCompiler.Compile(loaded.Document, _catalog);
        if (!compiled.IsSuccess || compiled.Appearance is null)
        {
            Queue(new PendingActivation(
                sequence,
                null,
                null,
                PersistSelection: false,
                CharacterActivationStatus.CompileFailedFallback));
            return new CharacterActivationResult(
                CharacterActivationStatus.CompileFailedFallback,
                characterId,
                string.Join("; ", compiled.Errors));
        }

        Queue(new PendingActivation(sequence, characterId, compiled.Appearance, PersistSelection: true));
        return new CharacterActivationResult(CharacterActivationStatus.Queued, characterId);
    }

    public async Task<CharacterActivationResult> LoadStartupAsync(CancellationToken token)
    {
        Guid? selected = _selection.ActiveCharacterId;
        if (selected is null)
        {
            Queue(new PendingActivation(
                Interlocked.Increment(ref _nextSequence), null, null, PersistSelection: false));
            return new CharacterActivationResult(CharacterActivationStatus.BuiltInQueued, null);
        }

        CharacterLoadResult loaded = await _store.LoadAsync(selected.Value, token)
            .ConfigureAwait(false);
        if (loaded.Status == CharacterLoadStatus.Cancelled)
            return new CharacterActivationResult(CharacterActivationStatus.Cancelled, selected);

        if (!loaded.IsSuccess || loaded.Document is null)
        {
            CharacterActivationStatus fallback = loaded.Status switch
            {
                CharacterLoadStatus.NotFound => CharacterActivationStatus.NotFoundFallback,
                CharacterLoadStatus.UnsupportedFutureVersion => CharacterActivationStatus.FutureVersionFallback,
                _ => CharacterActivationStatus.InvalidFallback,
            };
            Queue(new PendingActivation(
                Interlocked.Increment(ref _nextSequence), null, null, PersistSelection: false, fallback));
            return new CharacterActivationResult(fallback, selected, loaded.Detail);
        }

        CharacterCompileResult compiled = CharacterCompiler.Compile(loaded.Document, _catalog);
        if (!compiled.IsSuccess || compiled.Appearance is null)
        {
            Queue(new PendingActivation(
                Interlocked.Increment(ref _nextSequence),
                null,
                null,
                PersistSelection: false,
                CharacterActivationStatus.CompileFailedFallback));
            return new CharacterActivationResult(
                CharacterActivationStatus.CompileFailedFallback,
                selected,
                string.Join("; ", compiled.Errors));
        }

        Queue(new PendingActivation(
            Interlocked.Increment(ref _nextSequence), selected, compiled.Appearance, PersistSelection: false));
        return new CharacterActivationResult(CharacterActivationStatus.Queued, selected);
    }

    public async Task<CharacterDeleteResult> DeleteCharacterAsync(
        Guid characterId,
        CancellationToken token)
    {
        CharacterDeleteResult result = await _store.DeleteAsync(characterId, token)
            .ConfigureAwait(false);
        if (result.IsSuccess && _selection.ActiveCharacterId == characterId)
        {
            Queue(new PendingActivation(
                Interlocked.Increment(ref _nextSequence), null, null, PersistSelection: true));
        }
        return result;
    }

    public void PhysicsTick()
    {
        PendingActivation? pending;
        lock (_sync)
        {
            pending = _pending;
            _pending = null;
        }

        if (pending is null)
            return;

        PendingActivation activation = pending.Value;
        if (activation.Appearance is null)
            _rigView.ApplyBuiltInAppearance();
        else
            _rigView.ApplyAppearance(activation.Appearance);
        _rigView.RefreshCharacterCompositors();

        if (activation.PersistSelection)
            _selection.SetActive(activation.CharacterId);
        AppliedCharacterId = activation.CharacterId;
        AppliedSequence = activation.Sequence;
        LastFallbackStatus = activation.FallbackStatus;

        if (activation.PersistSelection)
            _ = FlushSelectionSafelyAsync();
    }

    private void Queue(in PendingActivation activation)
    {
        lock (_sync)
        {
            if (_pending is null || activation.Sequence >= _pending.Value.Sequence)
                _pending = activation;
        }
    }

    private async Task FlushSelectionSafelyAsync()
    {
        try
        {
            await _saves.FlushSelectionImmediatelyAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private readonly record struct PendingActivation(
        long Sequence,
        Guid? CharacterId,
        CompiledCharacterAppearance? Appearance,
        bool PersistSelection,
        CharacterActivationStatus? FallbackStatus = null);
}
