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
    Task FlushAsync(bool force, CancellationToken token = default);
}
