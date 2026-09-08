using System;
using DesktopBuddy.Persistence;

namespace DesktopBuddy.Environment;

public partial class EnvironmentBackgroundEditor
{
    /// <summary>
    /// Rebinds a closed Paint Background workspace to another Scene-owned store and refreshes the
    /// visible canvas from that store. An open/saving editor stays pinned to its original Scene so
    /// unsaved strokes can never be redirected silently.
    /// </summary>
    internal bool TryRebindStore(EnvironmentPaintStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (IsOpen || _saving)
            return false;

        _store = store;
        if (_store.Load() is byte[] painted)
            Canvas.Replace(painted);
        else
        {
            Canvas.Reset();
            Canvas.MarkSaved();
        }
        _baseline = null;
        return true;
    }
}
