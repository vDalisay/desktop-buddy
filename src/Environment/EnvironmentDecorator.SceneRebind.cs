using System;
using DesktopBuddy.Persistence;

namespace DesktopBuddy.Environment;

public partial class EnvironmentDecorator
{
    /// <summary>
    /// Rebinds a closed workspace to another Scene persistence capability. An open or saving editor
    /// stays pinned to the Scene it opened against; its Scene adapter rejects any stale commit.
    /// </summary>
    internal bool TryRebindPersistence(IEnvironmentProgressPersistence persistence)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        if (IsOpen || _saving)
            return false;
        _persistence = persistence;
        return true;
    }
}
