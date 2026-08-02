using System;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>
/// Focused persistent companion to BuddyProgressState. It owns only active character
/// selection and a revision so the existing progress writer can save both atomically.
/// </summary>
public sealed class CharacterSelectionState
{
    public CharacterSelectionState(Guid? activeCharacterId = null, long revision = 0)
    {
        if (activeCharacterId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(activeCharacterId));
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ActiveCharacterId = activeCharacterId;
        Revision = revision;
    }

    public event Action<Guid?>? Changed;

    public Guid? ActiveCharacterId { get; private set; }
    public long Revision { get; private set; }

    public bool SetActive(Guid? id)
    {
        if (id == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (ActiveCharacterId == id)
            return false;

        ActiveCharacterId = id;
        Revision++;
        Changed?.Invoke(id);
        return true;
    }

    public void Adopt(CharacterSelectionState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ActiveCharacterId = source.ActiveCharacterId;
        Revision++;
        Changed?.Invoke(ActiveCharacterId);
    }

    public void ResetToBuiltIn() => SetActive(null);
}
