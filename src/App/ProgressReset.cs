using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.App;

/// <summary>
/// "Reset Progress": the whole gameplay save goes back to a first run, and nothing else
/// moves. Settings and local character files are preserved; active character selection is
/// cloud progress and therefore resets to built-in.
/// </summary>
public static class ProgressReset
{
    public static BuddyProgressState CreateNewProgress(double cashPerPain)
    {
        ulong traitSeed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        BuddyTraits traits = BuddyTraits.Sample(new SeededRandomSource(traitSeed));
        return new BuddyProgressState(cashPerPain, traits: traits);
    }

    /// <summary>
    /// Resets gameplay state and optional character selection in place. A failed durable
    /// write restores both exact prior snapshots; local character documents are untouched.
    /// </summary>
    public static async Task<bool> ResetAsync(
        BuddyProgressState progress,
        SaveCoordinator saves,
        EconomyService? economy = null,
        CharacterSelectionState? characterSelection = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(saves);
        characterSelection ??= saves.CharacterSelection;

        ProgressSnapshot before = progress.Snapshot();
        CharacterSelectionSnapshot? selectionBefore = characterSelection?.Snapshot();
        ProgressSnapshot fresh = CreateNewProgress(progress.CashPerPain).Snapshot();
        progress.Adopt(fresh with { Revision = before.Revision + 1 });
        characterSelection?.ResetToBuiltIn();

        try
        {
            await saves.FlushProgressAsync(force: true, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            progress.Adopt(before);
            if (characterSelection is not null && selectionBefore.HasValue)
                characterSelection.Restore(selectionBefore.Value);
            return false;
        }

        economy?.NotifyBalanceChanged();
        return true;
    }
}
