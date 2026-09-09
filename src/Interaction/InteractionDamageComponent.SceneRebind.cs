using System;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;

namespace DesktopBuddy.Interaction;

public partial class InteractionDamageComponent
{
    /// <summary>
    /// Rebinds the authored compatibility actor to another persistent Buddy without rebuilding the
    /// node or subscribing its recovery events a second time. Scene switching persists the outgoing
    /// Buddy before calling this seam, then clears only transient impact/knockout/care state.
    /// The account ledger must remain the exact one already owned by the run-wide EconomyService.
    /// </summary>
    public void RebindProgress(BuddyRuntimeProgressBinding progress, EconomyService economy)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(economy);
        RequireInitialized();

        if (progress.IsSplit)
        {
            PlayerProgressState player = progress.PlayerProgress
                ?? throw new ArgumentException("Split progress binding has no player state.", nameof(progress));
            if (!economy.IsBackedBy(player))
            {
                throw new ArgumentException(
                    "Damage pipeline split binding and economy must share the same player ledger.",
                    nameof(economy));
            }
        }
        else
        {
            BuddyProgressState legacy = progress.LegacyProgress
                ?? throw new ArgumentException("Legacy progress binding has no aggregate state.", nameof(progress));
            if (!economy.IsBackedBy(legacy))
            {
                throw new ArgumentException(
                    "Damage pipeline progress and economy must reference the same legacy ledger.",
                    nameof(economy));
            }
        }

        ResetTransientState();
        _progress = progress;
        _economy = economy;
        Buddy.SetConsciousness(Consciousness.Conscious);
    }
}
