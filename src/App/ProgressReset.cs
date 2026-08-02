using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;

namespace DesktopBuddy.App;

/// <summary>
/// "Reset Progress": the whole gameplay save goes back to a first run, and nothing else
/// moves. Settings are preserved for free — progress and local settings are two independent
/// payloads written by two independent calls, and this path writes only the progress one, so
/// language, audio, controls, accessibility, comfort, presentation, window, zoom, and dock
/// preferences survive because nothing here touches them.
///
/// <para>
/// The reset is applied <b>in place</b> through <see cref="BuddyProgressState.Adopt"/>, so
/// every service and presenter composed at startup keeps reading the same instance and no
/// re-binding pass is needed. A failed write is rolled back to the exact prior snapshot, so a
/// reset that could not be persisted mutates nothing in memory or on disk. The save file is
/// never deleted, on either path.
/// </para>
///
/// <para>
/// ponytail: no achievements subsystem exists, so there is deliberately no
/// "preserve awarded achievements" path here — a preservation path for a system that does not
/// exist is untestable and would rot. The guard is
/// <c>ProgressResetTests.ResetMatrix_HasNoAchievementSurfaceYet</c>: when achievements land it
/// fails and forces whoever adds them back to the 13A-3 reset matrix.
/// </para>
/// </summary>
public static class ProgressReset
{
    /// <summary>
    /// The one first-run progress factory. Both a brand-new player (<see cref="Bootstrap"/>)
    /// and a reset player come through here, so the two states cannot drift apart.
    /// </summary>
    public static BuddyProgressState CreateNewProgress(double cashPerPain)
    {
        ulong traitSeed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        BuddyTraits traits = BuddyTraits.Sample(new SeededRandomSource(traitSeed));
        return new BuddyProgressState(cashPerPain, traits: traits);
    }

    /// <summary>
    /// Resets <paramref name="progress"/> to a first run and durably writes it. Returns
    /// <c>false</c> — having mutated nothing — when the write fails.
    /// </summary>
    public static async Task<bool> ResetAsync(
        BuddyProgressState progress,
        SaveCoordinator saves,
        EconomyService? economy = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(saves);

        ProgressSnapshot before = progress.Snapshot();
        ProgressSnapshot fresh = CreateNewProgress(progress.CashPerPain).Snapshot();
        // Revision only ever moves forward: the save coordinator's dirty tracking compares it.
        progress.Adopt(fresh with { Revision = before.Revision + 1 });

        try
        {
            await saves.FlushProgressAsync(force: true, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            progress.Adopt(before);
            return false;
        }

        economy?.NotifyBalanceChanged();
        return true;
    }
}
