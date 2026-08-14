namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    // New category/maintenance partials were introduced while the original main class still owns
    // RepositoryRoot(). Keep a single compatibility seam so both paths resolve the exact same root
    // instead of duplicating path logic across UI slices.
    private string FindRepositoryRoot() => RepositoryRoot();
}
