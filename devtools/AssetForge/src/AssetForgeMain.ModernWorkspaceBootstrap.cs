namespace DesktopBuddy.AssetForge;

public partial class AssetForgeMain
{
    public override void _PhysicsProcess(double delta)
    {
        EnsureModernWorkspaceUi();
        EnsureCombinedMaintenanceUi();
    }
}
