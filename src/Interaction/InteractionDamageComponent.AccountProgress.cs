using System;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Interaction;

public partial class InteractionDamageComponent
{
    /// <summary>
    /// Returns the account-global progress seam already validated against this pipeline's economy
    /// service. Compatibility UI that still receives a legacy aggregate argument can call this
    /// instead, so Scene runs cannot accidentally render or mutate the stale compatibility object.
    /// </summary>
    public PlayerRuntimeProgressBinding CreatePlayerProgressBinding()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Damage pipeline must be initialized before resolving player progress.");

        if (_progress.PlayerProgress is PlayerProgressState player)
            return new PlayerRuntimeProgressBinding(player);
        if (_progress.LegacyProgress is BuddyProgressState legacy)
            return new PlayerRuntimeProgressBinding(legacy);

        throw new InvalidOperationException("Damage pipeline has no active player progress binding.");
    }
}
