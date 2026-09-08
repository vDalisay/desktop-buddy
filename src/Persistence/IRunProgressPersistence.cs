using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Minimal run-lifetime semantic save surface shared by the legacy Initial Demo aggregate and the
/// Scene-enabled split graph. Settings and authored binary assets keep their focused stores; this
/// interface exists only so lifecycle/one-shot rewards can request semantic autosave/flush without
/// knowing which persistence model the active build uses.
/// </summary>
public interface IRunProgressPersistence
{
    bool IsDirty { get; }
    Task TickAsync(double validRunningSeconds, CancellationToken token = default);

    /// <summary>
    /// Flushes the current semantic generation. Ordinary checkpoints use the historical non-forced
    /// behavior; shutdown callers pass <c>force: true</c> so a mutation that lands during an active
    /// flush receives one final pass before exit.
    /// </summary>
    Task FlushAsync(bool force = false, CancellationToken token = default);
}
