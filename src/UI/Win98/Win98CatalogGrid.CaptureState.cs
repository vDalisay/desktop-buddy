namespace DesktopBuddy.UI.Win98;

public partial class Win98CatalogGrid
{
    /// <summary>
    /// Refreshes the caller-owned persistent tile accent without rebuilding the catalogue or
    /// disturbing the grid-owned preview selection outline. Buddy Studio uses this to keep the
    /// equipped cosmetic visibly marked while a different cosmetic is being previewed.
    /// </summary>
    public bool SetAccent(string id, bool accented)
    {
        if (!_tiles.TryGetValue(id, out TileParts? parts))
            return false;

        ApplyAccent(parts.Button, accented);
        return true;
    }
}
