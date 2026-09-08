using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>
/// Bridges the existing single-actor Character selection presentation state onto one persistent
/// Scene Buddy identity during the staged multi-Buddy migration. The legacy
/// <see cref="CharacterSelectionState"/> remains the UI/runtime observable; authoritative ownership
/// of the selected Character ID lives on <see cref="BuddyIdentityState"/> and every observed change
/// is flushed through the Scene transaction coordinator.
/// </summary>
public sealed class SceneCharacterSelectionBinding : IDisposable
{
    private readonly SceneProgressCoordinator _scenes;
    private readonly BuddyIdentityId _buddyIdentityId;
    private readonly CharacterSelectionState _selection;
    private bool _disposed;

    public SceneCharacterSelectionBinding(
        SceneProgressCoordinator scenes,
        BuddyIdentityId buddyIdentityId,
        CharacterSelectionState selection)
    {
        _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
        if (!buddyIdentityId.IsValid)
            throw new ArgumentException("Character selection requires a stable Buddy identity.", nameof(buddyIdentityId));
        _buddyIdentityId = buddyIdentityId;
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));

        BuddyIdentityState buddy = ResolveBuddy();
        if (buddy.CharacterId != selection.ActiveCharacterId)
        {
            throw new InvalidOperationException(
                "Character selection compatibility state does not match the authoritative Buddy identity.");
        }

        _selection.Changed += OnSelectionChanged;
    }

    public Exception? LastFailure { get; private set; }

    /// <summary>
    /// Applies the currently selected Character to the authoritative Buddy and persists one Scene
    /// generation. This explicit seam is useful for transactional callers and tests; ordinary UI
    /// selection changes call the same path through the observed-state event.
    /// </summary>
    public async Task FlushAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        BuddyIdentityState buddy = ResolveBuddy();
        buddy.SetCharacter(_selection.ActiveCharacterId);
        await _scenes.FlushAsync(force: true, token).ConfigureAwait(false);
        LastFailure = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _selection.Changed -= OnSelectionChanged;
    }

    private void OnSelectionChanged(Guid? characterId)
    {
        if (_disposed)
            return;

        BuddyIdentityState buddy;
        try
        {
            buddy = ResolveBuddy();
        }
        catch (Exception exception)
        {
            LastFailure = exception;
            return;
        }

        if (!buddy.SetCharacter(characterId))
            return;

        _ = FlushObservedAsync();
    }

    private async Task FlushObservedAsync()
    {
        try
        {
            await _scenes.FlushAsync(force: true).ConfigureAwait(false);
            LastFailure = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // SceneProgressCoordinator keeps the graph dirty on failure. Retaining the failure here
            // mirrors SaveCoordinator's observable immediate-save failure without falling back to
            // the legacy aggregate writer.
            LastFailure = exception;
        }
    }

    private BuddyIdentityState ResolveBuddy()
    {
        if (!_scenes.TryGetBuddy(_buddyIdentityId, out BuddyIdentityState? buddy) || buddy is null)
            throw new InvalidOperationException("Scene progress no longer contains the bound Buddy identity.");
        return buddy;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SceneCharacterSelectionBinding));
    }
}
