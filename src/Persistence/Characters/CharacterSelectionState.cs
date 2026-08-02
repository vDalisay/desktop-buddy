using System;

namespace DesktopBuddy.Persistence.Characters;

public readonly record struct CharacterSelectionSnapshot(
    Guid? ActiveCharacterId,
    long Revision);

/// <summary>
/// Focused persistent companion to BuddyProgressState. It owns only active character
/// selection and a revision so the existing progress writer can save both atomically.
/// </summary>
public sealed class CharacterSelectionState
{
    public CharacterSelectionState(Guid? activeCharacterId = null, long revision = 0)
    {
        Validate(activeCharacterId, revision);
        ActiveCharacterId = activeCharacterId;
        Revision = revision;
    }

    public event Action<Guid?>? Changed;

    public Guid? ActiveCharacterId { get; private set; }
    public long Revision { get; private set; }

    public CharacterSelectionSnapshot Snapshot() => new(ActiveCharacterId, Revision);

    public bool SetActive(Guid? id) => SetActiveCore(id, notify: true);

    /// <summary>
    /// Transaction-only mutation for Reset Progress. The caller already owns one explicit
    /// all-or-nothing progress write, so raising the ordinary immediate-save event here
    /// would create a competing write and break rollback ordering.
    /// </summary>
    internal bool SetActiveForExplicitTransaction(Guid? id) =>
        SetActiveCore(id, notify: false);

    public void Adopt(CharacterSelectionState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ActiveCharacterId = source.ActiveCharacterId;
        Revision++;
        Changed?.Invoke(ActiveCharacterId);
    }

    /// <summary>
    /// Rollback-only seam used by all-or-nothing Reset Progress. It restores the exact
    /// prior selection snapshot without creating a new dirty revision or retrying the
    /// failed write through the ordinary selection-change event.
    /// </summary>
    public void Restore(in CharacterSelectionSnapshot snapshot)
    {
        Validate(snapshot.ActiveCharacterId, snapshot.Revision);
        ActiveCharacterId = snapshot.ActiveCharacterId;
        Revision = snapshot.Revision;
    }

    public void ResetToBuiltIn() => SetActive(null);

    private bool SetActiveCore(Guid? id, bool notify)
    {
        if (id == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (ActiveCharacterId == id)
            return false;

        ActiveCharacterId = id;
        Revision++;
        if (notify)
            Changed?.Invoke(id);
        return true;
    }

    private static void Validate(Guid? activeCharacterId, long revision)
    {
        if (activeCharacterId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(activeCharacterId));
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
    }
}
